using BTAudioDriver.Localization;
using BTAudioDriver.Models;
using BTAudioDriver.Models.Backups;
using Microsoft.Win32;
using Serilog;

namespace BTAudioDriver.Services.Features.Handlers;

public sealed class LdacRegistryHandler : IFeatureHandler
{
    private readonly IRegistryService _registry;
    private readonly IFeatureManager? _featureManager;
    private readonly IBluetoothAdapterService? _adapterService;

    private const string BthA2dpKey = @"SYSTEM\CurrentControlSet\Services\BthA2dp\Parameters";
    private const string LdacDriverKey = @"SOFTWARE\A2DPCommander\LdacExperimental";
    private const string BthAudioKey = @"SYSTEM\CurrentControlSet\Services\BthEnum\Parameters\Audio";

    private static readonly (string Name, object Value, RegistryValueKind Kind)[] LdacSettings =
    [
        ("PreferredCodec", 4, RegistryValueKind.DWord),
        ("DisableSBC", 0, RegistryValueKind.DWord),
        ("PreferHighQuality", 1, RegistryValueKind.DWord),
        ("A2DPBitpool", 53, RegistryValueKind.DWord),
    ];

    private static readonly (string Name, object Value, RegistryValueKind Kind)[] ExperimentalSettings =
    [
        ("LdacEnabled", 1, RegistryValueKind.DWord),
        ("PreferredBitrate", 990000, RegistryValueKind.DWord),
        ("ForceLDAC", 1, RegistryValueKind.DWord),
        ("ExperimentalMode", 1, RegistryValueKind.DWord),
    ];

    public LdacRegistryHandler(IRegistryService registry, IFeatureManager? featureManager = null, IBluetoothAdapterService? adapterService = null)
    {
        _registry = registry;
        _featureManager = featureManager;
        _adapterService = adapterService;
    }

    public FeatureId FeatureId => FeatureId.LdacRegistry;

    public Task<(bool CanActivate, string? Reason)> CanActivateAsync()
    {
        var adapterSupport = CheckAdapterSupport();
        if (!adapterSupport.Supported)
        {
            return Task.FromResult((false, adapterSupport.Reason));
        }

        if (!IsRunningAsAdmin())
        {
            return Task.FromResult((false, (string?)"Requires administrator rights. Restart as admin to use this feature."));
        }

        if (_featureManager?.IsEnabled(FeatureId.ExternalEncoder) == true)
        {
            return Task.FromResult((false, (string?)"Disable External Encoder first. These features are mutually exclusive."));
        }

        return Task.FromResult((true, (string?)null));
    }

    private (bool Supported, string? Reason) CheckAdapterSupport()
    {
        if (_adapterService == null)
        {
            return (false, Strings.Get("Feature.NoBluetoothAdapter"));
        }

        var activeAdapter = _adapterService.GetActiveAdapter();
        if (activeAdapter == null)
        {
            return (false, Strings.Get("Feature.NoBluetoothAdapter"));
        }

        if (!activeAdapter.SupportsLDAC)
        {
            var manufacturer = !string.IsNullOrEmpty(activeAdapter.Manufacturer)
                ? $" ({activeAdapter.Manufacturer})"
                : "";
            return (false, string.Format(Strings.Get("Feature.AdapterNoLdac"), manufacturer));
        }

        return (true, null);
    }

    public Task<FeatureActivationResult> ActivateAsync()
    {
        Log.Information("LdacRegistry: Activating experimental codec forcing");

        var backup = new RegistryBackup
        {
            FeatureId = FeatureId.LdacRegistry
        };

        try
        {
            BackupAndApplySettings(backup, RegistryHive.LocalMachine, BthA2dpKey, LdacSettings);
            BackupAndApplySettings(backup, RegistryHive.LocalMachine, LdacDriverKey, ExperimentalSettings);

            if (_registry.KeyExists(RegistryHive.LocalMachine, BthAudioKey))
            {
                BackupExistingValues(backup, RegistryHive.LocalMachine, BthAudioKey);
            }

            Log.Information("LdacRegistry: Applied {Count} registry settings",
                LdacSettings.Length + ExperimentalSettings.Length);

            return Task.FromResult(FeatureActivationResult.Ok(backup));
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "LdacRegistry: Access denied - run as administrator");
            return Task.FromResult(FeatureActivationResult.Fail("Access denied. Run as administrator."));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LdacRegistry: Failed to apply settings");
            return Task.FromResult(FeatureActivationResult.Fail($"Failed: {ex.Message}"));
        }
    }

    public Task<FeatureDeactivationResult> DeactivateAsync(BackupData? backup)
    {
        Log.Information("LdacRegistry: Deactivating and restoring original settings");

        if (backup is not RegistryBackup registryBackup)
        {
            _registry.DeleteKey(RegistryHive.LocalMachine, LdacDriverKey);
            Log.Warning("LdacRegistry: No backup data, only removed experimental key");
            return Task.FromResult(FeatureDeactivationResult.Ok());
        }

        try
        {
            foreach (var (keyPath, keyBackup) in registryBackup.Keys)
            {
                RestoreKey(keyPath, keyBackup);
            }

            _registry.DeleteKey(RegistryHive.LocalMachine, LdacDriverKey);

            Log.Information("LdacRegistry: Restored {Count} registry keys", registryBackup.Keys.Count);
            return Task.FromResult(FeatureDeactivationResult.Ok());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LdacRegistry: Failed to restore settings");
            return Task.FromResult(FeatureDeactivationResult.Fail($"Restore failed: {ex.Message}"));
        }
    }

    public Task<FeatureHealthStatus> ValidateAsync()
    {
        var ldacEnabled = _registry.GetValue(RegistryHive.LocalMachine, LdacDriverKey, "LdacEnabled");
        if (ldacEnabled is int enabled && enabled == 1)
        {
            var preferredBitrate = _registry.GetValue(RegistryHive.LocalMachine, LdacDriverKey, "PreferredBitrate");
            var bitrateKbps = (preferredBitrate as int? ?? 990000) / 1000;
            return Task.FromResult(FeatureHealthStatus.Warning(
                $"EXPERIMENTAL: LDAC registry active @ {bitrateKbps} kbps. Reboot required. May not work with your BT adapter."));
        }

        return Task.FromResult(FeatureHealthStatus.Ok("Registry settings not applied"));
    }

    private void BackupAndApplySettings(
        RegistryBackup backup,
        RegistryHive hive,
        string subKey,
        (string Name, object Value, RegistryValueKind Kind)[] settings)
    {
        var fullPath = $"{hive}\\{subKey}";
        var keyBackup = new RegistryKeyBackup
        {
            KeyPath = fullPath,
            ExistedBefore = _registry.KeyExists(hive, subKey)
        };

        if (keyBackup.ExistedBefore)
        {
            foreach (var valueName in _registry.GetValueNames(hive, subKey))
            {
                var value = _registry.GetValue(hive, subKey, valueName);
                var kind = _registry.GetValueKind(hive, subKey, valueName);
                keyBackup.Values[valueName] = new RegistryValueBackup
                {
                    Name = valueName,
                    Value = value,
                    ValueKind = kind
                };
            }
        }

        backup.Keys[fullPath] = keyBackup;

        foreach (var (name, value, kind) in settings)
        {
            _registry.SetValue(hive, subKey, name, value, kind);
        }
    }

    private void BackupExistingValues(RegistryBackup backup, RegistryHive hive, string subKey)
    {
        var fullPath = $"{hive}\\{subKey}";
        var keyBackup = new RegistryKeyBackup
        {
            KeyPath = fullPath,
            ExistedBefore = true
        };

        foreach (var valueName in _registry.GetValueNames(hive, subKey))
        {
            var value = _registry.GetValue(hive, subKey, valueName);
            var kind = _registry.GetValueKind(hive, subKey, valueName);
            keyBackup.Values[valueName] = new RegistryValueBackup
            {
                Name = valueName,
                Value = value,
                ValueKind = kind
            };
        }

        backup.Keys[fullPath] = keyBackup;
    }

    private void RestoreKey(string fullPath, RegistryKeyBackup keyBackup)
    {
        var parts = fullPath.Split('\\', 2);
        if (parts.Length != 2) return;

        var hive = parts[0] switch
        {
            "LocalMachine" => RegistryHive.LocalMachine,
            "CurrentUser" => RegistryHive.CurrentUser,
            _ => RegistryHive.LocalMachine
        };
        var subKey = parts[1];

        if (!keyBackup.ExistedBefore)
        {
            _registry.DeleteKey(hive, subKey);
            return;
        }

        var currentValues = _registry.GetValueNames(hive, subKey);
        foreach (var currentValue in currentValues)
        {
            if (!keyBackup.Values.ContainsKey(currentValue))
            {
                _registry.DeleteValue(hive, subKey, currentValue);
            }
        }

        foreach (var (name, valueBackup) in keyBackup.Values)
        {
            if (valueBackup.Value != null)
            {
                _registry.SetValue(hive, subKey, name, valueBackup.Value, valueBackup.ValueKind);
            }
        }
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
