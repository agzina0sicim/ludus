using Ludus.Application.Security;
using Ludus.Domain.Common;
using Microsoft.AspNetCore.DataProtection;

namespace Ludus.Infrastructure.Security;

public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Ludus.StoredSecrets.v1");

    public ProtectedSecret Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return new ProtectedSecret(_protector.Protect(plaintext));
    }

    public string Unprotect(ProtectedSecret secret) => _protector.Unprotect(secret.Ciphertext);
}
