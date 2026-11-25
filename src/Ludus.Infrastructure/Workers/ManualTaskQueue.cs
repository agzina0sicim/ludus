using System.Collections.Concurrent;
using Ludus.Domain.Common;

namespace Ludus.Infrastructure.Workers;

public sealed class ManualTaskQueue
{
    private readonly ConcurrentDictionary<TaskKind, byte> _pending = new();
    public bool Enqueue(TaskKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid task kind.");
        return _pending.TryAdd(kind, 0);
    }
    public bool Take(TaskKind kind) => _pending.TryRemove(kind, out _);
    public IReadOnlyList<TaskKind> Pending => _pending.Keys.ToArray();
}
