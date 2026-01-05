using BTAudioDriver.Models;

namespace BTAudioDriver.Services;

public interface IAudioEndpointService : IDisposable
{
    event EventHandler<AudioEndpointInfo>? EndpointAdded;

    event EventHandler<AudioEndpointInfo>? EndpointRemoved;

    event EventHandler<AudioEndpointInfo>? DefaultDeviceChanged;

    IReadOnlyList<AudioEndpointInfo> GetPlaybackEndpoints();

    IReadOnlyList<AudioEndpointInfo> GetRecordingEndpoints();

    IReadOnlyList<AudioEndpointInfo> GetBluetoothEndpoints();

    IReadOnlyList<AudioEndpointInfo> GetEndpointsForBluetoothDevice(string deviceName);

    AudioEndpointInfo? GetDefaultPlaybackDevice();

    AudioEndpointInfo? GetDefaultRecordingDevice();

    AudioEndpointInfo? FindVirtualAudioDevice();

    bool SetDefaultPlaybackDevice(string deviceId);

    bool SetDefaultRecordingDevice(string deviceId);

    void Refresh();

    bool MuteDevice(string deviceId, bool mute);

    float? GetDeviceVolume(string deviceId);

    bool SetDeviceVolume(string deviceId, float volume);

    Task<bool> WaitForDeviceStateChangeAsync(string? deviceIdHint, int timeoutMs = 2000, CancellationToken cancellationToken = default);

    BufferSizeInfo GetBufferInfo(string deviceId);

    bool SetBufferSize(string deviceId, long periodIn100Ns);
}
