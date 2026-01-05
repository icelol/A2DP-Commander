using BTAudioDriver.Models;

namespace BTAudioDriver.Services;

public interface IWifiAdapterService
{
    List<WifiAdapterInfo> GetAllAdapters();

    WifiAdapterInfo? GetPrimaryAdapter();

    WifiSettingsBackupData? BackupSettings(string deviceInstanceId);

    bool RestoreSettings(WifiSettingsBackupData backup);

    bool SetBluetoothCollaboration(string deviceInstanceId, bool enabled);

    bool SetPowerSaving(string deviceInstanceId, bool enabled);

    List<BluetoothPowerInfo> GetBluetoothAdaptersWithPowerSaving();

    BluetoothPowerBackup? BackupBluetoothPowerSettings(string deviceInstanceId);

    bool RestoreBluetoothPowerSettings(BluetoothPowerBackup backup);

    bool SetBluetoothPowerSaving(string deviceInstanceId, bool enabled);
}

public sealed class BluetoothPowerInfo
{
    public required string Name { get; init; }

    public required string DeviceInstanceId { get; init; }

    public string? DriverKeyPath { get; set; }

    public bool HasSelectiveSuspend { get; set; }

    public bool? SelectiveSuspendEnabled { get; set; }

    public bool HasPnpCapabilities { get; set; }

    public int? PnpCapabilities { get; set; }
}

public sealed class BluetoothPowerBackup
{
    public required string DeviceInstanceId { get; init; }

    public required string DriverKeyPath { get; init; }

    public bool? OriginalSelectiveSuspend { get; init; }

    public int? OriginalPnpCapabilities { get; init; }
}
