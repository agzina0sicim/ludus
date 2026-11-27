using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Ludus.Domain.Common;
using Ludus.Domain.Profiles;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Features;

public sealed record ProfileFormatRequest([Range(1, long.MaxValue)] long CustomFormatId, int Score);
public sealed record ProfileRequest([Required, MaxLength(128)] string Name, [Range(1, long.MaxValue)] long PlatformId,
    int MinimumScore, bool AllowUpgrades, int UpgradeUntilScore,
    [Required, MaxLength(64)] List<string> AllowedRegions, [Required, MaxLength(64)] List<string> ExcludedRegions,
    bool RejectBeta, bool RejectDemo, bool RejectPrototype, bool AllowUpdates, bool AllowDlc, [Required, MaxLength(1000)] List<ProfileFormatRequest> Formats);
public sealed record FormatRuleRequest(FormatField Field, FormatOperator Operator, [Required, MaxLength(2000)] string Value, bool Negate, bool Required);
public sealed record FormatRequest([Required, MaxLength(128)] string Name, FormatMatchMode MatchMode, [Required, MinLength(1), MaxLength(100)] List<FormatRuleRequest> Rules);

[ApiController, Route("api/v1")]
public sealed class ProfilesController(AppDbContext database) : ControllerBase
{
    [HttpGet("profiles")]
    public async Task<IActionResult> Profiles([FromQuery] long? platformId, CancellationToken cancellationToken)
    {
        var profiles = await database.QualityProfiles.AsNoTracking().Where(x => platformId == null || x.PlatformId == platformId).Include(x => x.Formats).ToArrayAsync(cancellationToken);
        return Ok(profiles.Select(x => new { profile = ConfigurationSupport.View(x), formats = x.Formats.Select(f => new { f.CustomFormatId, f.Score }) }));
    }
    [HttpPost("profiles")]
    public async Task<IActionResult> AddProfile(ProfileRequest request, CancellationToken cancellationToken)
    {
        if (!await database.Platforms.AnyAsync(x => x.Id == request.PlatformId, cancellationToken)) return BadRequest(new { code = "InvalidPlatform" });
        var profile = new QualityProfile { Name = request.Name }; Apply(request, profile); database.QualityProfiles.Add(profile);
        await database.SaveChangesAsync(cancellationToken); return Created($"/api/v1/profiles/{profile.Id}", new { profile.Id });
    }
    [HttpPut("profiles/{id:long}")]
    public async Task<IActionResult> UpdateProfile(long id, ProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await database.QualityProfiles.Include(x => x.Formats).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        if (!await database.Platforms.AnyAsync(x => x.Id == request.PlatformId, cancellationToken)) return BadRequest(new { code = "InvalidPlatform" });
        if (profile.PlatformId != request.PlatformId && await database.Games.AnyAsync(x => x.QualityProfileId == id, cancellationToken))
            return Conflict(new { code = "ProfileAssignedToGames" });
        Apply(request, profile); await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpDelete("profiles/{id:long}")]
    public async Task<IActionResult> DeleteProfile(long id, CancellationToken cancellationToken)
    {
        var profile = await database.QualityProfiles.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException(); database.QualityProfiles.Remove(profile);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpGet("custom-formats")]
    public async Task<IActionResult> Formats(CancellationToken cancellationToken)
    {
        var formats = await database.CustomFormats.AsNoTracking().Include(x => x.Rules).ToArrayAsync(cancellationToken);
        return Ok(formats.Select(x => new { x.Id, x.Name, x.MatchMode, x.Version, rules = x.Rules.Select(ConfigurationSupport.View) }));
    }
    [HttpPost("custom-formats")]
    public async Task<IActionResult> AddFormat(FormatRequest request, CancellationToken cancellationToken)
    {
        Validate(request); var item = new CustomFormat { Name = request.Name, MatchMode = request.MatchMode, Rules = Rules(request) };
        database.CustomFormats.Add(item); await database.SaveChangesAsync(cancellationToken); return Created($"/api/v1/custom-formats/{item.Id}", new { item.Id });
    }
    [HttpPut("custom-formats/{id:long}")]
    public async Task<IActionResult> UpdateFormat(long id, FormatRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var item = await database.CustomFormats.Include(x => x.Rules).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        item.Name = request.Name; item.MatchMode = request.MatchMode;
        database.CustomFormatRules.RemoveRange(item.Rules); item.Rules = Rules(request);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpDelete("custom-formats/{id:long}")]
    public async Task<IActionResult> DeleteFormat(long id, CancellationToken cancellationToken)
    {
        var item = await database.CustomFormats.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException(); database.CustomFormats.Remove(item);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    private static void Apply(ProfileRequest request, QualityProfile profile)
    {
        if (request.UpgradeUntilScore < request.MinimumScore || request.Formats.Select(x => x.CustomFormatId).Distinct().Count() != request.Formats.Count
            || request.AllowedRegions.Concat(request.ExcludedRegions).Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 64)) throw new ArgumentException("Invalid profile scores or rules.");
        ConfigurationSupport.Apply(request, profile);
        foreach (var old in profile.Formats.Where(x => request.Formats.All(f => f.CustomFormatId != x.CustomFormatId)).ToArray()) profile.Formats.Remove(old);
        foreach (var input in request.Formats)
        {
            var item = profile.Formats.SingleOrDefault(x => x.CustomFormatId == input.CustomFormatId);
            if (item is null) profile.Formats.Add(new QualityProfileFormat { CustomFormatId = input.CustomFormatId, Score = input.Score });
            else item.Score = input.Score;
        }
    }
    private static ICollection<CustomFormatRule> Rules(FormatRequest request) => request.Rules.Select(x => new CustomFormatRule
        { Field = x.Field, Operator = x.Operator, Value = x.Value, Negate = x.Negate, Required = x.Required }).ToList();
    private static void Validate(FormatRequest request)
    {
        foreach (var rule in request.Rules.Where(x => x.Operator == FormatOperator.Regex))
            _ = new Regex(rule.Value, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    }
}
