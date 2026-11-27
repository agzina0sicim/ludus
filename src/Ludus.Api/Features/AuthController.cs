using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Ludus.Api.Security;
using Ludus.Application.Security;
using Ludus.Domain.Common;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Persistence;
using Ludus.Infrastructure.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Features;

public sealed record LoginRequest([Required, MaxLength(128)] string Username, [Required, MaxLength(1024)] string Password);
public sealed record AccountSetupRequest([Required, MaxLength(128)] string Username,
    [Required, MinLength(12), MaxLength(1024)] string Password);
public sealed record SecurityRequest(AuthenticationMethod AuthenticationMethod, [Required, MaxLength(128)] string Username, [MaxLength(1024)] string? Password,
    bool TrustForwardedHeaders, [Range(1, 10)] int ForwardLimit, [MaxLength(32)] string[] TrustedProxyNetworks);

[ApiController, Route("api/v1/auth")]
public sealed class AuthController(AppDbContext database, ISecretProtector secrets, IAntiforgery antiforgery,
    AccountManager accounts, LudusPaths paths) : ControllerBase
{
    [HttpGet("csrf"), AllowAnonymous]
    public IActionResult Csrf()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [HttpGet("session"), AllowAnonymous]
    public async Task<IActionResult> Session(CancellationToken cancellationToken)
    {
        var settings = await database.SecuritySettings.AsNoTracking().SingleAsync(cancellationToken);
        if (settings.AccountSetupMode == AccountSetupMode.Complete
            && settings.AuthenticationMethod == AuthenticationMethod.Basic
            && User.Identity?.IsAuthenticated != true)
            return Challenge(ApiAuthentication.SchemeName);
        return Ok(new
        {
            authenticated = User.Identity?.IsAuthenticated == true,
            authenticationMethod = settings.AuthenticationMethod,
            setupRequired = settings.AccountSetupMode == AccountSetupMode.NewInstance,
            localSetupRequired = settings.AccountSetupMode == AccountSetupMode.LegacyProtected
        });
    }

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var settings = await database.SecuritySettings.SingleAsync(cancellationToken);
        var hasher = new PasswordHasher<string>();
        if (settings.AccountSetupMode != AccountSetupMode.Complete || settings.AuthenticationMethod != AuthenticationMethod.Forms
            || settings.Username != request.Username || settings.PasswordHash is null
            || hasher.VerifyHashedPassword(settings.Username, settings.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Unauthorized();
        await HttpContext.SignInAsync(ApiAuthentication.CookieScheme, ApiAuthentication.Principal(settings.SessionVersion, "Password"),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12) });
        return NoContent();
    }

    [HttpPost("setup"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Setup(AccountSetupRequest request, CancellationToken cancellationToken)
    {
        var settings = await database.SecuritySettings.AsNoTracking().SingleAsync(cancellationToken);
        if (settings.AccountSetupMode != AccountSetupMode.NewInstance)
            return Conflict(new { code = "AccountSetupUnavailable" });
        if (string.IsNullOrWhiteSpace(request.Username)) return BadRequest(new { code = "CredentialsRequired" });
        var sessionVersion = await accounts.SetCredentialsAsync(
            request.Username, request.Password, AccountSetupMode.NewInstance, cancellationToken);
        if (sessionVersion is null) return Conflict(new { code = "AccountSetupUnavailable" });
        System.IO.File.Delete(Path.Combine(paths.ConfigDirectory, "bootstrap-api-key"));
        await HttpContext.SignInAsync(ApiAuthentication.CookieScheme, ApiAuthentication.Principal(sessionVersion.Value, "Password"),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12) });
        return NoContent();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(ApiAuthentication.CookieScheme); return NoContent(); }

    [HttpGet("security")]
    public async Task<IActionResult> Security(CancellationToken cancellationToken)
    {
        var value = await database.SecuritySettings.AsNoTracking().SingleAsync(cancellationToken);
        return Ok(new { value.AuthenticationMethod, value.Username, value.TrustForwardedHeaders, value.ForwardLimit, value.TrustedProxyNetworks });
    }

    [HttpPut("security")]
    public async Task<IActionResult> UpdateSecurity(SecurityRequest request, CancellationToken cancellationToken)
    {
        foreach (var network in request.TrustedProxyNetworks) TrustedProxyMiddleware.Validate(network);
        if (request.TrustForwardedHeaders && request.TrustedProxyNetworks.Length == 0) return BadRequest(new { code = "ProxyNetworkRequired" });
        var settings = await database.SecuritySettings.SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Username)
            || request.Password is null && (settings.PasswordHash is null || settings.Username != request.Username)) return BadRequest(new { code = "CredentialsRequired" });
        if (request.Password is not null && request.Password.Length < 12) return BadRequest(new { code = "PasswordTooShort" });
        settings.AuthenticationMethod = request.AuthenticationMethod;
        settings.AccountSetupMode = AccountSetupMode.Complete;
        settings.Username = request.Username.Trim();
        if (request.Password is not null) settings.PasswordHash = new PasswordHasher<string>().HashPassword(request.Username ?? "administrator", request.Password);
        settings.TrustForwardedHeaders = request.TrustForwardedHeaders;
        settings.ForwardLimit = request.ForwardLimit;
        settings.TrustedProxyNetworks = request.TrustedProxyNetworks.ToList();
        settings.SessionVersion = Guid.NewGuid();
        await database.SaveChangesAsync(cancellationToken);
        System.IO.File.Delete(Path.Combine(paths.ConfigDirectory, "bootstrap-api-key"));
        return NoContent();
    }

    [HttpGet("api-key")]
    public async Task<IActionResult> ApiKey(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { apiKey = secrets.Unprotect((await database.SecuritySettings.SingleAsync(cancellationToken)).ApiKey) });
    }

    [HttpPost("api-key/reset")]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        var settings = await database.SecuritySettings.SingleAsync(cancellationToken);
        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        settings.ApiKey = secrets.Protect(key);
        settings.SessionVersion = Guid.NewGuid();
        await database.SaveChangesAsync(cancellationToken);
        System.IO.File.Delete(Path.Combine(paths.ConfigDirectory, "bootstrap-api-key"));
        Response.Headers.CacheControl = "no-store";
        return Ok(new { apiKey = key });
    }
}
