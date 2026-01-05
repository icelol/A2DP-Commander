namespace BTAudioDriver.Models.Backups;

public sealed class EncoderServiceBackup : BackupData
{
    public override string BackupType => "EncoderService";

    public string? OriginalDefaultDeviceId { get; init; }

    public bool WasServiceRunning { get; init; }

    public string? PreviousCodec { get; init; }
}
