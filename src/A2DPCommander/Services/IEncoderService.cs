namespace BTAudioDriver.Services;

public interface IEncoderService : IDisposable
{
    event EventHandler<EncoderStatusChangedEventArgs>? StatusChanged;

    bool IsServiceAvailable { get; }

    bool IsRunning { get; }

    string? CurrentCodec { get; }

    uint? CurrentBitrate { get; }

    ulong FramesEncoded { get; }

    Task<bool> CheckServiceAvailableAsync(CancellationToken cancellationToken = default);

    Task<bool> StartEncoderAsync(string codec, string? quality = null, CancellationToken cancellationToken = default);

    Task<bool> SetQualityAsync(string quality, CancellationToken cancellationToken = default);

    Task<bool> StopEncoderAsync(CancellationToken cancellationToken = default);

    Task<EncoderStatus?> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<bool> StartServiceProcessAsync(CancellationToken cancellationToken = default);

    Task StopServiceProcessAsync(CancellationToken cancellationToken = default);
}

public class EncoderStatusChangedEventArgs : EventArgs
{
    public bool IsRunning { get; init; }
    public string? Codec { get; init; }
    public uint? Bitrate { get; init; }
    public ulong FramesEncoded { get; init; }
}

public record EncoderStatus(
    bool Running,
    string? Codec,
    uint? Bitrate,
    ulong FramesEncoded
);
