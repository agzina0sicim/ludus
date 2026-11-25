using Ludus.Domain.Common;
using Ludus.Domain.Profiles;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Features;

[ApiController, Route("api/v1")]
public sealed class ConfigurationResourcesController(AppDbContext database) : ControllerBase
{
    [HttpGet("{resource}/{id:long}")]
    public async Task<IActionResult> Get(string resource, long id, CancellationToken cancellationToken)
    {
        Entity? entity = resource switch
        {
            "root-folders" => await database.RootFolders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            "platforms" => await database.Platforms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            "dat-files" => await database.DatFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            "indexers" => await database.Indexers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            "download-clients" => await database.DownloadClients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            "remote-path-mappings" => await database.RemotePathMappings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            "webhooks" => await database.Webhooks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            "profiles" => await database.QualityProfiles.AsNoTracking().Include(x => x.Formats).SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            "custom-formats" => await database.CustomFormats.AsNoTracking().Include(x => x.Rules).SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            _ => null
        };
        return entity switch
        {
            null => NotFound(),
            QualityProfile profile => Ok(new { profile = ConfigurationSupport.View(profile), formats = profile.Formats.Select(x => new { x.CustomFormatId, x.Score }) }),
            CustomFormat format => Ok(new { format.Id, format.Name, format.MatchMode, format.Version, rules = format.Rules.Select(ConfigurationSupport.View) }),
            _ => Ok(ConfigurationSupport.View(entity))
        };
    }
}
