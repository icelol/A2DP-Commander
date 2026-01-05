namespace BTAudioDriver.Models;

public sealed class BufferSizeInfo
{
    public double CurrentMs { get; init; }

    public double MinMs { get; init; }

    public double MaxMs { get; init; }

    public double DefaultMs { get; init; }

    public long CurrentPeriod { get; init; }

    public long MinPeriod { get; init; }

    public long MaxPeriod { get; init; }

    public long DefaultPeriod { get; init; }

    public bool IsSupported { get; init; }

    public string? ErrorMessage { get; init; }

    public static BufferSizeInfo NotSupported(string reason) => new()
    {
        IsSupported = false,
        ErrorMessage = reason
    };

    public static BufferSizeInfo FromPeriods(long currentPeriod, long minPeriod, long maxPeriod, long defaultPeriod)
    {
        double periodToMs(long period) => period / 10000.0;

        return new BufferSizeInfo
        {
            CurrentPeriod = currentPeriod,
            MinPeriod = minPeriod,
            MaxPeriod = maxPeriod,
            DefaultPeriod = defaultPeriod,
            CurrentMs = periodToMs(currentPeriod),
            MinMs = periodToMs(minPeriod),
            MaxMs = periodToMs(maxPeriod),
            DefaultMs = periodToMs(defaultPeriod),
            IsSupported = true
        };
    }
}
