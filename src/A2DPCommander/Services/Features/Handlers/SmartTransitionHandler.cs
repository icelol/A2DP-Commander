using BTAudioDriver.Models;
using BTAudioDriver.Models.Backups;
using Serilog;

namespace BTAudioDriver.Services.Features.Handlers;

public sealed class SmartTransitionHandler : IFeatureHandler
{
    private readonly IProfileManager _profileManager;

    public SmartTransitionHandler(IProfileManager profileManager)
    {
        _profileManager = profileManager;
    }

    public FeatureId FeatureId => FeatureId.SmartTransition;

    public Task<(bool CanActivate, string? Reason)> CanActivateAsync()
    {
        return Task.FromResult((true, (string?)null));
    }

    public Task<FeatureActivationResult> ActivateAsync()
    {
        Log.Information("SmartTransition: Activating smart device transition");

        var currentConfig = _profileManager.TransitionConfig;
        var backup = new TransitionConfigBackup
        {
            FeatureId = FeatureId.SmartTransition,
            UsePolling = currentConfig.UsePolling,
            UseEventWait = currentConfig.UseEventWait,
            UseFadeInOut = currentConfig.UseFadeInOut,
            TimeoutMs = currentConfig.TimeoutMs,
            DelayMs = currentConfig.FadeTimeMs
        };

        _profileManager.SetTransitionConfig(TransitionConfig.Smart);

        Log.Information("SmartTransition: Enabled (Polling=true, EventWait=true, Fade=true, Timeout=3000ms)");
        return Task.FromResult(FeatureActivationResult.Ok(backup));
    }

    public Task<FeatureDeactivationResult> DeactivateAsync(BackupData? backup)
    {
        Log.Information("SmartTransition: Deactivating");

        if (backup is TransitionConfigBackup configBackup)
        {
            var restoredConfig = new TransitionConfig
            {
                UsePolling = configBackup.UsePolling,
                UseEventWait = configBackup.UseEventWait,
                UseFadeInOut = configBackup.UseFadeInOut,
                TimeoutMs = configBackup.TimeoutMs,
                FadeTimeMs = configBackup.DelayMs
            };
            _profileManager.SetTransitionConfig(restoredConfig);
            Log.Information("SmartTransition: Restored original config from backup");
        }
        else
        {
            _profileManager.SetTransitionConfig(TransitionConfig.Default);
            Log.Information("SmartTransition: Restored default config (no backup)");
        }

        return Task.FromResult(FeatureDeactivationResult.Ok());
    }

    public Task<FeatureHealthStatus> ValidateAsync()
    {
        var config = _profileManager.TransitionConfig;
        if (config.UsePolling || config.UseEventWait)
        {
            return Task.FromResult(FeatureHealthStatus.Ok("Smart transition active"));
        }

        return Task.FromResult(FeatureHealthStatus.Warning("Smart transition using fallback mode"));
    }
}
