namespace Ludus.Domain.Common;

public abstract class Entity
{
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid Version { get; set; }
}
