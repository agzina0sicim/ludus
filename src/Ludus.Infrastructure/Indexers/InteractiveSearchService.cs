using Ludus.Application.Indexers;
using Ludus.Application.Profiles;
using Ludus.Application.Releases;
using Ludus.Domain.Common;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ludus.Infrastructure.Indexers;

public sealed class InteractiveSearchService(IDbContextFactory<AppDbContext> databaseFactory, IIndexerClient client,
    ReleaseParser parser, ReleaseScoringEngine scoring, ILogger<InteractiveSearchService> logger) : IInteractiveSearch
{
    public async Task<InteractiveSearchResult> SearchAsync(long gameId, CancellationToken cancellationToken = default, bool automatic = false)
    {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var game = await database.Games.AsNoTracking().Include(x => x.Platform).Include(x => x.Files)
            .Include(x => x.QualityProfile).ThenInclude(x => x.Formats).ThenInclude(x => x.CustomFormat).ThenInclude(x => x.Rules)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == gameId, cancellationToken)
            ?? throw new KeyNotFoundException("Game does not exist.");
        var blocks = await database.ReleaseBlocks.AsNoTracking().Where(x => x.GameId == gameId).ToListAsync(cancellationToken);
        var settings = await database.AppSettings.AsNoTracking().SingleAsync(cancellationToken);
        var indexers = await database.Indexers.Where(x => automatic ? x.EnableAutomaticSearch : x.EnableInteractiveSearch).OrderBy(x => x.Priority).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var releases = new List<EvaluatedRelease>();
        var failures = new List<IndexerFailure>();
        var truncated = false;
        foreach (var indexer in indexers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var caps = await client.GetCapabilitiesAsync(indexer, cancellationToken);
                indexer.CapabilitiesXml = IndexerXmlParser.SerializeCapabilities(caps);
                indexer.CapabilitiesUpdatedAtUtc = DateTime.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
                if (!caps.SearchAvailable || !caps.SearchParameters.Contains("q", StringComparer.OrdinalIgnoreCase))
                    throw new IndexerException("SearchUnsupported");
                var categories = SelectCategories(game.Platform.TorznabCategories, indexer.Categories, caps.Categories);
                if (categories.Length == 0 && (game.Platform.TorznabCategories.Count != 0 || indexer.Categories.Count != 0))
                    throw new IndexerException("CategoriesUnsupported");
                var limit = Math.Min(caps.MaximumLimit, 100);
                var offset = 0;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var pageNumber = 0; pageNumber < 20; pageNumber++)
                {
                    var page = await client.SearchAsync(indexer, new IndexerQuery(parser.Parse(game.Title).Title, categories, offset, limit), cancellationToken);
                    var added = 0;
                    foreach (var release in page.Releases.Where(x => seen.Add(x.ReleaseId)))
                    {
                        added++;
                        var parsed = parser.Parse(string.IsNullOrWhiteSpace(release.Title) || release.Title.Length > 4096 ? "Unknown" : release.Title);
                        var decision = scoring.Evaluate(parsed, new ReleaseEvaluationContext(game.QualityProfile, game.Platform)
                        {
                            Game = game, ExistingFiles = game.Files.ToArray(), Blocks = blocks, ReleaseFingerprint = release.Fingerprint, Automatic = automatic
                        });
                        var reasons = decision.Rejections.ToList();
                        reasons.AddRange(ReleasePolicy.Rejections(release, settings, DateTime.UtcNow));
                        if (release.ParseWarnings.Count != 0) reasons.Add(RejectionReasons.Create(RejectionCode.AmbiguousRelease));
                        if (release.DownloadUrl is null) reasons.Add(RejectionReasons.Create(RejectionCode.DownloadUnavailable));
                        var specificCategories = release.Categories.Where(category => category % 1000 != 0).ToArray();
                        var releaseCategories = specificCategories.Length != 0 ? specificCategories : release.Categories;
                        if (categories.Length != 0 && releaseCategories.Count != 0
                            && !categories.Any(category => releaseCategories.Any(actual => Covers(category, actual) || Covers(actual, category))))
                            reasons.Add(RejectionReasons.Create(RejectionCode.CategoryMismatch));
                        if (release.Protocol == DownloadProtocol.Torrent && indexer.MinimumSeeders > 0
                            && (release.Seeders is null || release.Seeders < indexer.MinimumSeeders))
                            reasons.Add(RejectionReasons.Create(RejectionCode.InsufficientSeeders));
                        releases.Add(new EvaluatedRelease(release, parsed, decision with { Rejections = reasons.DistinctBy(x => x.Code).ToArray() }));
                    }
                    offset += page.Releases.Count;
                    if (added == 0 && page.Releases.Count > 0)
                    {
                        truncated = true;
                        throw new IndexerException("PaginationStalled");
                    }
                    if (page.Releases.Count == 0 && page.Total > offset)
                    {
                        truncated = true;
                        throw new IndexerException("PaginationIncomplete");
                    }
                    if (page.Releases.Count == 0 || (page.Total is not null && offset >= page.Total)
                        || (page.Total is null && page.Releases.Count < limit)) break;
                    if (pageNumber == 19) truncated = true;
                }
            }
            catch (IndexerException exception)
            {
                failures.Add(new IndexerFailure(indexer.Id, exception.Code, exception.RetryAfterSeconds));
                logger.LogWarning("Indexer {IndexerId} search failed with {ErrorCode}.", indexer.Id, exception.Code);
            }
        }
        return new InteractiveSearchResult(releases.OrderByDescending(x => x.Decision.Accepted)
            .ThenByDescending(x => x.Decision.Score).ThenBy(x => x.Release.Title, StringComparer.Ordinal).ToArray(), failures, truncated);
    }

    private static int[] SelectCategories(IReadOnlyList<int> platform, IReadOnlyList<int> configured, IReadOnlyList<IndexerCategory> available)
    {
        var requested = platform.Count == 0 ? configured : platform;
        var candidates = requested.SelectMany(category => configured.Count == 0 ? [category]
            : configured.Where(other => Covers(category, other) || Covers(other, category)).Select(other => Covers(category, other) ? other : category));
        return candidates.Where(category => available.Count == 0 || available.Any(x => Covers(x.Id, category) || Covers(category, x.Id)))
            .Distinct().Order().ToArray();
    }

    private static bool Covers(int parent, int child) => parent == child || (parent % 1000 == 0 && child / 1000 == parent / 1000);
}
