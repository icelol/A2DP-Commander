using BTAudioDriver.Models;
using BTAudioDriver.Models.Backups;
using Serilog;

namespace BTAudioDriver.Services.Features.Handlers;

public sealed class LatencyQueryHandler : IFeatureHandler
{
    private readonly IAudioLatencyService _latencyService;
    private readonly IAudioEndpointService _audioEndpointService;

    public LatencyQueryHandler(IAudioLatencyService latencyService, IAudioEndpointService audioEndpointService)
    {
        _latencyService = latencyService;
        _audioEndpointService = audioEndpointService;
    }

    public FeatureId FeatureId => FeatureId.LatencyQuery;

    public Task<(bool CanActivate, string? Reason)> CanActivateAsync()
    {
        if (!_latencyService.IsSupported)
        {
            return Task.FromResult((false, (string?)"IAudioClient3 requires Windows 10 or later"));
        }

        return Task.FromResult((true, (string?)null));
    }

    public Task<FeatureActivationResult> ActivateAsync()
    {
        Log.Information("LatencyQuery: Activating latency monitoring");

        var backup = new NoOpBackup
        {
            FeatureId = FeatureId.LatencyQuery
        };

        return Task.FromResult(FeatureActivationResult.Ok(backup));
    }

    public Task<FeatureDeactivationResult> DeactivateAsync(BackupData? backup)
    {
        Log.Information("LatencyQuery: Deactivating latency monitoring");
        return Task.FromResult(FeatureDeactivationResult.Ok());
    }

    public Task<FeatureHealthStatus> ValidateAsync()
    {
        if (!_latencyService.IsSupported)
        {
            return Task.FromResult(FeatureHealthStatus.Error("Windows 10+ required"));
        }

        var defaultDevice = _audioEndpointService.GetDefaultPlaybackDevice();
        if (defaultDevice == null)
        {
            return Task.FromResult(FeatureHealthStatus.Warning("No default playback device"));
        }

        var latencyInfo = _latencyService.GetLatencyInfo(defaultDevice.Id);
        if (!latencyInfo.IsSupported)
        {
            return Task.FromResult(FeatureHealthStatus.Warning(latencyInfo.ErrorMessage ?? "Latency query failed"));
        }

        return Task.FromResult(FeatureHealthStatus.Ok($"Current: {latencyInfo.CurrentMs:F1}ms, Range: {latencyInfo.MinMs:F1}-{latencyInfo.MaxMs:F1}ms"));
    }
}
