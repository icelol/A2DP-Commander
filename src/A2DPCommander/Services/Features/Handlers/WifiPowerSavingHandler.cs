using BTAudioDriver.Models;
using BTAudioDriver.Models.Backups;
using Serilog;

namespace BTAudioDriver.Services.Features.Handlers;

public sealed class WifiPowerSavingHandler : IFeatureHandler
{
    private readonly IWifiAdapterService _wifiService;
    private static readonly ILogger Logger = Log.ForContext<WifiPowerSavingHandler>();

    public WifiPowerSavingHandler(IWifiAdapterService wifiService)
    {
        _wifiService = wifiService;
    }

    public FeatureId FeatureId => FeatureId.WifiPowerSaving;

    public Task<(bool CanActivate, string? Reason)> CanActivateAsync()
    {
        if (!IsRunningAsAdmin())
        {
            return Task.FromResult((false, (string?)"Requires administrator rights"));
        }

        var wifiAdapters = _wifiService.GetAllAdapters();
        var wifiSupported = wifiAdapters.FirstOrDefault(a => a.HasPowerSaving);

        var btAdapters = _wifiService.GetBluetoothAdaptersWithPowerSaving();

        if (wifiSupported == null && btAdapters.Count == 0)
        {
            return Task.FromResult((false, (string?)"No WiFi or Bluetooth adapter with Power Saving control found"));
        }

        return Task.FromResult((true, (string?)null));
    }

    public Task<FeatureActivationResult> ActivateAsync()
    {
        var wifiAdapters = _wifiService.GetAllAdapters();
        var btAdapters = _wifiService.GetBluetoothAdaptersWithPowerSaving();

        var backup = new WifiCoexistenceBackup
        {
            FeatureId = FeatureId.WifiPowerSaving
        };

        var anySuccess = false;

        foreach (var adapter in wifiAdapters.Where(a => a.HasPowerSaving))
        {
            var backupData = _wifiService.BackupSettings(adapter.DeviceInstanceId);
            if (backupData != null)
            {
                backup.Adapters.Add(new WifiAdapterBackupEntry
                {
                    DeviceInstanceId = backupData.DeviceInstanceId,
                    DriverKeyPath = backupData.DriverKeyPath,
                    OriginalPowerSaving = backupData.OriginalPowerSaving
                });
            }

            if (adapter.PowerSavingEnabled == true)
            {
                var success = _wifiService.SetPowerSaving(adapter.DeviceInstanceId, false);
                if (success)
                {
                    anySuccess = true;
                    Logger.Information("PowerSaving: Disabled WiFi Power Saving on {Name}", adapter.Name);
                }
                else
                {
                    Logger.Warning("PowerSaving: Failed to disable WiFi Power Saving on {Name}", adapter.Name);
                }
            }
            else
            {
                Logger.Information("PowerSaving: WiFi Power Saving already disabled on {Name}", adapter.Name);
                anySuccess = true;
            }
        }

        foreach (var btAdapter in btAdapters)
        {
            var btBackup = _wifiService.BackupBluetoothPowerSettings(btAdapter.DeviceInstanceId);
            if (btBackup != null)
            {
                backup.BluetoothAdapters.Add(new BluetoothAdapterBackupEntry
                {
                    DeviceInstanceId = btBackup.DeviceInstanceId,
                    DriverKeyPath = btBackup.DriverKeyPath,
                    OriginalSelectiveSuspend = btBackup.OriginalSelectiveSuspend,
                    OriginalPnpCapabilities = btBackup.OriginalPnpCapabilities
                });
            }

            if (btAdapter.SelectiveSuspendEnabled == true || btAdapter.HasPnpCapabilities)
            {
                var success = _wifiService.SetBluetoothPowerSaving(btAdapter.DeviceInstanceId, false);
                if (success)
                {
                    anySuccess = true;
                    Logger.Information("PowerSaving: Disabled Bluetooth Power Saving on {Name}", btAdapter.Name);
                }
                else
                {
                    Logger.Warning("PowerSaving: Failed to disable Bluetooth Power Saving on {Name}", btAdapter.Name);
                }
            }
            else
            {
                Logger.Information("PowerSaving: Bluetooth Power Saving already disabled on {Name}", btAdapter.Name);
                anySuccess = true;
            }
        }

        if (!anySuccess && backup.Adapters.Count == 0 && backup.BluetoothAdapters.Count == 0)
        {
            return Task.FromResult(FeatureActivationResult.Fail("No adapters to configure"));
        }

        return Task.FromResult(FeatureActivationResult.Ok(backup));
    }

    public Task<FeatureDeactivationResult> DeactivateAsync(BackupData? backup)
    {
        if (backup is not WifiCoexistenceBackup coexBackup)
        {
            Logger.Warning("PowerSaving: No backup data, cannot restore");
            return Task.FromResult(FeatureDeactivationResult.Ok());
        }

        foreach (var entry in coexBackup.Adapters)
        {
            if (entry.OriginalPowerSaving.HasValue)
            {
                var restoreData = new WifiSettingsBackupData
                {
                    DeviceInstanceId = entry.DeviceInstanceId,
                    DriverKeyPath = entry.DriverKeyPath,
                    OriginalBluetoothCollaboration = null,
                    OriginalPowerSaving = entry.OriginalPowerSaving
                };

                var success = _wifiService.RestoreSettings(restoreData);
                if (success)
                {
                    Logger.Information("PowerSaving: Restored WiFi Power Saving for {DeviceId}", entry.DeviceInstanceId);
                }
            }
        }

        foreach (var btEntry in coexBackup.BluetoothAdapters)
        {
            var btRestoreData = new BluetoothPowerBackup
            {
                DeviceInstanceId = btEntry.DeviceInstanceId,
                DriverKeyPath = btEntry.DriverKeyPath,
                OriginalSelectiveSuspend = btEntry.OriginalSelectiveSuspend,
                OriginalPnpCapabilities = btEntry.OriginalPnpCapabilities
            };

            var success = _wifiService.RestoreBluetoothPowerSettings(btRestoreData);
            if (success)
            {
                Logger.Information("PowerSaving: Restored Bluetooth Power Saving for {DeviceId}", btEntry.DeviceInstanceId);
            }
        }

        return Task.FromResult(FeatureDeactivationResult.Ok());
    }

    public Task<FeatureHealthStatus> ValidateAsync()
    {
        var wifiAdapters = _wifiService.GetAllAdapters();
        var btAdapters = _wifiService.GetBluetoothAdaptersWithPowerSaving();

        var wifiWithPowerSave = wifiAdapters.Where(a => a.HasPowerSaving).ToList();
        var btWithPowerSave = btAdapters.Where(a => a.HasSelectiveSuspend || a.HasPnpCapabilities).ToList();

        if (wifiWithPowerSave.Count == 0 && btWithPowerSave.Count == 0)
        {
            return Task.FromResult(FeatureHealthStatus.Warning("No adapters with Power Saving control"));
        }

        var wifiAllDisabled = wifiWithPowerSave.All(a => a.PowerSavingEnabled == false);
        var btAllDisabled = btWithPowerSave.All(a => a.SelectiveSuspendEnabled == false);

        if (wifiAllDisabled && btAllDisabled)
        {
            var total = wifiWithPowerSave.Count + btWithPowerSave.Count;
            return Task.FromResult(FeatureHealthStatus.Ok($"Power Saving disabled on {total} adapter(s)"));
        }

        var enabledNames = new List<string>();
        enabledNames.AddRange(wifiWithPowerSave
            .Where(a => a.PowerSavingEnabled == true)
            .Select(a => $"WiFi: {a.Name}"));
        enabledNames.AddRange(btWithPowerSave
            .Where(a => a.SelectiveSuspendEnabled == true)
            .Select(a => $"BT: {a.Name}"));

        return Task.FromResult(FeatureHealthStatus.Warning($"Power Saving still enabled: {string.Join(", ", enabledNames)}"));
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
