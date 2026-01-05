using Microsoft.Win32;
using Serilog;

namespace BTAudioDriver.Services;

public sealed class RegistryService : IRegistryService
{
    public bool KeyExists(RegistryHive hive, string subKey)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey);
        return key != null;
    }

    public bool ValueExists(RegistryHive hive, string subKey, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey);
        if (key == null) return false;
        return key.GetValue(valueName) != null;
    }

    public object? GetValue(RegistryHive hive, string subKey, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey);
        return key?.GetValue(valueName);
    }

    public RegistryValueKind GetValueKind(RegistryHive hive, string subKey, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey);
        if (key == null) return RegistryValueKind.Unknown;
        try
        {
            return key.GetValueKind(valueName);
        }
        catch
        {
            return RegistryValueKind.Unknown;
        }
    }

    public string[] GetValueNames(RegistryHive hive, string subKey)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey);
        return key?.GetValueNames() ?? Array.Empty<string>();
    }

    public void SetValue(RegistryHive hive, string subKey, string valueName, object value, RegistryValueKind kind)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.CreateSubKey(subKey, writable: true);
        if (key == null)
        {
            throw new InvalidOperationException($"Cannot create or open registry key: {hive}\\{subKey}");
        }
        key.SetValue(valueName, value, kind);
        Log.Debug("Registry: Set {Hive}\\{SubKey}\\{Name} = {Value}", hive, subKey, valueName, value);
    }

    public void DeleteValue(RegistryHive hive, string subKey, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey, writable: true);
        if (key == null) return;
        try
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            Log.Debug("Registry: Deleted {Hive}\\{SubKey}\\{Name}", hive, subKey, valueName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Registry: Failed to delete {Hive}\\{SubKey}\\{Name}", hive, subKey, valueName);
        }
    }

    public void CreateKey(RegistryHive hive, string subKey)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.CreateSubKey(subKey);
        Log.Debug("Registry: Created key {Hive}\\{SubKey}", hive, subKey);
    }

    public void DeleteKey(RegistryHive hive, string subKey)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        try
        {
            baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            Log.Debug("Registry: Deleted key tree {Hive}\\{SubKey}", hive, subKey);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Registry: Failed to delete key {Hive}\\{SubKey}", hive, subKey);
        }
    }
}
