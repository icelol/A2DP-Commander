namespace BTAudioDriver.Models;

public sealed class TransitionConfig
{
    public bool UsePolling { get; set; } = true;

    public bool UseEventWait { get; set; } = true;

    public bool UseFadeInOut { get; set; } = false;

    public int TimeoutMs { get; set; } = 3000;

    public int FadeTimeMs { get; set; } = 50;

    public static TransitionConfig Default => new()
    {
        UsePolling = false,
        UseEventWait = false,
        UseFadeInOut = false,
        TimeoutMs = 500
    };

    public static TransitionConfig Smart => new()
    {
        UsePolling = true,
        UseEventWait = true,
        UseFadeInOut = true,
        TimeoutMs = 3000,
        FadeTimeMs = 50
    };

    public TransitionConfig Clone() => new()
    {
        UsePolling = UsePolling,
        UseEventWait = UseEventWait,
        UseFadeInOut = UseFadeInOut,
        TimeoutMs = TimeoutMs,
        FadeTimeMs = FadeTimeMs
    };
}
