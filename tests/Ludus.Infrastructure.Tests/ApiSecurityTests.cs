using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ludus.Api.Security;
using Ludus.Domain.Common;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Persistence;
using Ludus.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ludus.Infrastructure.Tests;

public sealed class ApiSecurityTests
{
    [Fact]
    public async Task AccountSetupFormsBasicAndApiKeyAuthenticationRemainIndependent()
    {
        await using var fixture = new ApiFixture();
        using var integration = await fixture.AuthorizedClientAsync();
        using var browser = fixture.Client();

        using var initialSession = await browser.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.OK, initialSession.StatusCode);
        var initialState = await initialSession.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(initialState.GetProperty("setupRequired").GetBoolean());
        using var formsUnauthorized = await browser.GetAsync("/api/v1/games");
        Assert.Equal(HttpStatusCode.Unauthorized, formsUnauthorized.StatusCode);
        Assert.Null(formsUnauthorized.Headers.WwwAuthenticate.SingleOrDefault());

        await CsrfAsync(browser);
        using var setup = await browser.PostAsJsonAsync("/api/v1/auth/setup", new
        {
            username = "administrator",
            password = "correct horse battery staple"
        });
        Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/games")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await integration.GetAsync("/api/v1/games")).StatusCode);

        using var switched = await integration.PutAsJsonAsync("/api/v1/auth/security", new
        {
            authenticationMethod = "Basic",
            username = "administrator",
            password = (string?)null,
            trustForwardedHeaders = true,
            forwardLimit = 1,
            trustedProxyNetworks = new[] { "127.0.0.0/8" }
        });
        Assert.Equal(HttpStatusCode.NoContent, switched.StatusCode);

        using var basicChallenge = await fixture.Client().GetAsync("/api/v1/games");
        Assert.Equal(HttpStatusCode.Unauthorized, basicChallenge.StatusCode);
        Assert.Equal("Basic", Assert.Single(basicChallenge.Headers.WwwAuthenticate).Scheme);

        using var basic = fixture.Client();
        basic.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("administrator:correct horse battery staple")));
        Assert.Equal(HttpStatusCode.OK, (await basic.GetAsync("/api/v1/games")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await integration.GetAsync("/api/v1/games")).StatusCode);
    }

    [Fact]
    public async Task LegacyApiKeyInstallationRejectsAnonymousBrowserSetupAndKeepsApiKeyAccess()
    {
        await using var fixture = new ApiFixture();
        using var integration = await fixture.AuthorizedClientAsync();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var legacySettings = await database.SecuritySettings.SingleAsync();
            legacySettings.AccountSetupMode = AccountSetupMode.LegacyProtected;
            await database.SaveChangesAsync();
        }

        using var browser = fixture.Client();
        var state = await (await browser.GetAsync("/api/v1/auth/session")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(state.GetProperty("setupRequired").GetBoolean());
        Assert.True(state.GetProperty("localSetupRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await integration.GetAsync("/api/v1/games")).StatusCode);
        using var unauthorized = await browser.GetAsync("/api/v1/games");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Null(unauthorized.Headers.WwwAuthenticate.SingleOrDefault());

        await CsrfAsync(browser);
        using var rejected = await browser.PostAsJsonAsync("/api/v1/auth/setup", new
        {
            username = "administrator",
            password = "correct horse battery staple"
        });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        await using var verificationScope = fixture.Factory.Services.CreateAsyncScope();
        var settings = await verificationScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .SecuritySettings.AsNoTracking().SingleAsync();
        Assert.Null(settings.Username);
        Assert.Null(settings.PasswordHash);
    }

    [Fact]
    public async Task BasicAuthenticationFailuresAreRateLimitedWithoutBlockingApiKeys()
    {
        await using var fixture = new ApiFixture();
        using var integration = await fixture.AuthorizedClientAsync();
        using var configured = await integration.PutAsJsonAsync("/api/v1/auth/security", new
        {
            authenticationMethod = "Basic",
            username = "administrator",
            password = "correct horse battery staple",
            trustForwardedHeaders = false,
            forwardLimit = 1,
            trustedProxyNetworks = new[] { "127.0.0.0/8" }
        });
        Assert.Equal(HttpStatusCode.NoContent, configured.StatusCode);
        using var attacker = fixture.Client();
        attacker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("administrator:wrong password")));
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.GetAsync("/api/v1/games")).StatusCode);
        using var limited = await attacker.GetAsync("/api/v1/games");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", Assert.Single(limited.Headers.GetValues("Retry-After")));
        Assert.Equal(HttpStatusCode.OK, (await integration.GetAsync("/api/v1/games")).StatusCode);
    }

    [Fact]
    public async Task RomLinksSupportRangesAndStopWorkingAfterFileVersionChanges()
    {
        await using var fixture = new ApiFixture(); using var api = await fixture.AuthorizedClientAsync(); using var anonymous = fixture.Client();
        long fileId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var root = await database.RootFolders.SingleAsync(); Directory.CreateDirectory(root.Path); await File.WriteAllTextAsync(Path.Combine(root.Path, "game.nes"), "0123456789");
            var game = new Game { Title = "Game", SortTitle = "Game", PlatformId = (await database.Platforms.FirstAsync()).Id, RootFolder = root, QualityProfileId = (await database.QualityProfiles.SingleAsync()).Id };
            var file = new GameFile { Game = game, RelativePath = "game.nes", OriginalFileName = "game.nes", ReleaseName = "Game", Size = 10, FileGroupId = Guid.NewGuid(), ImportedAtUtc = DateTime.UtcNow };
            database.GameFiles.Add(file); await database.SaveChangesAsync(); fileId = file.Id;
        }
        var created = await api.PostAsJsonAsync("/api/v1/media/links", new { kind = "rom", fileId });
        var url = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString();
        using var range = new HttpRequestMessage(HttpMethod.Get, url); range.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);
        using var response = await anonymous.SendAsync(range); Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode); Assert.Equal("0123", await response.Content.ReadAsStringAsync());
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await database.GameFiles.SingleAsync()).Score++;
            await database.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task LoginIsRateLimited()
    {
        await using var fixture = new ApiFixture(); using var browser = fixture.Client(); await CsrfAsync(browser);
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/api/v1/auth/login", new { username = "invalid", password = "invalid password" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await browser.PostAsJsonAsync("/api/v1/auth/login", new { username = "invalid", password = "invalid password" })).StatusCode);
    }

    [Fact]
    public async Task FormsLoginRequiresCsrfAndCookiesCannotBypassInvalidApiKeys()
    {
        await using var fixture = new ApiFixture();
        using var api = await fixture.AuthorizedClientAsync();
        using var configured = await api.PutAsJsonAsync("/api/v1/auth/security", new { authenticationMethod = "Forms", username = "admin", password = "correct horse battery staple",
            trustForwardedHeaders = false, forwardLimit = 1, trustedProxyNetworks = new[] { "127.0.0.0/8" } });
        Assert.Equal(HttpStatusCode.NoContent, configured.StatusCode);
        using var browser = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/games")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "correct horse battery staple" })).StatusCode);
        await CsrfAsync(browser);
        using var login = await browser.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "correct horse battery staple" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), x => x.Contains("httponly", StringComparison.OrdinalIgnoreCase) && x.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/games")).StatusCode);
        browser.DefaultRequestHeaders.Remove("X-CSRF-Token");
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        await CsrfAsync(browser);
        browser.DefaultRequestHeaders.Add("X-Api-Key", new string('0', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/games")).StatusCode);
        browser.DefaultRequestHeaders.Remove("X-Api-Key");
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/games")).StatusCode);
    }

    [Fact]
    public async Task MediaTokensAreScopedExpireAndAreRevokedByKeyRotation()
    {
        await using var fixture = new ApiFixture();
        using var api = await fixture.AuthorizedClientAsync();
        using var anonymous = fixture.Client();
        var media = Path.Combine(fixture.DirectoryPath, "config", "Media", "cover.png");
        await File.WriteAllBytesAsync(media, [0x89, 0x50, 0x4e, 0x47, 13, 10, 26, 10]);
        using var response = await api.PostAsJsonAsync("/api/v1/media/links", new { kind = "cover", coverRelativePath = "cover.png" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        Assert.DoesNotContain(api.DefaultRequestHeaders.GetValues("X-Api-Key").Single(), url);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync(url + "tampered")).StatusCode);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await database.SecuritySettings.SingleAsync();
            var tokens = scope.ServiceProvider.GetRequiredService<MediaTokens>();
            var expired = tokens.Create(new MediaGrant("cover", "cover.png", Guid.Empty, settings.SessionVersion), TimeSpan.FromMilliseconds(1));
            await Task.Delay(30);
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/v1/media/content?token=" + Uri.EscapeDataString(expired))).StatusCode);
            var traversal = tokens.Create(new MediaGrant("cover", "../ludus.db.png", Guid.Empty, settings.SessionVersion), TimeSpan.FromMinutes(1));
            Assert.Equal(HttpStatusCode.Conflict, (await anonymous.GetAsync("/api/v1/media/content?token=" + Uri.EscapeDataString(traversal))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await api.PostAsync("/api/v1/auth/api-key/reset", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/v1/games")).StatusCode);
    }

    [Fact]
    public async Task ConfigurationEncryptsSecretsAndNeverReturnsThem()
    {
        await using var fixture = new ApiFixture();
        using var api = await fixture.AuthorizedClientAsync();
        using var created = await api.PostAsJsonAsync("/api/v1/download-clients", new { name = "test", kind = "QBittorrent", baseUrl = "http://client:8080", username = "login-secret", password = "password-secret",
            enabled = true, priority = 1, postImportTag = "ludus-imported", removeCompleted = false, removeFailed = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var text = await (await api.GetAsync("/api/v1/download-clients")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("login-secret", text); Assert.DoesNotContain("password-secret", text);
        Assert.Contains("passwordConfigured", text);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var client = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DownloadClients.SingleAsync();
        Assert.DoesNotContain("password-secret", client.Password!.Ciphertext);
        using var bad = await api.PostAsJsonAsync("/api/v1/download-clients", new { name = "bad", kind = "QBittorrent", baseUrl = "http://user:pass@host", priority = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task OnlyConfiguredPrivateProxiesMayChangeRequestScheme()
    {
        Assert.Throws<ArgumentException>(() => TrustedProxyMiddleware.Validate("0.0.0.0/0"));
        Assert.Throws<ArgumentException>(() => TrustedProxyMiddleware.Validate("10.0.0.0/7"));
        await using var fixture = new TestDatabase(); await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await database.SecuritySettings.SingleAsync(); settings.TrustForwardedHeaders = true; settings.TrustedProxyNetworks = ["10.0.0.0/8"];
        await database.SaveChangesAsync();
        var middleware = new TrustedProxyMiddleware(_ => Task.CompletedTask, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        foreach (var (address, expected) in new[] { ("10.1.2.3", "https"), ("8.8.8.8", "http") })
        {
            var context = new DefaultHttpContext(); context.Request.Scheme = "http"; context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            context.Request.Headers["X-Forwarded-For"] = "192.168.1.9"; context.Request.Headers["X-Forwarded-Proto"] = "https";
            await middleware.InvokeAsync(context, database); Assert.Equal(expected, context.Request.Scheme);
        }
    }

    private static async Task CsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-Token");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
    }
}
