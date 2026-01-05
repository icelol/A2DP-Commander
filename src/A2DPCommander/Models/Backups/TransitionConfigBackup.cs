namespace BTAudioDriver.Models.Backups;

public sealed class TransitionConfigBackup : BackupData
{
    public override string BackupType => "TransitionConfig";

    public bool UsePolling { get; init; }

    public bool UseEventWait { get; init; }

    public bool UseFadeInOut { get; init; }

    public int TimeoutMs { get; init; }

    public int DelayMs { get; init; }
}
