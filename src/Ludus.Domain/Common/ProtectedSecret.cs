using System.Text.Json.Serialization;

namespace Ludus.Domain.Common;

public sealed record ProtectedSecret
{
    [JsonIgnore]
    public string Ciphertext { get; }

    public ProtectedSecret(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        Ciphertext = ciphertext;
    }

    public override string ToString() => "[REDACTED]";
}
