using System.ComponentModel.DataAnnotations;
using Ludus.Domain.Common;
using Ludus.Infrastructure.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Ludus.Api.Features;

[ApiController, Route("api/v1/metadata")]
public sealed class MetadataController(MetadataService metadata) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery, Required, MaxLength(200)] string query, [FromQuery] long platformId, [FromQuery] MetadataProviderKind? provider, CancellationToken cancellationToken)
        => Ok(await metadata.SearchAsync(query, platformId, provider, cancellationToken));
    [HttpGet("{provider}/systems")]
    public async Task<IActionResult> Systems(MetadataProviderKind provider, CancellationToken cancellationToken) => Ok(await metadata.SystemsAsync(provider, cancellationToken));
    [HttpPost("{provider}/test")]
    public async Task<IActionResult> Test(MetadataProviderKind provider, CancellationToken cancellationToken) { await metadata.TestAsync(provider, cancellationToken); return Ok(new { success = true }); }
}
