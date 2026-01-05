using BTAudioDriver.Models;

namespace BTAudioDriver.Services;

public interface IAudioLatencyService
{
    LatencyInfo GetLatencyInfo(string deviceId);

    bool IsSupported { get; }
}
