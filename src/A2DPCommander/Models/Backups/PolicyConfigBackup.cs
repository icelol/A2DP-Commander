namespace BTAudioDriver.Models.Backups;

public sealed class PolicyConfigBackup : BackupData
{
    public override string BackupType => "PolicyConfig";

    public required string DeviceId { get; init; }

    public long DefaultPeriod { get; init; }

    public long MinPeriod { get; init; }
}
