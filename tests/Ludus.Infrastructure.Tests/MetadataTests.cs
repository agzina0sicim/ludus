using System.Net;
using System.Text.Json;
using Ludus.Domain.Common;
using Ludus.Infrastructure.Metadata;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ludus.Infrastructure.Tests;

public sealed class MetadataTests
{
    [Fact]
    public async Task IgdbRenewsRejectedTokenCachesResultsAndStoresCoverLocally()
    {
        var tokenCalls = 0; var gameCalls = 0; var imageCalls = 0;
        using var handler = new DownloadClientTests.Handler(async request =>
        {
            if (request.RequestUri!.Host == "id.twitch.tv")
            {
                tokenCalls++;
                Assert.Equal("", request.RequestUri.Query);
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
                Assert.Equal("client-secret", form["client_secret"]);
                return Json($$"""{"access_token":"token-{{tokenCalls}}","expires_in":3600}""");
            }
            if (request.RequestUri.Host == "images.igdb.com")
            {
                imageCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=")) };
            }
            gameCalls++;
            Assert.Equal("client-id", request.Headers.GetValues("Client-ID").Single());
            Assert.Contains("platforms = (18)", await request.Content!.ReadAsStringAsync());
            if (gameCalls == 1) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            Assert.Equal("token-2", request.Headers.Authorization!.Parameter);
            return Json("""[{"id":42,"name":"Example","summary":"Description","first_release_date":788918400,"platforms":[18],"cover":{"image_id":"co123"},"genres":[{"name":"Action"}],"involved_companies":[{"developer":true,"company":{"name":"Studio"}}]}]""");
        });
        await using var fixture = new TestDatabase(services => services.AddSingleton<IHttpClientFactory>(new DownloadClientTests.Factory(handler)));
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provider = await database.MetadataProviders.SingleAsync(x => x.Provider == MetadataProviderKind.Igdb);
        provider.Enabled = true; provider.ClientId = fixture.Secrets.Protect("client-id"); provider.ClientSecret = fixture.Secrets.Protect("client-secret");
        var platform = await database.Platforms.SingleAsync(x => x.Slug == "nes"); platform.IgdbPlatformId = 18;
        await database.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<MetadataService>();
        var result = Assert.Single(await service.SearchAsync("Example", platform.Id, MetadataProviderKind.Igdb, default));
        Assert.Equal(1995, result.Year); Assert.Equal("Studio", result.Developer);
        Assert.NotNull(result.CoverRelativePath); Assert.Null(result.RemoteCoverUrl);
        Assert.True(File.Exists(Path.Combine(fixture.Paths.MediaDirectory, result.CoverRelativePath)));
        await service.SearchAsync("Example", platform.Id, MetadataProviderKind.Igdb, default);
        Assert.Equal(2, tokenCalls); Assert.Equal(2, gameCalls); Assert.Equal(1, imageCalls);
        var payload = (await database.MetadataCache.SingleAsync()).PayloadJson;
        Assert.DoesNotContain("token-", payload); Assert.DoesNotContain("client-secret", payload); Assert.DoesNotContain("https://", payload);
        Assert.Equal("token-2", fixture.Secrets.Unprotect(provider.AccessToken!));
    }

    [Fact]
    public async Task PrimaryFailureFallsBackToScreenScraperWithSystemAndLanguageMapping()
    {
        using var handler = new DownloadClientTests.Handler(request =>
        {
            if (request.RequestUri!.Host == "id.twitch.tv") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            Assert.Equal("api.screenscraper.fr", request.RequestUri.Host);
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            Assert.Equal("3", query["systemeid"]); Assert.Equal("developer-secret", query["devpassword"]);
            return Task.FromResult(Json("""{"response":{"jeux":[{"id":"7","noms":[{"region":"us","text":"US Title"},{"region":"eu","text":"European Title"}],"systeme":{"id":"3"},"synopsis":[{"langue":"en","text":"English"},{"langue":"de","text":"Deutsch"}],"dates":[{"region":"eu","text":"1992-01-01"}],"developpeur":{"text":"Studio"},"genres":[{"id":"1","noms":[{"langue":"en","text":"Platform"}]}]}]}}"""));
        });
        await using var fixture = new TestDatabase(services => services.AddSingleton<IHttpClientFactory>(new DownloadClientTests.Factory(handler)));
        await fixture.InitializeAsync();
        await using var scope = fixture.CreateScope(); var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var settings in await database.MetadataProviders.ToArrayAsync())
        {
            settings.Enabled = true; settings.ClientId = fixture.Secrets.Protect("developer"); settings.ClientSecret = fixture.Secrets.Protect("developer-secret");
        }
        var platform = await database.Platforms.SingleAsync(x => x.Slug == "nes"); platform.IgdbPlatformId = 18; platform.ScreenScraperSystemId = 3;
        await database.SaveChangesAsync();
        var game = Assert.Single(await scope.ServiceProvider.GetRequiredService<MetadataService>().SearchAsync("Example", platform.Id, null, default));
        Assert.Equal(MetadataProviderKind.ScreenScraper, game.Provider); Assert.Equal("European Title", game.Title); Assert.Equal("Deutsch", game.Description);
        Assert.Equal(1992, game.Year); Assert.Contains("Platform", game.Genres);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
}
