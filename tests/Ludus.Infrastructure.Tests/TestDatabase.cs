using Ludus.Application.Security;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Ludus.Application.Downloads;

namespace Ludus.Infrastructure.Tests;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Ludus.Tests", Guid.NewGuid().ToString("N"));

    public TestDatabase(Action<IServiceCollection>? configure = null)
    {
        Paths = new LudusPaths(Path.Combine(_directory, "config"), Path.Combine(_directory, "downloads"), Path.Combine(_directory, "roms"));
        Paths.EnsureConfigDirectories();
        var services = new ServiceCollection().AddLogging().AddInfrastructure(Paths);
        services.AddSingleton<IRealtimePublisher, RecordingPublisher>();
        configure?.Invoke(services);
        _services = services.BuildServiceProvider();
    }

    public LudusPaths Paths { get; }
    public ISecretProtector Secrets => _services.GetRequiredService<ISecretProtector>();
    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();

    public async Task InitializeAsync()
    {
        await using var scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}

internal sealed class RecordingPublisher : IRealtimePublisher
{
    public List<(string Event, object Payload)> Messages { get; } = [];
    public Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
    {
        Messages.Add((eventName, payload));
        return Task.CompletedTask;
    }
}
