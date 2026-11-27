using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ludus.Infrastructure.Persistence;
using Ludus.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ludus.Infrastructure.Tests;

public sealed class ApiWorkflowTests
{
    [Fact]
    public async Task GamesProfilesAndPlatformMonitoringRoundTripThroughTheApi()
    {
        await using var fixture = new ApiFixture(); using var client = await fixture.AuthorizedClientAsync();
        long platformId; long otherPlatformId; long rootId; long profileId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            platformId = (await database.Platforms.SingleAsync(x => x.Slug == "nes")).Id;
            otherPlatformId = (await database.Platforms.FirstAsync(x => x.Id != platformId)).Id;
            rootId = (await database.RootFolders.SingleAsync()).Id; profileId = (await database.QualityProfiles.SingleAsync()).Id;
        }
        using var mismatch = await client.PostAsJsonAsync("/api/v1/games", new { title = "Wrong profile", platformId = otherPlatformId, rootFolderId = rootId, qualityProfileId = profileId, monitored = true });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        using var created = await client.PostAsJsonAsync("/api/v1/games", new { title = "Example", platformId, rootFolderId = rootId, qualityProfileId = profileId, monitored = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(created.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/profiles/{profileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/platforms/{platformId}/monitoring", new { monitored = false })).StatusCode);
        var filtered = await client.GetFromJsonAsync<JsonElement>("/api/v1/games?status=unmonitored"); Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/games?status=cutoffUnmet")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/games?pageSize=1000")).StatusCode);
        using var invalidFormat = await client.PostAsJsonAsync("/api/v1/custom-formats", new { name = "Invalid", matchMode = "All", rules = new[] { new { field = "ReleaseTitle", @operator = "Regex", value = "[", negate = false, required = false } } });
        Assert.Equal(HttpStatusCode.BadRequest, invalidFormat.StatusCode);
        using var format = await client.PostAsJsonAsync("/api/v1/custom-formats", new { name = "Test format", matchMode = "All", rules = new[] { new { field = "Region", @operator = "Equals", value = "Europe", negate = false, required = false } } });
        Assert.Equal(HttpStatusCode.Created, format.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(format.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/v1/games/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/games/{id}")).StatusCode);
    }

    [Fact]
    public async Task ManualTasksDoNotEnableDisabledSchedulesAndHubRejectsForeignOrigins()
    {
        await using var fixture = new ApiFixture(); using var client = await fixture.AuthorizedClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/system/tasks/RssSync", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/v1/system/tasks/RssSync/run", null)).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False((await database.ScheduledTasks.SingleAsync(x => x.Kind == Ludus.Domain.Common.TaskKind.RssSync)).Enabled);
        Assert.Contains(Ludus.Domain.Common.TaskKind.RssSync, scope.ServiceProvider.GetRequiredService<ManualTaskQueue>().Pending);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/activity/negotiate?negotiateVersion=1"); request.Headers.Add("Origin", "https://foreign.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }
}
