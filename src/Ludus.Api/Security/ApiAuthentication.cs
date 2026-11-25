using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Ludus.Application.Security;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ludus.Api.Security;

public sealed class ApiAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, AppDbContext database, ISecretProtector secrets) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Ludus";
    public const string CookieScheme = "LudusSession";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var supplied))
            return await Context.AuthenticateAsync(CookieScheme);
        if (supplied.Count != 1 || supplied[0]?.Length != 64) return AuthenticateResult.Fail("Invalid API key.");
        var settings = await database.SecuritySettings.AsNoTracking().SingleAsync(Context.RequestAborted);
        if (!Matches(secrets.Unprotect(settings.ApiKey), supplied[0]!)) return AuthenticateResult.Fail("Invalid API key.");
        return AuthenticateResult.Success(new AuthenticationTicket(Principal(settings.SessionVersion, "ApiKey"), SchemeName));
    }

    public static bool Matches(string expected, string supplied) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
    public static ClaimsPrincipal Principal(Guid version, string method) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "administrator"), new Claim("sessionVersion", version.ToString()), new Claim("method", method)], SchemeName));

    public static async Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        var database = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var settings = await database.SecuritySettings.AsNoTracking().SingleAsync(context.HttpContext.RequestAborted);
        if (context.Principal?.FindFirst("sessionVersion")?.Value != settings.SessionVersion.ToString()
            || context.Principal?.FindFirst("method")?.Value == "Password" && !settings.FormsAuthenticationEnabled)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieScheme);
        }
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
