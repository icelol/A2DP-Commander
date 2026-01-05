namespace BTAudioDriver.Models;

public abstract class BackupData
{
    public required FeatureId FeatureId { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public abstract string BackupType { get; }
}
