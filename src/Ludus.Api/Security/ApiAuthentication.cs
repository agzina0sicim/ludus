using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Ludus.Application.Security;
using Ludus.Domain.Common;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ludus.Api.Security;

public sealed class ApiAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, AppDbContext database, ISecretProtector secrets, BasicPasswordAttemptLimiter basicLimiter) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Ludus";
    public const string CookieScheme = "LudusSession";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var settings = await database.SecuritySettings.AsNoTracking().SingleAsync(Context.RequestAborted);
        if (Request.Headers.TryGetValue("X-Api-Key", out var supplied))
        {
            if (supplied.Count != 1 || supplied[0]?.Length != 64 || !Matches(secrets.Unprotect(settings.ApiKey), supplied[0]!))
                return AuthenticateResult.Fail("Invalid API key.");
            return Success(settings.SessionVersion, "ApiKey");
        }
        if (settings.AccountSetupMode != AccountSetupMode.Complete)
            return AuthenticateResult.NoResult();
        if (settings.AuthenticationMethod == AuthenticationMethod.Forms)
            return await Context.AuthenticateAsync(CookieScheme);
        if (!Request.Headers.TryGetValue("Authorization", out var authorization)
            || authorization.Count != 1 || !authorization[0]!.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();
        var client = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (basicLimiter.IsBlocked(client))
        {
            Context.Items[BasicPasswordAttemptLimiter.RateLimitedKey] = true;
            return AuthenticateResult.Fail("Too many Basic authentication attempts.");
        }
        string decoded;
        try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authorization[0]![6..].Trim())); }
        catch (FormatException) { basicLimiter.RegisterFailure(client); return AuthenticateResult.Fail("Invalid Basic credentials."); }
        var separator = decoded.IndexOf(':');
        if (separator <= 0 || settings.PasswordHash is null || settings.Username is null)
        {
            basicLimiter.RegisterFailure(client);
            return AuthenticateResult.Fail("Invalid Basic credentials.");
        }
        var username = decoded[..separator];
        var password = decoded[(separator + 1)..];
        var result = new PasswordHasher<string>().VerifyHashedPassword(settings.Username, settings.PasswordHash, password);
        if (!string.Equals(username, settings.Username, StringComparison.Ordinal) || result == PasswordVerificationResult.Failed)
        {
            basicLimiter.RegisterFailure(client);
            return AuthenticateResult.Fail("Invalid Basic credentials.");
        }
        basicLimiter.Reset(client);
        return Success(settings.SessionVersion, "Basic");
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey(BasicPasswordAttemptLimiter.RateLimitedKey))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = "60";
            return;
        }
        var settings = await database.SecuritySettings.AsNoTracking().SingleAsync(Context.RequestAborted);
        if (settings.AccountSetupMode == AccountSetupMode.Complete
            && settings.AuthenticationMethod == AuthenticationMethod.Basic)
            Response.Headers.WWWAuthenticate = "Basic realm=\"Ludus\", charset=\"UTF-8\"";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static AuthenticateResult Success(Guid version, string method) => AuthenticateResult.Success(
        new AuthenticationTicket(Principal(version, method), SchemeName));

    public static bool Matches(string expected, string supplied) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
    public static ClaimsPrincipal Principal(Guid version, string method) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "administrator"), new Claim("sessionVersion", version.ToString()), new Claim("method", method)], SchemeName));

    public static async Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        var database = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var settings = await database.SecuritySettings.AsNoTracking().SingleAsync(context.HttpContext.RequestAborted);
        if (context.Principal?.FindFirst("sessionVersion")?.Value != settings.SessionVersion.ToString()
            || context.Principal?.FindFirst("method")?.Value == "Password"
                && (settings.AccountSetupMode != AccountSetupMode.Complete
                    || settings.AuthenticationMethod != AuthenticationMethod.Forms))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieScheme);
        }
    }
}

public sealed class BasicPasswordAttemptLimiter
{
    public const string RateLimitedKey = "Ludus.BasicRateLimited";
    private const int PermitLimit = 10;
    private static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);

    public bool IsBlocked(string client)
    {
        if (!_windows.TryGetValue(client, out var window)) return false;
        lock (window)
        {
            if (window.ExpiresAtUtc <= DateTime.UtcNow)
            {
                _windows.TryRemove(client, out _);
                return false;
            }
            return window.Failures >= PermitLimit;
        }
    }

    public void RegisterFailure(string client)
    {
        var now = DateTime.UtcNow;
        var window = _windows.GetOrAdd(client, _ => new Window(now.Add(WindowLength)));
        lock (window)
        {
            if (window.ExpiresAtUtc <= now)
            {
                window.ExpiresAtUtc = now.Add(WindowLength);
                window.Failures = 0;
            }
            window.Failures++;
        }
        if (_windows.Count > 4096)
            foreach (var expired in _windows.Where(x => x.Value.ExpiresAtUtc <= now).Take(1024))
                _windows.TryRemove(expired.Key, out _);
    }

    public void Reset(string client) => _windows.TryRemove(client, out _);

    private sealed class Window(DateTime expiresAtUtc)
    {
        public DateTime ExpiresAtUtc { get; set; } = expiresAtUtc;
        public int Failures { get; set; }
    }
}

public sealed class CsrfMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (context.Request.Path.StartsWithSegments("/hubs") && context.Request.Headers.TryGetValue("Origin", out var origin)
            && !string.Equals(origin.ToString(), context.Request.Scheme + "://" + context.Request.Host, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if ((context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
            && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method)
            && !context.Request.Headers.ContainsKey("X-Api-Key"))
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { code = "InvalidCsrfToken" });
                return;
            }
        }
        await next(context);
    }
}
