namespace BTAudioDriver.Models;

public sealed class LatencyInfo
{
    public double CurrentMs { get; init; }

    public double MinMs { get; init; }

    public double MaxMs { get; init; }

    public double DefaultMs { get; init; }

    public int SampleRate { get; init; }

    public bool IsSupported { get; init; }

    public string? ErrorMessage { get; init; }

    public static LatencyInfo NotSupported(string reason) => new()
    {
        IsSupported = false,
        ErrorMessage = reason
    };

    public static LatencyInfo FromPeriods(
        uint currentFrames,
        uint minFrames,
        uint maxFrames,
        uint defaultFrames,
        int sampleRate)
    {
        double framesToMs(uint frames) => frames * 1000.0 / sampleRate;

        return new LatencyInfo
        {
            CurrentMs = framesToMs(currentFrames),
            MinMs = framesToMs(minFrames),
            MaxMs = framesToMs(maxFrames),
            DefaultMs = framesToMs(defaultFrames),
            SampleRate = sampleRate,
            IsSupported = true
        };
    }
}
