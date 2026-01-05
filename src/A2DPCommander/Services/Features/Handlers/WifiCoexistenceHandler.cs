using BTAudioDriver.Models;
using BTAudioDriver.Models.Backups;
using Serilog;

namespace BTAudioDriver.Services.Features.Handlers;

public sealed class WifiCoexistenceHandler : IFeatureHandler
{
    private readonly IWifiAdapterService _wifiService;
    private static readonly ILogger Logger = Log.ForContext<WifiCoexistenceHandler>();

    public WifiCoexistenceHandler(IWifiAdapterService wifiService)
    {
        _wifiService = wifiService;
    }

    public FeatureId FeatureId => FeatureId.WifiCoexistence;

    public Task<(bool CanActivate, string? Reason)> CanActivateAsync()
    {
        if (!IsRunningAsAdmin())
        {
            return Task.FromResult((false, (string?)"Requires administrator rights"));
        }

        var adapters = _wifiService.GetAllAdapters();
        var supported = adapters.FirstOrDefault(a => a.HasBluetoothCollaboration);

        if (supported == null)
        {
            return Task.FromResult((false, (string?)"No WiFi adapter with Bluetooth Collaboration support found"));
        }

        return Task.FromResult((true, (string?)null));
    }

    public Task<FeatureActivationResult> ActivateAsync()
    {
        var adapters = _wifiService.GetAllAdapters();
        var backup = new WifiCoexistenceBackup
        {
            FeatureId = FeatureId.WifiCoexistence
        };

        var anySuccess = false;

        foreach (var adapter in adapters.Where(a => a.HasBluetoothCollaboration))
        {
            var backupData = _wifiService.BackupSettings(adapter.DeviceInstanceId);
            if (backupData != null)
            {
                backup.Adapters.Add(new WifiAdapterBackupEntry
                {
                    DeviceInstanceId = backupData.DeviceInstanceId,
                    DriverKeyPath = backupData.DriverKeyPath,
                    OriginalBluetoothCollaboration = backupData.OriginalBluetoothCollaboration
                });
            }

            if (adapter.BluetoothCollaborationEnabled == true)
            {
                var success = _wifiService.SetBluetoothCollaboration(adapter.DeviceInstanceId, false);
                if (success)
                {
                    anySuccess = true;
                    Logger.Information("WifiCoexistence: Disabled Bluetooth Collaboration on {Name}", adapter.Name);
                }
                else
                {
                    Logger.Warning("WifiCoexistence: Failed to disable Bluetooth Collaboration on {Name}", adapter.Name);
                }
            }
            else
            {
                Logger.Information("WifiCoexistence: Bluetooth Collaboration already disabled on {Name}", adapter.Name);
                anySuccess = true;
            }
        }

        if (!anySuccess && backup.Adapters.Count == 0)
        {
            return Task.FromResult(FeatureActivationResult.Fail("No WiFi adapters to configure"));
        }

        return Task.FromResult(FeatureActivationResult.Ok(backup));
    }

    public Task<FeatureDeactivationResult> DeactivateAsync(BackupData? backup)
    {
        if (backup is not WifiCoexistenceBackup coexBackup)
        {
            Logger.Warning("WifiCoexistence: No backup data, cannot restore");
            return Task.FromResult(FeatureDeactivationResult.Ok());
        }

        foreach (var entry in coexBackup.Adapters)
        {
            if (entry.OriginalBluetoothCollaboration.HasValue)
            {
                var restoreData = new WifiSettingsBackupData
                {
                    DeviceInstanceId = entry.DeviceInstanceId,
                    DriverKeyPath = entry.DriverKeyPath,
                    OriginalBluetoothCollaboration = entry.OriginalBluetoothCollaboration,
                    OriginalPowerSaving = null
                };

                var success = _wifiService.RestoreSettings(restoreData);
                if (success)
                {
                    Logger.Information("WifiCoexistence: Restored Bluetooth Collaboration for {DeviceId}", entry.DeviceInstanceId);
                }
            }
        }

        return Task.FromResult(FeatureDeactivationResult.Ok());
    }

    public Task<FeatureHealthStatus> ValidateAsync()
    {
        var adapters = _wifiService.GetAllAdapters();
        var withBtCollab = adapters.Where(a => a.HasBluetoothCollaboration).ToList();

        if (withBtCollab.Count == 0)
        {
            return Task.FromResult(FeatureHealthStatus.Warning("No WiFi adapter with BT Collaboration"));
        }

        var allDisabled = withBtCollab.All(a => a.BluetoothCollaborationEnabled == false);

        if (allDisabled)
        {
            return Task.FromResult(FeatureHealthStatus.Ok($"BT Collaboration disabled on {withBtCollab.Count} adapter(s)"));
        }

        var enabledNames = string.Join(", ", withBtCollab
            .Where(a => a.BluetoothCollaborationEnabled == true)
            .Select(a => a.Name));

        return Task.FromResult(FeatureHealthStatus.Warning($"BT Collaboration still enabled: {enabledNames}"));
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
