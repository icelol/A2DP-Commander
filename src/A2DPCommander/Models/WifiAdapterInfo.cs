namespace BTAudioDriver.Models;

public sealed class WifiAdapterInfo
{
    public required string Name { get; init; }

    public required string DeviceInstanceId { get; init; }

    public string? Manufacturer { get; set; }

    public bool IsEnabled { get; set; }

    public bool HasBluetoothCollaboration { get; set; }

    public bool? BluetoothCollaborationEnabled { get; set; }

    public bool HasPowerSaving { get; set; }

    public bool? PowerSavingEnabled { get; set; }

    public string? DriverKeyPath { get; set; }
}

public sealed class WifiSettingsBackupData
{
    public required string DeviceInstanceId { get; init; }

    public required string DriverKeyPath { get; init; }

    public bool? OriginalBluetoothCollaboration { get; init; }

    public bool? OriginalPowerSaving { get; init; }
}
