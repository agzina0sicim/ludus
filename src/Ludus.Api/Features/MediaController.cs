using System.ComponentModel.DataAnnotations;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Persistence;
using Ludus.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Features;

public sealed record MediaLinkRequest([Required] string Kind, long? FileId, [MaxLength(256)] string? CoverRelativePath);

[ApiController, Route("api/v1/media")]
public sealed class MediaController(AppDbContext database, LudusPaths paths, MediaTokens tokens) : ControllerBase
{
    [HttpPost("links")]
    public async Task<IActionResult> Create(MediaLinkRequest request, CancellationToken cancellationToken)
    {
        var security = await database.SecuritySettings.AsNoTracking().SingleAsync(cancellationToken);
        var lifetime = (await database.AppSettings.AsNoTracking().SingleAsync(cancellationToken)).MediaLinkLifetimeSeconds;
        MediaGrant grant;
        if (request.Kind == "rom")
        {
            var file = await database.GameFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.FileId && !x.Missing, cancellationToken) ?? throw new KeyNotFoundException();
            grant = new MediaGrant("rom", file.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), file.Version, security.SessionVersion);
        }
        else if (request.Kind == "cover" && request.CoverRelativePath is { } cover && System.IO.File.Exists(SafePaths.Within(paths.MediaDirectory, cover)))
            grant = new MediaGrant("cover", cover, Guid.Empty, security.SessionVersion);
        else return BadRequest(new { code = "InvalidMediaResource" });
        Response.Headers.CacheControl = "no-store";
        return Ok(new { url = "/api/v1/media/content?token=" + Uri.EscapeDataString(tokens.Create(grant, TimeSpan.FromSeconds(lifetime))), expiresAtUtc = DateTime.UtcNow.AddSeconds(lifetime) });
    }

    [HttpGet("content"), AllowAnonymous]
    public async Task<IActionResult> Content([FromQuery, Required] string token, CancellationToken cancellationToken)
    {
        var grant = tokens.Read(token);
        if (grant.SessionVersion != (await database.SecuritySettings.AsNoTracking().SingleAsync(cancellationToken)).SessionVersion) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (grant.Kind == "cover")
        {
            var mime = Path.GetExtension(grant.Resource) switch { ".png" => "image/png", ".jpg" => "image/jpeg", ".webp" => "image/webp", _ => null };
            if (mime is null) return NotFound();
            return File(System.IO.File.OpenRead(SafePaths.Within(paths.MediaDirectory, grant.Resource)), mime);
        }
        if (grant.Kind != "rom" || !long.TryParse(grant.Resource, out var id)) return NotFound();
        var file = await database.GameFiles.AsNoTracking().Include(x => x.Game).ThenInclude(x => x.RootFolder)
            .SingleOrDefaultAsync(x => x.Id == id && !x.Missing, cancellationToken);
        if (file is null || file.Version != grant.Version) return NotFound();
        return File(System.IO.File.OpenRead(SafePaths.Within(file.Game.RootFolder.Path, file.RelativePath)), "application/octet-stream", Path.GetFileName(file.RelativePath), true);
    }
}
