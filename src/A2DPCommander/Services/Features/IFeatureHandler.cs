using BTAudioDriver.Models;

namespace BTAudioDriver.Services.Features;

public interface IFeatureHandler
{
    FeatureId FeatureId { get; }

    Task<(bool CanActivate, string? Reason)> CanActivateAsync();

    Task<FeatureActivationResult> ActivateAsync();

    Task<FeatureDeactivationResult> DeactivateAsync(BackupData? backup);

    Task<FeatureHealthStatus> ValidateAsync();
}

public sealed class FeatureActivationResult
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public BackupData? Backup { get; init; }

    public static FeatureActivationResult Ok(BackupData backup) => new()
    {
        Success = true,
        Backup = backup
    };

    public static FeatureActivationResult Fail(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}

public sealed class FeatureDeactivationResult
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public static FeatureDeactivationResult Ok() => new() { Success = true };

    public static FeatureDeactivationResult Fail(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}

public sealed class FeatureHealthStatus
{
    public HealthLevel Level { get; init; }

    public string Message { get; init; } = string.Empty;

    public static FeatureHealthStatus Ok(string message = "OK") => new()
    {
        Level = HealthLevel.Ok,
        Message = message
    };

    public static FeatureHealthStatus Warning(string message) => new()
    {
        Level = HealthLevel.Warning,
        Message = message
    };

    public static FeatureHealthStatus Error(string message) => new()
    {
        Level = HealthLevel.Error,
        Message = message
    };

    public static FeatureHealthStatus Unavailable(string message) => new()
    {
        Level = HealthLevel.Unavailable,
        Message = message
    };
}

public enum HealthLevel
{
    Ok,
    Warning,
    Error,
    Unavailable
}
