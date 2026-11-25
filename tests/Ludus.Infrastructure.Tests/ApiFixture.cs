using Ludus.Application.Security;
using Ludus.Infrastructure.Persistence;
using Ludus.Infrastructure.Workers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ludus.Infrastructure.Tests;

internal sealed class ApiFixture : IAsyncDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "Ludus.Api.Tests", Guid.NewGuid().ToString("N"));
    public WebApplicationFactory<Program> Factory { get; }
    public ApiFixture()
    {
        Directory.CreateDirectory(DirectoryPath);
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ludus:ConfigDirectory", Path.Combine(DirectoryPath, "config"));
            builder.UseSetting("Ludus:DownloadDirectory", Path.Combine(DirectoryPath, "downloads"));
            builder.UseSetting("Ludus:RomDirectory", Path.Combine(DirectoryPath, "roms"));
            builder.ConfigureServices(services =>
            {
                foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(AutomationWorker)).ToArray()) services.Remove(descriptor);
            });
        });
    }
    public HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    public async Task<HttpClient> AuthorizedClientAsync()
    {
        var client = Client();
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = scope.ServiceProvider.GetRequiredService<ISecretProtector>().Unprotect((await database.SecuritySettings.SingleAsync()).ApiKey);
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }
    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(DirectoryPath, true);
    }
}
