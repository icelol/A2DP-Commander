using BTAudioDriver.Localization;
using BTAudioDriver.Models;
using BTAudioDriver.Models.Backups;
using Serilog;

namespace BTAudioDriver.Services.Features.Handlers;

public sealed class ExternalEncoderHandler : IFeatureHandler
{
    private readonly IEncoderService _encoderService;
    private readonly IAudioEndpointService? _audioService;
    private readonly IBluetoothAdapterService? _adapterService;

    public ExternalEncoderHandler(IEncoderService encoderService, IAudioEndpointService? audioService = null, IBluetoothAdapterService? adapterService = null)
    {
        _encoderService = encoderService;
        _audioService = audioService;
        _adapterService = adapterService;
    }

    public FeatureId FeatureId => FeatureId.ExternalEncoder;

    public async Task<(bool CanActivate, string? Reason)> CanActivateAsync()
    {
        var adapterCheck = CheckExternalTransmitterRequired();
        if (!adapterCheck.CanProceed)
        {
            return (false, adapterCheck.Reason);
        }

        var available = await _encoderService.CheckServiceAvailableAsync();
        if (available)
        {
            return (true, null);
        }

        var started = await _encoderService.StartServiceProcessAsync();
        if (started)
        {
            return (true, null);
        }

        return (false, "A2DP Encoder Service not available. Install a2dp-encoder.exe to enable this feature.");
    }

    private (bool CanProceed, string? Reason) CheckExternalTransmitterRequired()
    {
        if (_adapterService == null)
        {
            return (true, null);
        }

        var activeAdapter = _adapterService.GetActiveAdapter();
        if (activeAdapter == null)
        {
            return (false, Strings.Get("Feature.NoBluetoothAdapter"));
        }

        if (!activeAdapter.SupportsLDAC && !activeAdapter.SupportsAptXHD)
        {
            var manufacturer = !string.IsNullOrEmpty(activeAdapter.Manufacturer)
                ? $" ({activeAdapter.Manufacturer})"
                : "";
            return (false, string.Format(Strings.Get("Feature.AdapterNoLdacAptx"), manufacturer));
        }

        return (true, null);
    }

    public async Task<FeatureActivationResult> ActivateAsync()
    {
        Log.Information("ExternalEncoder: Activating");

        var wasRunning = _encoderService.IsRunning;
        var previousCodec = _encoderService.CurrentCodec;
        string? originalDeviceId = null;

        if (_audioService != null)
        {
            var currentDefault = _audioService.GetDefaultPlaybackDevice();
            originalDeviceId = currentDefault?.Id;

            var virtualDevice = _audioService.FindVirtualAudioDevice();
            if (virtualDevice != null)
            {
                Log.Information("ExternalEncoder: Switching to virtual audio device: {Name}", virtualDevice.FriendlyName);
                _audioService.SetDefaultPlaybackDevice(virtualDevice.Id);
            }
            else
            {
                Log.Warning("ExternalEncoder: No virtual audio device found (VB-Audio Cable recommended)");
            }
        }

        if (!_encoderService.IsServiceAvailable)
        {
            var started = await _encoderService.StartServiceProcessAsync();
            if (!started)
            {
                if (originalDeviceId != null && _audioService != null)
                {
                    _audioService.SetDefaultPlaybackDevice(originalDeviceId);
                }
                return FeatureActivationResult.Fail("Failed to start encoder service");
            }
        }

        var success = await _encoderService.StartEncoderAsync("ldac", "high");
        if (!success)
        {
            if (originalDeviceId != null && _audioService != null)
            {
                _audioService.SetDefaultPlaybackDevice(originalDeviceId);
            }
            return FeatureActivationResult.Fail("Failed to start LDAC encoder");
        }

        var backup = new EncoderServiceBackup
        {
            FeatureId = FeatureId.ExternalEncoder,
            OriginalDefaultDeviceId = originalDeviceId,
            WasServiceRunning = wasRunning,
            PreviousCodec = previousCodec
        };

        Log.Information("ExternalEncoder: Activated with LDAC encoder");
        return FeatureActivationResult.Ok(backup);
    }

    public async Task<FeatureDeactivationResult> DeactivateAsync(BackupData? backup)
    {
        Log.Information("ExternalEncoder: Deactivating");

        if (_encoderService.IsRunning)
        {
            await _encoderService.StopEncoderAsync();
        }

        if (backup is EncoderServiceBackup encoderBackup)
        {
            if (!string.IsNullOrEmpty(encoderBackup.OriginalDefaultDeviceId) && _audioService != null)
            {
                Log.Information("ExternalEncoder: Restoring original audio device");
                _audioService.SetDefaultPlaybackDevice(encoderBackup.OriginalDefaultDeviceId);
            }

            if (!encoderBackup.WasServiceRunning)
            {
                await _encoderService.StopServiceProcessAsync();
            }
        }

        Log.Information("ExternalEncoder: Deactivated");
        return FeatureDeactivationResult.Ok();
    }

    public async Task<FeatureHealthStatus> ValidateAsync()
    {
        var available = await _encoderService.CheckServiceAvailableAsync();
        if (!available)
        {
            return FeatureHealthStatus.Unavailable("Encoder service not running");
        }

        var status = await _encoderService.GetStatusAsync();
        if (status == null)
        {
            return FeatureHealthStatus.Warning("Cannot get encoder status");
        }

        if (!status.Running)
        {
            return FeatureHealthStatus.Ok("Encoder service ready");
        }

        return FeatureHealthStatus.Ok($"Encoding: {status.Codec} @ {status.Bitrate} kbps, {status.FramesEncoded} frames");
    }
}
