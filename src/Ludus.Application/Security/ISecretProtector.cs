using Ludus.Domain.Common;

namespace Ludus.Application.Security;

public interface ISecretProtector
{
    ProtectedSecret Protect(string plaintext);
    string Unprotect(ProtectedSecret secret);
}
