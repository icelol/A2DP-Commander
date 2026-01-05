using Microsoft.Win32;

namespace BTAudioDriver.Services;

public interface IRegistryService
{
    bool KeyExists(RegistryHive hive, string subKey);

    bool ValueExists(RegistryHive hive, string subKey, string valueName);

    object? GetValue(RegistryHive hive, string subKey, string valueName);

    RegistryValueKind GetValueKind(RegistryHive hive, string subKey, string valueName);

    string[] GetValueNames(RegistryHive hive, string subKey);

    void SetValue(RegistryHive hive, string subKey, string valueName, object value, RegistryValueKind kind);

    void DeleteValue(RegistryHive hive, string subKey, string valueName);

    void CreateKey(RegistryHive hive, string subKey);

    void DeleteKey(RegistryHive hive, string subKey);
}
