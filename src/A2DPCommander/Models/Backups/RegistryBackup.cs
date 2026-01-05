namespace BTAudioDriver.Models.Backups;

public sealed class RegistryBackup : BackupData
{
    public override string BackupType => "Registry";

    public Dictionary<string, RegistryKeyBackup> Keys { get; init; } = new();
}

public sealed class RegistryKeyBackup
{
    public required string KeyPath { get; init; }

    public bool ExistedBefore { get; init; }

    public Dictionary<string, RegistryValueBackup> Values { get; init; } = new();
}

public sealed class RegistryValueBackup
{
    public required string Name { get; init; }

    public object? Value { get; init; }

    public Microsoft.Win32.RegistryValueKind ValueKind { get; init; }
}
