using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Ludus.Infrastructure.Security;

public sealed record MediaGrant(string Kind, string Resource, Guid Version, Guid SessionVersion);

public sealed class MediaTokens(IDataProtectionProvider provider)
{
    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("Ludus.Media.v1").ToTimeLimitedDataProtector();
    public string Create(MediaGrant grant, TimeSpan lifetime) => _protector.Protect(JsonSerializer.Serialize(grant), lifetime);
    public MediaGrant Read(string token)
    {
        if (token.Length > 4096) throw new CryptographicException("Invalid media token.");
        try { return JsonSerializer.Deserialize<MediaGrant>(_protector.Unprotect(token)) ?? throw new CryptographicException("Invalid media token."); }
        catch (JsonException) { throw new CryptographicException("Invalid media token."); }
    }
}
