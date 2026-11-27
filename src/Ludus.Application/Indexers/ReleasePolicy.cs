using Ludus.Application.Profiles;
using Ludus.Domain.Common;
using Ludus.Domain.Configuration;

namespace Ludus.Application.Indexers;

public static class ReleasePolicy
{
    public static IReadOnlyList<RejectionReason> Rejections(IndexerRelease release, AppSettings settings, DateTime nowUtc)
    {
        var reasons = new List<RejectionReason>();
        if (settings.MinimumAgeMinutes > 0 && release.Protocol == DownloadProtocol.Usenet
            && release.PublishedAtUtc is { } published && published > nowUtc.AddMinutes(-settings.MinimumAgeMinutes))
            reasons.Add(RejectionReasons.Create(RejectionCode.TooYoung, $"{settings.MinimumAgeMinutes} min"));
        if (settings.MaximumIndexerSizeBytes > 0 && release.Size > settings.MaximumIndexerSizeBytes)
            reasons.Add(RejectionReasons.Create(RejectionCode.TooLarge, settings.MaximumIndexerSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return reasons;
    }
}
