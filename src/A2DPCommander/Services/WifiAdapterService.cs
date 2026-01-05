using System.Runtime.InteropServices;
using BTAudioDriver.Models;
using BTAudioDriver.Native;
using Microsoft.Win32;
using Serilog;

namespace BTAudioDriver.Services;

public class WifiAdapterService : IWifiAdapterService
{
    private static readonly ILogger Logger = Log.ForContext<WifiAdapterService>();

    private static readonly Guid GUID_DEVCLASS_NET = new("4d36e972-e325-11ce-bfc1-08002be10318");
    private static readonly Guid GUID_DEVCLASS_BLUETOOTH = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");

    private static readonly string[] BluetoothPowerSavingNames = new[]
    {
        "SelectiveSuspendEnabled", "SelectiveSuspend", "SSIdleTime", "DeviceSelectiveSuspended"
    };

    private static readonly Dictionary<string, string[]> BluetoothCollaborationPropertyNames = new()
    {
        { "Intel", new[] { "BtCoexistenceMode", "BTCoexistence" } },
        { "Realtek", new[] { "BTHSMode", "BluetoothHSMode" } },
        { "Broadcom", new[] { "BTCoexistence", "BluetoothCoexistence" } },
        { "Qualcomm", new[] { "btCoexMode", "BluetoothCoexistence" } },
        { "MediaTek", new[] { "BTCoexistence" } }
    };

    private static readonly string[] CommonBtCollaborationNames = new[]
    {
        "BtCoexistenceMode", "BTCoexistence", "BTHSMode", "BluetoothHSMode",
        "BluetoothCoexistence", "btCoexMode", "BT_Coexistence"
    };

    private static readonly string[] PowerSavingNames = new[]
    {
        "PnPCapabilities", "PowerSaveMode", "WowlanPowerSave", "WoWLAN"
    };

    public List<WifiAdapterInfo> GetAllAdapters()
    {
        var adapters = new List<WifiAdapterInfo>();

        try
        {
            var guid = GUID_DEVCLASS_NET;
            using var deviceInfoSet = SetupApi.SetupDiGetClassDevs(
                ref guid,
                null,
                IntPtr.Zero,
                SetupApi.DIGCF_PRESENT);

            if (deviceInfoSet.IsInvalid)
            {
                Logger.Warning("Failed to get network device info set");
                return adapters;
            }

            var deviceInfoData = new SetupApi.SP_DEVINFO_DATA
            {
                cbSize = Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>()
            };

            for (var i = 0; SetupApi.SetupDiEnumDeviceInfo(deviceInfoSet, i, ref deviceInfoData); i++)
            {
                var friendlyName = SetupApi.GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SetupApi.SPDRP_FRIENDLYNAME);
                var description = SetupApi.GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SetupApi.SPDRP_DEVICEDESC);
                var instanceId = SetupApi.GetDeviceInstanceId(deviceInfoSet, ref deviceInfoData);

                var name = friendlyName ?? description ?? "";

                if (!IsWifiAdapter(name, instanceId))
                    continue;

                var adapter = new WifiAdapterInfo
                {
                    Name = name,
                    DeviceInstanceId = instanceId ?? ""
                };

                DetectManufacturer(adapter);
                LoadDriverSettings(adapter);

                adapters.Add(adapter);
                Logger.Debug("Found WiFi adapter: {Name}, BtCollab={HasBtCollab}, PowerSave={HasPowerSave}",
                    adapter.Name, adapter.HasBluetoothCollaboration, adapter.HasPowerSaving);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to enumerate WiFi adapters");
        }

        return adapters;
    }

    public WifiAdapterInfo? GetPrimaryAdapter()
    {
        return GetAllAdapters().FirstOrDefault(a => a.IsEnabled);
    }

    public WifiSettingsBackupData? BackupSettings(string deviceInstanceId)
    {
        try
        {
            var adapter = GetAllAdapters().FirstOrDefault(a =>
                a.DeviceInstanceId.Equals(deviceInstanceId, StringComparison.OrdinalIgnoreCase));

            if (adapter == null || string.IsNullOrEmpty(adapter.DriverKeyPath))
            {
                Logger.Warning("Cannot backup settings: adapter not found or no driver key");
                return null;
            }

            return new WifiSettingsBackupData
            {
                DeviceInstanceId = deviceInstanceId,
                DriverKeyPath = adapter.DriverKeyPath,
                OriginalBluetoothCollaboration = adapter.BluetoothCollaborationEnabled,
                OriginalPowerSaving = adapter.PowerSavingEnabled
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to backup WiFi settings for {DeviceId}", deviceInstanceId);
            return null;
        }
    }

    public bool RestoreSettings(WifiSettingsBackupData backup)
    {
        try
        {
            var success = true;

            if (backup.OriginalBluetoothCollaboration.HasValue)
            {
                success &= SetBluetoothCollaborationByKey(backup.DriverKeyPath, backup.OriginalBluetoothCollaboration.Value);
            }

            if (backup.OriginalPowerSaving.HasValue)
            {
                success &= SetPowerSavingByKey(backup.DriverKeyPath, backup.OriginalPowerSaving.Value);
            }

            Logger.Information("Restored WiFi settings for {DeviceId}: {Result}",
                backup.DeviceInstanceId, success ? "Success" : "Partial/Failed");

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to restore WiFi settings for {DeviceId}", backup.DeviceInstanceId);
            return false;
        }
    }

    public bool SetBluetoothCollaboration(string deviceInstanceId, bool enabled)
    {
        try
        {
            var adapter = GetAllAdapters().FirstOrDefault(a =>
                a.DeviceInstanceId.Equals(deviceInstanceId, StringComparison.OrdinalIgnoreCase));

            if (adapter == null)
            {
                Logger.Warning("Adapter not found: {DeviceId}", deviceInstanceId);
                return false;
            }

            if (!adapter.HasBluetoothCollaboration || string.IsNullOrEmpty(adapter.DriverKeyPath))
            {
                Logger.Warning("Adapter {Name} does not support Bluetooth Collaboration control", adapter.Name);
                return false;
            }

            return SetBluetoothCollaborationByKey(adapter.DriverKeyPath, enabled);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to set Bluetooth Collaboration for {DeviceId}", deviceInstanceId);
            return false;
        }
    }

    public bool SetPowerSaving(string deviceInstanceId, bool enabled)
    {
        try
        {
            var adapter = GetAllAdapters().FirstOrDefault(a =>
                a.DeviceInstanceId.Equals(deviceInstanceId, StringComparison.OrdinalIgnoreCase));

            if (adapter == null)
            {
                Logger.Warning("Adapter not found: {DeviceId}", deviceInstanceId);
                return false;
            }

            if (!adapter.HasPowerSaving || string.IsNullOrEmpty(adapter.DriverKeyPath))
            {
                Logger.Warning("Adapter {Name} does not support Power Saving control", adapter.Name);
                return false;
            }

            return SetPowerSavingByKey(adapter.DriverKeyPath, enabled);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to set Power Saving for {DeviceId}", deviceInstanceId);
            return false;
        }
    }

    private static bool IsWifiAdapter(string name, string? instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
            return false;

        var lowerName = name.ToLowerInvariant();
        var lowerInstanceId = instanceId.ToLowerInvariant();

        if (lowerName.Contains("virtual") ||
            lowerName.Contains("vpn") ||
            lowerName.Contains("hyper-v") ||
            lowerName.Contains("miniport") ||
            lowerName.Contains("bluetooth") ||
            lowerName.Contains("wan") ||
            lowerName.Contains("tunnel") ||
            lowerName.Contains("loopback"))
            return false;

        if (lowerName.Contains("wi-fi") ||
            lowerName.Contains("wifi") ||
            lowerName.Contains("wireless") ||
            lowerName.Contains("wlan") ||
            lowerName.Contains("802.11"))
            return true;

        if (lowerInstanceId.Contains("pci\\") &&
            (lowerName.Contains("network") || lowerName.Contains("adapter")))
        {
            if (lowerInstanceId.Contains("vid_8086") ||
                lowerInstanceId.Contains("vid_10ec") ||
                lowerInstanceId.Contains("vid_14c3") ||
                lowerInstanceId.Contains("vid_168c") ||
                lowerInstanceId.Contains("vid_14e4"))
                return true;
        }

        return false;
    }

    private static void DetectManufacturer(WifiAdapterInfo adapter)
    {
        var name = adapter.Name.ToLowerInvariant();
        var instanceId = adapter.DeviceInstanceId.ToLowerInvariant();

        if (name.Contains("intel") || instanceId.Contains("vid_8086"))
            adapter.Manufacturer = "Intel";
        else if (name.Contains("realtek") || instanceId.Contains("vid_10ec"))
            adapter.Manufacturer = "Realtek";
        else if (name.Contains("mediatek") || instanceId.Contains("vid_14c3"))
            adapter.Manufacturer = "MediaTek";
        else if (name.Contains("qualcomm") || name.Contains("atheros") || instanceId.Contains("vid_168c"))
            adapter.Manufacturer = "Qualcomm";
        else if (name.Contains("broadcom") || instanceId.Contains("vid_14e4"))
            adapter.Manufacturer = "Broadcom";
    }

    private void LoadDriverSettings(WifiAdapterInfo adapter)
    {
        try
        {
            var driverKeyPath = GetDriverKeyPath(adapter.DeviceInstanceId);
            if (string.IsNullOrEmpty(driverKeyPath))
            {
                Logger.Debug("No driver key found for {Name}", adapter.Name);
                return;
            }

            adapter.DriverKeyPath = driverKeyPath;

            using var key = Registry.LocalMachine.OpenSubKey(driverKeyPath);
            if (key == null)
            {
                Logger.Debug("Cannot open driver key for {Name}", adapter.Name);
                return;
            }

            adapter.IsEnabled = true;

            var valueNames = key.GetValueNames();

            foreach (var btPropName in GetBluetoothCollaborationNames(adapter.Manufacturer))
            {
                if (valueNames.Contains(btPropName, StringComparer.OrdinalIgnoreCase))
                {
                    adapter.HasBluetoothCollaboration = true;
                    var value = key.GetValue(btPropName);
                    adapter.BluetoothCollaborationEnabled = InterpretBtCollaborationValue(value);
                    Logger.Debug("{Name}: Found BT Collaboration property {Prop}={Value}",
                        adapter.Name, btPropName, adapter.BluetoothCollaborationEnabled);
                    break;
                }
            }

            foreach (var powerPropName in PowerSavingNames)
            {
                if (valueNames.Contains(powerPropName, StringComparer.OrdinalIgnoreCase))
                {
                    adapter.HasPowerSaving = true;
                    var value = key.GetValue(powerPropName);
                    adapter.PowerSavingEnabled = InterpretPowerSavingValue(powerPropName, value);
                    Logger.Debug("{Name}: Found Power Saving property {Prop}={Value}",
                        adapter.Name, powerPropName, adapter.PowerSavingEnabled);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to load driver settings for {Name}", adapter.Name);
        }
    }

    private static string[] GetBluetoothCollaborationNames(string? manufacturer)
    {
        if (manufacturer != null && BluetoothCollaborationPropertyNames.TryGetValue(manufacturer, out var names))
        {
            return names.Concat(CommonBtCollaborationNames).Distinct().ToArray();
        }
        return CommonBtCollaborationNames;
    }

    private static bool? InterpretBtCollaborationValue(object? value)
    {
        return value switch
        {
            int intVal => intVal != 0,
            string strVal => strVal != "0" && !strVal.Equals("disabled", StringComparison.OrdinalIgnoreCase),
            _ => null
        };
    }

    private static bool? InterpretPowerSavingValue(string propertyName, object? value)
    {
        if (propertyName.Equals("PnPCapabilities", StringComparison.OrdinalIgnoreCase))
        {
            if (value is int intVal)
            {
                return (intVal & 0x18) == 0;
            }
        }

        return value switch
        {
            int intVal => intVal != 0,
            string strVal => strVal != "0" && !strVal.Equals("disabled", StringComparison.OrdinalIgnoreCase),
            _ => null
        };
    }

    private static string? GetDriverKeyPath(string deviceInstanceId)
    {
        try
        {
            var enumPath = $@"SYSTEM\CurrentControlSet\Enum\{deviceInstanceId}";
            using var enumKey = Registry.LocalMachine.OpenSubKey(enumPath);
            if (enumKey == null) return null;

            var driver = enumKey.GetValue("Driver") as string;
            if (string.IsNullOrEmpty(driver)) return null;

            return $@"SYSTEM\CurrentControlSet\Control\Class\{driver}";
        }
        catch
        {
            return null;
        }
    }

    private bool SetBluetoothCollaborationByKey(string driverKeyPath, bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(driverKeyPath, writable: true);
            if (key == null)
            {
                Logger.Warning("Cannot open driver key for writing: {Path}", driverKeyPath);
                return false;
            }

            var valueNames = key.GetValueNames();
            string? targetProp = null;

            foreach (var btPropName in CommonBtCollaborationNames)
            {
                if (valueNames.Contains(btPropName, StringComparer.OrdinalIgnoreCase))
                {
                    targetProp = btPropName;
                    break;
                }
            }

            if (targetProp == null)
            {
                Logger.Warning("No Bluetooth Collaboration property found in {Path}", driverKeyPath);
                return false;
            }

            var currentValue = key.GetValue(targetProp);
            int newValue = enabled ? 1 : 0;

            if (currentValue is string)
            {
                key.SetValue(targetProp, newValue.ToString(), RegistryValueKind.String);
            }
            else
            {
                key.SetValue(targetProp, newValue, RegistryValueKind.DWord);
            }

            Logger.Information("Set Bluetooth Collaboration ({Prop}) to {Enabled}", targetProp, enabled);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            Logger.Warning("Access denied: Cannot modify Bluetooth Collaboration. Run as administrator.");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to set Bluetooth Collaboration");
            return false;
        }
    }

    private bool SetPowerSavingByKey(string driverKeyPath, bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(driverKeyPath, writable: true);
            if (key == null)
            {
                Logger.Warning("Cannot open driver key for writing: {Path}", driverKeyPath);
                return false;
            }

            var valueNames = key.GetValueNames();
            string? targetProp = null;

            foreach (var powerPropName in PowerSavingNames)
            {
                if (valueNames.Contains(powerPropName, StringComparer.OrdinalIgnoreCase))
                {
                    targetProp = powerPropName;
                    break;
                }
            }

            if (targetProp == null)
            {
                Logger.Warning("No Power Saving property found in {Path}", driverKeyPath);
                return false;
            }

            if (targetProp.Equals("PnPCapabilities", StringComparison.OrdinalIgnoreCase))
            {
                var current = key.GetValue(targetProp) as int? ?? 0;
                int newValue = enabled
                    ? current & ~0x18
                    : current | 0x18;
                key.SetValue(targetProp, newValue, RegistryValueKind.DWord);
            }
            else
            {
                int newValue = enabled ? 1 : 0;
                key.SetValue(targetProp, newValue, RegistryValueKind.DWord);
            }

            Logger.Information("Set Power Saving ({Prop}) to {Enabled}", targetProp, enabled);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            Logger.Warning("Access denied: Cannot modify Power Saving. Run as administrator.");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to set Power Saving");
            return false;
        }
    }

    public List<BluetoothPowerInfo> GetBluetoothAdaptersWithPowerSaving()
    {
        var adapters = new List<BluetoothPowerInfo>();

        try
        {
            var guid = GUID_DEVCLASS_BLUETOOTH;
            using var deviceInfoSet = SetupApi.SetupDiGetClassDevs(
                ref guid,
                null,
                IntPtr.Zero,
                SetupApi.DIGCF_PRESENT);

            if (deviceInfoSet.IsInvalid)
            {
                Logger.Warning("Failed to get Bluetooth device info set");
                return adapters;
            }

            var deviceInfoData = new SetupApi.SP_DEVINFO_DATA
            {
                cbSize = Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>()
            };

            for (var i = 0; SetupApi.SetupDiEnumDeviceInfo(deviceInfoSet, i, ref deviceInfoData); i++)
            {
                var friendlyName = SetupApi.GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SetupApi.SPDRP_FRIENDLYNAME);
                var description = SetupApi.GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SetupApi.SPDRP_DEVICEDESC);
                var instanceId = SetupApi.GetDeviceInstanceId(deviceInfoSet, ref deviceInfoData);

                var name = friendlyName ?? description ?? "";

                if (string.IsNullOrEmpty(instanceId))
                    continue;

                var lowerName = name.ToLowerInvariant();
                if (!lowerName.Contains("bluetooth") && !lowerName.Contains("radio"))
                    continue;

                var adapter = new BluetoothPowerInfo
                {
                    Name = name,
                    DeviceInstanceId = instanceId
                };

                LoadBluetoothPowerSettings(adapter);

                if (adapter.HasSelectiveSuspend || adapter.HasPnpCapabilities)
                {
                    adapters.Add(adapter);
                    Logger.Debug("Found Bluetooth adapter with power control: {Name}, SS={HasSS}, PnP={HasPnP}",
                        adapter.Name, adapter.HasSelectiveSuspend, adapter.HasPnpCapabilities);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to enumerate Bluetooth adapters for power saving");
        }

        return adapters;
    }

    private void LoadBluetoothPowerSettings(BluetoothPowerInfo adapter)
    {
        try
        {
            var driverKeyPath = GetDriverKeyPath(adapter.DeviceInstanceId);
            if (string.IsNullOrEmpty(driverKeyPath))
            {
                Logger.Debug("No driver key found for BT adapter {Name}", adapter.Name);
                return;
            }

            adapter.DriverKeyPath = driverKeyPath;

            using var key = Registry.LocalMachine.OpenSubKey(driverKeyPath);
            if (key == null)
            {
                Logger.Debug("Cannot open driver key for BT adapter {Name}", adapter.Name);
                return;
            }

            var valueNames = key.GetValueNames();

            foreach (var propName in BluetoothPowerSavingNames)
            {
                if (valueNames.Contains(propName, StringComparer.OrdinalIgnoreCase))
                {
                    adapter.HasSelectiveSuspend = true;
                    var value = key.GetValue(propName);
                    adapter.SelectiveSuspendEnabled = value switch
                    {
                        int intVal => intVal != 0,
                        string strVal => strVal != "0",
                        _ => null
                    };
                    Logger.Debug("BT {Name}: Found {Prop}={Value}",
                        adapter.Name, propName, adapter.SelectiveSuspendEnabled);
                    break;
                }
            }

            var enumPath = $@"SYSTEM\CurrentControlSet\Enum\{adapter.DeviceInstanceId}";
            using var enumKey = Registry.LocalMachine.OpenSubKey(enumPath);
            if (enumKey != null)
            {
                var pnpCaps = enumKey.GetValue("PnPCapabilities");
                if (pnpCaps is int pnpValue)
                {
                    adapter.HasPnpCapabilities = true;
                    adapter.PnpCapabilities = pnpValue;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to load BT power settings for {Name}", adapter.Name);
        }
    }

    public BluetoothPowerBackup? BackupBluetoothPowerSettings(string deviceInstanceId)
    {
        try
        {
            var adapter = GetBluetoothAdaptersWithPowerSaving().FirstOrDefault(a =>
                a.DeviceInstanceId.Equals(deviceInstanceId, StringComparison.OrdinalIgnoreCase));

            if (adapter == null || string.IsNullOrEmpty(adapter.DriverKeyPath))
            {
                Logger.Warning("Cannot backup BT power settings: adapter not found or no driver key");
                return null;
            }

            return new BluetoothPowerBackup
            {
                DeviceInstanceId = deviceInstanceId,
                DriverKeyPath = adapter.DriverKeyPath,
                OriginalSelectiveSuspend = adapter.SelectiveSuspendEnabled,
                OriginalPnpCapabilities = adapter.PnpCapabilities
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to backup BT power settings for {DeviceId}", deviceInstanceId);
            return null;
        }
    }

    public bool RestoreBluetoothPowerSettings(BluetoothPowerBackup backup)
    {
        try
        {
            var success = true;

            if (backup.OriginalSelectiveSuspend.HasValue)
            {
                success &= SetBluetoothSelectiveSuspendByKey(backup.DriverKeyPath, backup.OriginalSelectiveSuspend.Value);
            }

            if (backup.OriginalPnpCapabilities.HasValue)
            {
                var enumPath = $@"SYSTEM\CurrentControlSet\Enum\{backup.DeviceInstanceId}";
                try
                {
                    using var enumKey = Registry.LocalMachine.OpenSubKey(enumPath, writable: true);
                    if (enumKey != null)
                    {
                        enumKey.SetValue("PnPCapabilities", backup.OriginalPnpCapabilities.Value, RegistryValueKind.DWord);
                        Logger.Information("Restored BT PnPCapabilities for {DeviceId}", backup.DeviceInstanceId);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to restore BT PnPCapabilities");
                    success = false;
                }
            }

            Logger.Information("Restored BT power settings for {DeviceId}: {Result}",
                backup.DeviceInstanceId, success ? "Success" : "Partial/Failed");

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to restore BT power settings for {DeviceId}", backup.DeviceInstanceId);
            return false;
        }
    }

    public bool SetBluetoothPowerSaving(string deviceInstanceId, bool enabled)
    {
        try
        {
            var adapter = GetBluetoothAdaptersWithPowerSaving().FirstOrDefault(a =>
                a.DeviceInstanceId.Equals(deviceInstanceId, StringComparison.OrdinalIgnoreCase));

            if (adapter == null)
            {
                Logger.Warning("BT Adapter not found: {DeviceId}", deviceInstanceId);
                return false;
            }

            var anySuccess = false;

            if (adapter.HasSelectiveSuspend && !string.IsNullOrEmpty(adapter.DriverKeyPath))
            {
                anySuccess |= SetBluetoothSelectiveSuspendByKey(adapter.DriverKeyPath, enabled);
            }

            if (adapter.HasPnpCapabilities)
            {
                var enumPath = $@"SYSTEM\CurrentControlSet\Enum\{deviceInstanceId}";
                try
                {
                    using var enumKey = Registry.LocalMachine.OpenSubKey(enumPath, writable: true);
                    if (enumKey != null)
                    {
                        var current = adapter.PnpCapabilities ?? 0;
                        int newValue = enabled
                            ? current & ~0x18
                            : current | 0x18;
                        enumKey.SetValue("PnPCapabilities", newValue, RegistryValueKind.DWord);
                        Logger.Information("Set BT PnPCapabilities to 0x{Value:X}", newValue);
                        anySuccess = true;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    Logger.Warning("Access denied: Cannot modify BT PnPCapabilities. Run as administrator.");
                }
            }

            return anySuccess;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to set BT Power Saving for {DeviceId}", deviceInstanceId);
            return false;
        }
    }

    private bool SetBluetoothSelectiveSuspendByKey(string driverKeyPath, bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(driverKeyPath, writable: true);
            if (key == null)
            {
                Logger.Warning("Cannot open BT driver key for writing: {Path}", driverKeyPath);
                return false;
            }

            var valueNames = key.GetValueNames();
            string? targetProp = null;

            foreach (var propName in BluetoothPowerSavingNames)
            {
                if (valueNames.Contains(propName, StringComparer.OrdinalIgnoreCase))
                {
                    targetProp = propName;
                    break;
                }
            }

            if (targetProp == null)
            {
                Logger.Warning("No Selective Suspend property found in {Path}", driverKeyPath);
                return false;
            }

            int newValue = enabled ? 1 : 0;
            key.SetValue(targetProp, newValue, RegistryValueKind.DWord);
            Logger.Information("Set BT Selective Suspend ({Prop}) to {Enabled}", targetProp, enabled);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            Logger.Warning("Access denied: Cannot modify BT Selective Suspend. Run as administrator.");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to set BT Selective Suspend");
            return false;
        }
    }
}
