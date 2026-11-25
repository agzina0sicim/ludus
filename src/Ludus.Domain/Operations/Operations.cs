using Ludus.Domain.Common;

namespace Ludus.Domain.Operations;

public sealed class ScheduledTask : Entity
{
    public TaskKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LastStartedAtUtc { get; set; }
    public DateTime? LastCompletedAtUtc { get; set; }
    public DateTime? NextRunAtUtc { get; set; }
}

public sealed class TaskRun : Entity
{
    public long ScheduledTaskId { get; set; }
    public ScheduledTask ScheduledTask { get; set; } = null!;
    public TaskRunStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? ErrorCode { get; set; }
}

public sealed class HealthIssue : Entity
{
    public required string Code { get; set; }
    public required string ResourceKey { get; set; }
    public required string Message { get; set; }
    public HealthSeverity Severity { get; set; }
    public DateTime LastCheckedAtUtc { get; set; }
}

public sealed class BackupRecord : Entity
{
    public required string RelativePath { get; set; }
    public required string ApplicationVersion { get; set; }
    public required string SchemaVersion { get; set; }
    public required string Sha256 { get; set; }
    public long Size { get; set; }
    public bool Scheduled { get; set; }
}
