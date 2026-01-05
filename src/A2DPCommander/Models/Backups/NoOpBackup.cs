namespace BTAudioDriver.Models.Backups;

public sealed class NoOpBackup : BackupData
{
    public override string BackupType => "NoOp";
}
