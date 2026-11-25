using Ludus.Application.Downloads;
using Microsoft.AspNetCore.SignalR;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Hubs;

public sealed class ActivityHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var version = Context.User?.FindFirst("sessionVersion")?.Value;
        if (version is null) { Context.Abort(); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, version, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}

public sealed class SignalRPublisher(IHubContext<ActivityHub> hub, IServiceScopeFactory scopes) : IRealtimePublisher
{
    public async Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var version = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SecuritySettings.AsNoTracking().Select(x => x.SessionVersion).SingleAsync(cancellationToken);
        await hub.Clients.Group(version.ToString()).SendAsync(eventName, payload, cancellationToken);
    }
}
