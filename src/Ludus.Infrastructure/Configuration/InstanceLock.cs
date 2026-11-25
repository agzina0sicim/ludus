namespace Ludus.Infrastructure.Configuration;

public sealed class InstanceLock : IDisposable
{
    private readonly FileStream _stream;

    public InstanceLock(LudusPaths paths)
    {
        try
        {
            _stream = new FileStream(Path.Combine(paths.ConfigDirectory, ".ludus.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Cannot lock the config directory. Run only one Ludus instance per config volume.", exception);
        }
    }

    public void Dispose() => _stream.Dispose();
}
