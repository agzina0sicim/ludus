namespace Ludus.Infrastructure.Imports;

public sealed class ImportException(string code, bool permanent) : Exception($"Import failed: {code}.")
{
    public string Code { get; } = code;
    public bool Permanent { get; } = permanent;
}
