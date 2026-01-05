using BTAudioDriver.Models;
using BTAudioDriver.Models.Backups;
using Serilog;

namespace BTAudioDriver.Services.Features.Handlers;

public sealed class ProcessingPeriodHandler : IFeatureHandler
{
    private readonly IAudioEndpointService _audioService;
    private const long TargetPeriod = 30000L;

    public ProcessingPeriodHandler(IAudioEndpointService audioService)
    {
        _audioService = audioService;
    }

    public FeatureId FeatureId => FeatureId.ProcessingPeriodControl;

    public Task<(bool CanActivate, string? Reason)> CanActivateAsync()
    {
        try
        {
            var a2dpEndpoint = GetA2dpEndpoint();
            if (a2dpEndpoint == null)
            {
                return Task.FromResult((false, (string?)"No Bluetooth A2DP device connected"));
            }

            var bufferInfo = _audioService.GetBufferInfo(a2dpEndpoint.Id);
            if (!bufferInfo.IsSupported)
            {
                return Task.FromResult((false, (string?)$"Device does not support buffer control: {bufferInfo.ErrorMessage}"));
            }

            return Task.FromResult((true, (string?)null));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ProcessingPeriodControl: Exception in CanActivateAsync");
            return Task.FromResult((false, (string?)$"Error: {ex.Message}"));
        }
    }

    public Task<FeatureActivationResult> ActivateAsync()
    {
        try
        {
            var a2dpEndpoint = GetA2dpEndpoint();
            if (a2dpEndpoint == null)
            {
                return Task.FromResult(FeatureActivationResult.Fail("No A2DP device"));
            }

            var bufferInfo = _audioService.GetBufferInfo(a2dpEndpoint.Id);
            if (!bufferInfo.IsSupported)
            {
                return Task.FromResult(FeatureActivationResult.Fail(bufferInfo.ErrorMessage ?? "Buffer info unavailable"));
            }

            var backup = new PolicyConfigBackup
            {
                FeatureId = FeatureId.ProcessingPeriodControl,
                DeviceId = a2dpEndpoint.Id,
                DefaultPeriod = bufferInfo.DefaultPeriod,
                MinPeriod = bufferInfo.MinPeriod
            };

            var targetPeriod = Math.Max(TargetPeriod, bufferInfo.MinPeriod);
            var success = _audioService.SetBufferSize(a2dpEndpoint.Id, targetPeriod);

            if (!success)
            {
                Log.Warning("ProcessingPeriodControl: Failed to set buffer size to {PeriodMs}ms", targetPeriod / 10000.0);
                return Task.FromResult(FeatureActivationResult.Fail("Failed to set buffer size"));
            }

            Log.Information("ProcessingPeriodControl: Set buffer to {PeriodMs}ms (was {DefaultMs}ms)",
                targetPeriod / 10000.0, bufferInfo.DefaultMs);

            return Task.FromResult(FeatureActivationResult.Ok(backup));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ProcessingPeriodControl: Exception during activation");
            return Task.FromResult(FeatureActivationResult.Fail($"Error: {ex.Message}"));
        }
    }

    public Task<FeatureDeactivationResult> DeactivateAsync(BackupData? backup)
    {
        if (backup is PolicyConfigBackup configBackup && !string.IsNullOrEmpty(configBackup.DeviceId))
        {
            var success = _audioService.SetBufferSize(configBackup.DeviceId, configBackup.DefaultPeriod);
            if (success)
            {
                Log.Information("ProcessingPeriodControl: Restored buffer to {DefaultMs}ms",
                    configBackup.DefaultPeriod / 10000.0);
            }
            else
            {
                Log.Warning("ProcessingPeriodControl: Failed to restore buffer");
            }
        }

        return Task.FromResult(FeatureDeactivationResult.Ok());
    }

    public Task<FeatureHealthStatus> ValidateAsync()
    {
        try
        {
            var a2dpEndpoint = GetA2dpEndpoint();
            if (a2dpEndpoint == null)
            {
                return Task.FromResult(FeatureHealthStatus.Warning("No A2DP device"));
            }

            var bufferInfo = _audioService.GetBufferInfo(a2dpEndpoint.Id);
            if (!bufferInfo.IsSupported)
            {
                return Task.FromResult(FeatureHealthStatus.Error(bufferInfo.ErrorMessage ?? "Not supported"));
            }

            return Task.FromResult(FeatureHealthStatus.Ok($"Buffer: {bufferInfo.CurrentMs:F1}ms (min: {bufferInfo.MinMs:F1}ms)"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ProcessingPeriodControl: Exception in ValidateAsync");
            return Task.FromResult(FeatureHealthStatus.Error($"Error: {ex.Message}"));
        }
    }

    private AudioEndpointInfo? GetA2dpEndpoint()
    {
        return _audioService.GetBluetoothEndpoints()
            .FirstOrDefault(e => e.IsPlayback && e.BluetoothProfile == BluetoothAudioProfile.A2dp);
    }
}
