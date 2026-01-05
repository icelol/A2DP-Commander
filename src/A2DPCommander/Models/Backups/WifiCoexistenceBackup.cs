namespace BTAudioDriver.Models.Backups;

public sealed class WifiCoexistenceBackup : BackupData
{
    public override string BackupType => "WifiCoexistence";

    public List<WifiAdapterBackupEntry> Adapters { get; init; } = new();

    public List<BluetoothAdapterBackupEntry> BluetoothAdapters { get; init; } = new();
}

public sealed class WifiAdapterBackupEntry
{
    public required string DeviceInstanceId { get; init; }

    public required string DriverKeyPath { get; init; }

    public bool? OriginalBluetoothCollaboration { get; init; }

    public bool? OriginalPowerSaving { get; init; }
}

public sealed class BluetoothAdapterBackupEntry
{
    public required string DeviceInstanceId { get; init; }

    public required string DriverKeyPath { get; init; }

    public bool? OriginalSelectiveSuspend { get; init; }

    public int? OriginalPnpCapabilities { get; init; }
}
