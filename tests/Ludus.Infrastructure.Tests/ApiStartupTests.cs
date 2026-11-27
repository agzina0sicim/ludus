using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Ludus.Application.Security;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ludus.Infrastructure.Tests;

public sealed class ApiStartupTests
{
    [Fact]
    public async Task StartupMigratesAndExposesOnlyMinimalAnonymousHealth()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Ludus.Api.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Ludus:ConfigDirectory", Path.Combine(directory, "config"));
                builder.UseSetting("Ludus:DownloadDirectory", Path.Combine(directory, "downloads"));
                builder.UseSetting("Ludus:RomDirectory", Path.Combine(directory, "roms"));
            });
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/api/v1/system/status");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Single(payload.RootElement.EnumerateObject());
            Assert.Equal("ok", payload.RootElement.GetProperty("status").GetString());
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.True(File.Exists(Path.Combine(directory, "config", "ludus.db")));
            Assert.False(File.Exists(Path.Combine(directory, "config", "account-setup-token")));
            Assert.False(File.Exists(Path.Combine(directory, "config", "reset-account")));
            using var negotiate = await client.PostAsync("/hubs/activity/negotiate?negotiateVersion=1", null);
            Assert.Equal(HttpStatusCode.Unauthorized, negotiate.StatusCode);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var key = scope.ServiceProvider.GetRequiredService<ISecretProtector>().Unprotect((await database.SecuritySettings.SingleAsync()).ApiKey);
                using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/activity/negotiate?negotiateVersion=1");
                request.Headers.Add("X-Api-Key", key);
                using var authenticated = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
                using var negotiation = JsonDocument.Parse(await authenticated.Content.ReadAsStringAsync());
                Assert.Contains(negotiation.RootElement.GetProperty("availableTransports").EnumerateArray(),
                    x => x.GetProperty("transport").GetString() == "WebSockets");
            }
            using var unknownApiResponse = await client.GetAsync("/api/v1/games");
            Assert.Equal(HttpStatusCode.Unauthorized, unknownApiResponse.StatusCode);
            using var privateFileResponse = await client.GetAsync("/config/ludus.db");
            if (privateFileResponse.StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal("text/html", privateFileResponse.Content.Headers.ContentType?.MediaType);
            }
            else
            {
                Assert.Contains(privateFileResponse.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized });
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
