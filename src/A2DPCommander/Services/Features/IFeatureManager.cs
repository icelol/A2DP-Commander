using BTAudioDriver.Models;

namespace BTAudioDriver.Services.Features;

public interface IFeatureManager
{
    bool IsEnabled(FeatureId featureId);

    FeatureState GetState(FeatureId featureId);

    FeatureStateInfo GetStateInfo(FeatureId featureId);

    IReadOnlyList<FeatureStateInfo> GetAllStates();

    (bool CanEnable, string? Reason) CanEnable(FeatureId featureId);

    Task<(bool CanActivate, string? Reason)> CanActivateAsync(FeatureId featureId);

    IReadOnlyList<FeatureId> GetConflicts(FeatureId featureId);

    Task<FeatureOperationResult> EnableAsync(FeatureId featureId);

    Task<FeatureOperationResult> DisableAsync(FeatureId featureId);

    Task<FeatureOperationResult> RollbackAsync(FeatureId featureId);

    Task<FeatureOperationResult> RollbackAllAsync();

    Task<FeatureHealthStatus> ValidateAsync(FeatureId featureId);

    Task LoadStateAsync();

    Task SaveStateAsync();

    event EventHandler<FeatureStateChangedEventArgs>? StateChanged;
}

public sealed class FeatureOperationResult
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public FeatureState NewState { get; init; }

    public static FeatureOperationResult Ok(FeatureState newState) => new()
    {
        Success = true,
        NewState = newState
    };

    public static FeatureOperationResult Fail(string error, FeatureState state = FeatureState.Error) => new()
    {
        Success = false,
        ErrorMessage = error,
        NewState = state
    };
}

public sealed class FeatureStateChangedEventArgs : EventArgs
{
    public required FeatureId FeatureId { get; init; }

    public required FeatureState OldState { get; init; }

    public required FeatureState NewState { get; init; }
}
