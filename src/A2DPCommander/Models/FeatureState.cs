namespace BTAudioDriver.Models;

public enum FeatureState
{
    Disabled,
    Enabling,
    Enabled,
    Disabling,
    Error,
    RollingBack
}

public sealed class FeatureStateInfo
{
    public required FeatureId FeatureId { get; init; }

    public FeatureState State { get; set; } = FeatureState.Disabled;

    public string? ErrorMessage { get; set; }

    public DateTime? LastStateChange { get; set; }

    public bool HasBackup { get; set; }
}
