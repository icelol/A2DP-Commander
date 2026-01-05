namespace BTAudioDriver.Models.Backups;

public sealed class DevicePropertyBackup : BackupData
{
    public override string BackupType => "DeviceProperty";

    public required string DeviceInstanceId { get; init; }

    public Dictionary<string, object?> Properties { get; init; } = new();
}
