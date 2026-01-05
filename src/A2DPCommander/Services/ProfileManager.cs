using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using BTAudioDriver.Models;
using BTAudioDriver.Native;
using Serilog;

namespace BTAudioDriver.Services;

public class ProfileManager : IProfileManager
{
    private static readonly ILogger Logger = Log.ForContext<ProfileManager>();

    private readonly IAudioEndpointService _audioService;
    private readonly Dictionary<string, DeviceProfileState> _deviceStates = new();
    private TransitionConfig _transitionConfig = TransitionConfig.Default;
    private bool _disposed;

    public event EventHandler<DeviceProfileState>? ProfileModeChanged;

    public TransitionConfig TransitionConfig => _transitionConfig;

    public void SetTransitionConfig(TransitionConfig config)
    {
        _transitionConfig = config ?? TransitionConfig.Default;
        Logger.Information("Transition config updated: Polling={Polling}, EventWait={EventWait}, Fade={Fade}, Timeout={Timeout}ms",
            _transitionConfig.UsePolling, _transitionConfig.UseEventWait, _transitionConfig.UseFadeInOut, _transitionConfig.TimeoutMs);
    }

    public bool RequiresAdminRights => true;

    public bool IsRunningAsAdmin
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public ProfileManager(IAudioEndpointService audioService)
    {
        _audioService = audioService;
    }

    public async Task<DeviceProfileState?> GetDeviceProfileStateAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var endpoints = _audioService.GetEndpointsForBluetoothDevice(deviceName);
                if (endpoints.Count == 0)
                {
                    Logger.Warning("No audio endpoints found for device: {DeviceName}", deviceName);
                    return null;
                }

                var a2dpEndpoint = endpoints.FirstOrDefault(e => e.IsA2dp);
                var hfpEndpoint = endpoints.FirstOrDefault(e => e.IsHfp);

                var hfpDeviceId = FindHfpDeviceInstanceId(deviceName);

                var isHfpEnabled = hfpEndpoint != null;

                var hasA2dp = a2dpEndpoint != null ||
                    (endpoints.Count > 0 && endpoints.All(e => !e.IsHfp));

                Logger.Debug("Profile state: A2DP={HasA2dp}, HFP={IsHfp}, Endpoints={Count}",
                    hasA2dp, isHfpEnabled, endpoints.Count);

                var state = new DeviceProfileState
                {
                    DeviceId = a2dpEndpoint?.Id ?? hfpEndpoint?.Id ?? endpoints.FirstOrDefault()?.Id ?? "",
                    DeviceName = deviceName,
                    IsA2dpEnabled = hasA2dp,
                    IsHfpEnabled = isHfpEnabled,
                    HfpDeviceInstanceId = hfpDeviceId,
                    A2dpDeviceInstanceId = null,
                    CurrentMode = DetermineCurrentMode(hasA2dp, isHfpEnabled)
                };

                _deviceStates[deviceName] = state;
                return state;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get profile state for {DeviceName}", deviceName);
                return null;
            }
        }, cancellationToken);
    }

    public async Task<bool> SetMusicModeAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        Logger.Information("Setting Music mode for {DeviceName} (config: Polling={Polling}, EventWait={EventWait})",
            deviceName, _transitionConfig.UsePolling, _transitionConfig.UseEventWait);

        if (!IsRunningAsAdmin)
        {
            Logger.Warning("Admin rights required to change device state");
            return false;
        }

        try
        {
            var hfpDeviceId = FindHfpDeviceInstanceId(deviceName);
            if (hfpDeviceId == null)
            {
                Logger.Warning("HFP device not found for {DeviceName}", deviceName);
                return false;
            }

            var currentDefault = _audioService.GetDefaultPlaybackDevice();
            float? originalVolume = null;

            if (_transitionConfig.UseFadeInOut && currentDefault != null)
            {
                originalVolume = _audioService.GetDeviceVolume(currentDefault.Id);
                if (originalVolume.HasValue)
                {
                    Logger.Debug("Fading out audio on {DeviceId}", currentDefault.Id);
                    await FadeVolumeAsync(currentDefault.Id, originalVolume.Value, 0f, _transitionConfig.FadeTimeMs, cancellationToken);
                }
            }

            var success = await DisableDeviceWithWaitAsync(hfpDeviceId, cancellationToken);

            if (success)
            {
                Logger.Information("Successfully disabled HFP for {DeviceName}", deviceName);

                _audioService.Refresh();

                var endpoints = _audioService.GetEndpointsForBluetoothDevice(deviceName);
                var a2dpEndpoint = endpoints.FirstOrDefault(e => e.IsA2dp && e.IsPlayback);

                if (a2dpEndpoint != null)
                {
                    Logger.Information("Setting A2DP endpoint as default: {EndpointName}", a2dpEndpoint.FriendlyName);
                    _audioService.SetDefaultPlaybackDevice(a2dpEndpoint.Id);

                    if (_transitionConfig.UseFadeInOut)
                    {
                        _audioService.SetDeviceVolume(a2dpEndpoint.Id, 0f);
                        await Task.Delay(50, cancellationToken);
                        await FadeVolumeAsync(a2dpEndpoint.Id, 0f, originalVolume ?? 1f, _transitionConfig.FadeTimeMs, cancellationToken);
                    }
                }
                else
                {
                    Logger.Warning("A2DP playback endpoint not found for {DeviceName}", deviceName);
                }

                if (_deviceStates.TryGetValue(deviceName, out var state))
                {
                    state.IsHfpEnabled = false;
                    state.CurrentMode = ProfileMode.Music;
                    ProfileModeChanged?.Invoke(this, state);
                }
            }
            else if (_transitionConfig.UseFadeInOut && currentDefault != null && originalVolume.HasValue)
            {
                await FadeVolumeAsync(currentDefault.Id, 0f, originalVolume.Value, _transitionConfig.FadeTimeMs, cancellationToken);
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to set Music mode for {DeviceName}", deviceName);
            return false;
        }
    }

    public async Task<bool> SetCallsModeAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        Logger.Information("Setting Calls mode for {DeviceName} (config: Polling={Polling}, EventWait={EventWait})",
            deviceName, _transitionConfig.UsePolling, _transitionConfig.UseEventWait);

        if (!IsRunningAsAdmin)
        {
            Logger.Warning("Admin rights required to change device state");
            return false;
        }

        try
        {
            var hfpDeviceId = FindHfpDeviceInstanceId(deviceName);
            if (hfpDeviceId == null)
            {
                Logger.Warning("HFP device not found for {DeviceName}", deviceName);
                return false;
            }

            var currentDefault = _audioService.GetDefaultPlaybackDevice();
            float? originalVolume = null;

            if (_transitionConfig.UseFadeInOut && currentDefault != null)
            {
                originalVolume = _audioService.GetDeviceVolume(currentDefault.Id);
                if (originalVolume.HasValue)
                {
                    Logger.Debug("Fading out audio on {DeviceId}", currentDefault.Id);
                    await FadeVolumeAsync(currentDefault.Id, originalVolume.Value, 0f, _transitionConfig.FadeTimeMs, cancellationToken);
                }
            }

            var success = await EnableDeviceWithWaitAsync(hfpDeviceId, cancellationToken);

            if (success)
            {
                Logger.Information("Successfully enabled HFP for {DeviceName}", deviceName);

                if (_deviceStates.TryGetValue(deviceName, out var state))
                {
                    state.IsHfpEnabled = true;
                    state.CurrentMode = ProfileMode.Calls;
                    ProfileModeChanged?.Invoke(this, state);
                }

                _audioService.Refresh();

                if (_transitionConfig.UseFadeInOut)
                {
                    var endpoints = _audioService.GetEndpointsForBluetoothDevice(deviceName);
                    var hfpEndpoint = endpoints.FirstOrDefault(e => e.IsHfp && e.IsPlayback);
                    if (hfpEndpoint != null)
                    {
                        _audioService.SetDeviceVolume(hfpEndpoint.Id, 0f);
                        await Task.Delay(50, cancellationToken);
                        await FadeVolumeAsync(hfpEndpoint.Id, 0f, originalVolume ?? 1f, _transitionConfig.FadeTimeMs, cancellationToken);
                    }
                }
            }
            else if (_transitionConfig.UseFadeInOut && currentDefault != null && originalVolume.HasValue)
            {
                await FadeVolumeAsync(currentDefault.Id, 0f, originalVolume.Value, _transitionConfig.FadeTimeMs, cancellationToken);
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to set Calls mode for {DeviceName}", deviceName);
            return false;
        }
    }

    public async Task<ProfileMode> ToggleModeAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        var state = await GetDeviceProfileStateAsync(deviceName, cancellationToken);
        if (state == null) return ProfileMode.Auto;

        if (state.CurrentMode == ProfileMode.Music)
        {
            await SetCallsModeAsync(deviceName, cancellationToken);
            return ProfileMode.Calls;
        }
        else
        {
            await SetMusicModeAsync(deviceName, cancellationToken);
            return ProfileMode.Music;
        }
    }

    private string? FindHfpDeviceInstanceId(string deviceName)
    {
        var guid = SetupApi.GUID_DEVCLASS_MEDIA;
        using var deviceInfoSet = SetupApi.SetupDiGetClassDevs(
            ref guid,
            null,
            IntPtr.Zero,
            SetupApi.DIGCF_PRESENT);

        if (deviceInfoSet.IsInvalid)
        {
            Logger.Warning("Failed to get device info set");
            return null;
        }

        var deviceInfoData = new SetupApi.SP_DEVINFO_DATA
        {
            cbSize = Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>()
        };

        for (var i = 0; SetupApi.SetupDiEnumDeviceInfo(deviceInfoSet, i, ref deviceInfoData); i++)
        {
            var friendlyName = SetupApi.GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SetupApi.SPDRP_FRIENDLYNAME);
            var description = SetupApi.GetDeviceProperty(deviceInfoSet, ref deviceInfoData, SetupApi.SPDRP_DEVICEDESC);

            var name = friendlyName ?? description ?? "";

            if (name.Contains(deviceName, StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Headset", StringComparison.OrdinalIgnoreCase)))
            {
                var instanceId = SetupApi.GetDeviceInstanceId(deviceInfoSet, ref deviceInfoData);
                Logger.Debug("Found HFP device: {Name} ({InstanceId})", name, instanceId);
                return instanceId;
            }
        }

        guid = SetupApi.GUID_DEVCLASS_SOUND;
        using var soundDeviceInfoSet = SetupApi.SetupDiGetClassDevs(
            ref guid,
            null,
            IntPtr.Zero,
            SetupApi.DIGCF_PRESENT);

        if (!soundDeviceInfoSet.IsInvalid)
        {
            deviceInfoData.cbSize = Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>();

            for (var i = 0; SetupApi.SetupDiEnumDeviceInfo(soundDeviceInfoSet, i, ref deviceInfoData); i++)
            {
                var friendlyName = SetupApi.GetDeviceProperty(soundDeviceInfoSet, ref deviceInfoData, SetupApi.SPDRP_FRIENDLYNAME);
                var description = SetupApi.GetDeviceProperty(soundDeviceInfoSet, ref deviceInfoData, SetupApi.SPDRP_DEVICEDESC);

                var name = friendlyName ?? description ?? "";

                if (name.Contains(deviceName, StringComparison.OrdinalIgnoreCase) &&
                    (name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Headset", StringComparison.OrdinalIgnoreCase)))
                {
                    var instanceId = SetupApi.GetDeviceInstanceId(soundDeviceInfoSet, ref deviceInfoData);
                    Logger.Debug("Found HFP device in Sound class: {Name} ({InstanceId})", name, instanceId);
                    return instanceId;
                }
            }
        }

        Logger.Debug("HFP device not found for {DeviceName}", deviceName);
        return null;
    }

    private bool DisableDevice(string instanceId)
    {
        return SetDeviceState(instanceId, false);
    }

    private bool EnableDevice(string instanceId)
    {
        return SetDeviceState(instanceId, true);
    }

    private bool SetDeviceState(string instanceId, bool enable)
    {
        Logger.Debug("SetDeviceState: {InstanceId}, enable={Enable}", instanceId, enable);

        var classGuids = new[] { SetupApi.GUID_DEVCLASS_MEDIA, SetupApi.GUID_DEVCLASS_SOUND };

        foreach (var classGuid in classGuids)
        {
            var guid = classGuid;
            using var deviceInfoSet = SetupApi.SetupDiGetClassDevs(
                ref guid,
                null,
                IntPtr.Zero,
                SetupApi.DIGCF_PRESENT);

            if (deviceInfoSet.IsInvalid)
                continue;

            var deviceInfoData = new SetupApi.SP_DEVINFO_DATA
            {
                cbSize = Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>()
            };

            for (var i = 0; SetupApi.SetupDiEnumDeviceInfo(deviceInfoSet, i, ref deviceInfoData); i++)
            {
                var currentInstanceId = SetupApi.GetDeviceInstanceId(deviceInfoSet, ref deviceInfoData);
                if (currentInstanceId == null)
                    continue;

                if (currentInstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Debug("Found device in class {ClassGuid}", classGuid);

                    var result = SetupApi.SetDeviceEnabled(deviceInfoSet, ref deviceInfoData, enable);

                    if (!result)
                    {
                        var error = Marshal.GetLastWin32Error();
                        Logger.Warning("Failed to {Action} device {InstanceId}: {Error}",
                            enable ? "enable" : "disable", instanceId, new Win32Exception(error).Message);
                    }
                    else
                    {
                        Logger.Information("Successfully {Action} device {InstanceId}",
                            enable ? "enabled" : "disabled", instanceId);
                    }

                    return result;
                }
            }
        }

        Logger.Warning("Device not found by Instance ID: {InstanceId}", instanceId);
        return false;
    }

    private static ProfileMode DetermineCurrentMode(bool hasA2dp, bool hasHfp)
    {
        if (hasHfp) return ProfileMode.Calls;
        if (hasA2dp) return ProfileMode.Music;
        return ProfileMode.Auto;
    }

    private async Task<bool> DisableDeviceWithWaitAsync(string instanceId, CancellationToken cancellationToken)
    {
        var success = await Task.Run(() => DisableDevice(instanceId), cancellationToken);

        if (success)
        {
            await WaitForDeviceTransitionAsync(instanceId, false, cancellationToken);
        }

        return success;
    }

    private async Task<bool> EnableDeviceWithWaitAsync(string instanceId, CancellationToken cancellationToken)
    {
        var success = await Task.Run(() => EnableDevice(instanceId), cancellationToken);

        if (success)
        {
            await WaitForDeviceTransitionAsync(instanceId, true, cancellationToken);
        }

        return success;
    }

    private async Task WaitForDeviceTransitionAsync(string instanceId, bool waitForStarted, CancellationToken cancellationToken)
    {
        var startTime = Environment.TickCount;

        if (_transitionConfig.UseEventWait)
        {
            Logger.Debug("Waiting for MMDevice state change event...");
            var eventReceived = await _audioService.WaitForDeviceStateChangeAsync(null, _transitionConfig.TimeoutMs, cancellationToken);
            if (eventReceived)
            {
                Logger.Debug("MMDevice state change event received");
            }
        }

        if (_transitionConfig.UsePolling)
        {
            var remainingTimeout = Math.Max(0, _transitionConfig.TimeoutMs - (Environment.TickCount - startTime));
            if (remainingTimeout > 0)
            {
                Logger.Debug("Polling device state (remaining timeout: {Timeout}ms)...", remainingTimeout);
                var deviceReady = await PollDeviceStateAsync(instanceId, waitForStarted, remainingTimeout, cancellationToken);
                Logger.Debug("Device polling result: {Ready}", deviceReady);
            }
        }

        if (!_transitionConfig.UsePolling && !_transitionConfig.UseEventWait)
        {
            Logger.Debug("Using fixed delay: {Delay}ms", _transitionConfig.TimeoutMs);
            await Task.Delay(_transitionConfig.TimeoutMs, cancellationToken);
        }
    }

    private async Task<bool> PollDeviceStateAsync(string instanceId, bool waitForStarted, int timeoutMs, CancellationToken cancellationToken)
    {
        var classGuids = new[] { SetupApi.GUID_DEVCLASS_MEDIA, SetupApi.GUID_DEVCLASS_SOUND };

        foreach (var classGuid in classGuids)
        {
            var guid = classGuid;
            using var deviceInfoSet = SetupApi.SetupDiGetClassDevs(
                ref guid,
                null,
                IntPtr.Zero,
                SetupApi.DIGCF_PRESENT);

            if (deviceInfoSet.IsInvalid)
                continue;

            var deviceInfoData = new SetupApi.SP_DEVINFO_DATA
            {
                cbSize = Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>()
            };

            for (var i = 0; SetupApi.SetupDiEnumDeviceInfo(deviceInfoSet, i, ref deviceInfoData); i++)
            {
                var currentInstanceId = SetupApi.GetDeviceInstanceId(deviceInfoSet, ref deviceInfoData);
                if (currentInstanceId?.Equals(instanceId, StringComparison.OrdinalIgnoreCase) == true)
                {
                    var devInst = deviceInfoData.DevInst;
                    return await SetupApi.WaitForDeviceStateAsync(devInst, waitForStarted, timeoutMs, cancellationToken);
                }
            }
        }

        await Task.Delay(Math.Min(timeoutMs, 500), cancellationToken);
        return false;
    }

    private async Task FadeVolumeAsync(string deviceId, float from, float to, int durationMs, CancellationToken cancellationToken)
    {
        const int steps = 10;
        var stepDuration = durationMs / steps;
        var volumeStep = (to - from) / steps;

        for (var i = 1; i <= steps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var volume = from + (volumeStep * i);
            _audioService.SetDeviceVolume(deviceId, volume);
            await Task.Delay(stepDuration, cancellationToken);
        }

        _audioService.SetDeviceVolume(deviceId, to);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _deviceStates.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
