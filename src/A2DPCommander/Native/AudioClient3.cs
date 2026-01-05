using System.Runtime.InteropServices;

namespace BTAudioDriver.Native;

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig]
    int Initialize(
        int ShareMode,
        uint StreamFlags,
        long hnsBufferDuration,
        long hnsPeriodicity,
        IntPtr pFormat,
        IntPtr AudioSessionGuid);

    [PreserveSig]
    int GetBufferSize(out uint pNumBufferFrames);

    [PreserveSig]
    int GetStreamLatency(out long phnsLatency);

    [PreserveSig]
    int GetCurrentPadding(out uint pNumPaddingFrames);

    [PreserveSig]
    int IsFormatSupported(int ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);

    [PreserveSig]
    int GetMixFormat(out IntPtr ppDeviceFormat);

    [PreserveSig]
    int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int SetEventHandle(IntPtr eventHandle);

    [PreserveSig]
    int GetService(ref Guid riid, out IntPtr ppv);
}

[ComImport]
[Guid("726778CD-F60A-4eda-82DE-E47610CD78AA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient2 : IAudioClient
{
    [PreserveSig]
    new int Initialize(int ShareMode, uint StreamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr AudioSessionGuid);
    [PreserveSig]
    new int GetBufferSize(out uint pNumBufferFrames);
    [PreserveSig]
    new int GetStreamLatency(out long phnsLatency);
    [PreserveSig]
    new int GetCurrentPadding(out uint pNumPaddingFrames);
    [PreserveSig]
    new int IsFormatSupported(int ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
    [PreserveSig]
    new int GetMixFormat(out IntPtr ppDeviceFormat);
    [PreserveSig]
    new int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
    [PreserveSig]
    new int Start();
    [PreserveSig]
    new int Stop();
    [PreserveSig]
    new int Reset();
    [PreserveSig]
    new int SetEventHandle(IntPtr eventHandle);
    [PreserveSig]
    new int GetService(ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int IsOffloadCapable(int Category, out bool pbOffloadCapable);

    [PreserveSig]
    int SetClientProperties(IntPtr pProperties);

    [PreserveSig]
    int GetBufferSizeLimits(IntPtr pFormat, bool bEventDriven, out long phnsMinBufferDuration, out long phnsMaxBufferDuration);
}

[ComImport]
[Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient3 : IAudioClient2
{
    [PreserveSig]
    new int Initialize(int ShareMode, uint StreamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr AudioSessionGuid);
    [PreserveSig]
    new int GetBufferSize(out uint pNumBufferFrames);
    [PreserveSig]
    new int GetStreamLatency(out long phnsLatency);
    [PreserveSig]
    new int GetCurrentPadding(out uint pNumPaddingFrames);
    [PreserveSig]
    new int IsFormatSupported(int ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
    [PreserveSig]
    new int GetMixFormat(out IntPtr ppDeviceFormat);
    [PreserveSig]
    new int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
    [PreserveSig]
    new int Start();
    [PreserveSig]
    new int Stop();
    [PreserveSig]
    new int Reset();
    [PreserveSig]
    new int SetEventHandle(IntPtr eventHandle);
    [PreserveSig]
    new int GetService(ref Guid riid, out IntPtr ppv);
    [PreserveSig]
    new int IsOffloadCapable(int Category, out bool pbOffloadCapable);
    [PreserveSig]
    new int SetClientProperties(IntPtr pProperties);
    [PreserveSig]
    new int GetBufferSizeLimits(IntPtr pFormat, bool bEventDriven, out long phnsMinBufferDuration, out long phnsMaxBufferDuration);

    [PreserveSig]
    int GetSharedModeEnginePeriod(
        IntPtr pFormat,
        out uint pDefaultPeriodInFrames,
        out uint pFundamentalPeriodInFrames,
        out uint pMinPeriodInFrames,
        out uint pMaxPeriodInFrames);

    [PreserveSig]
    int GetCurrentSharedModeEnginePeriod(
        out IntPtr ppFormat,
        out uint pCurrentPeriodInFrames);

    [PreserveSig]
    int InitializeSharedAudioStream(
        uint StreamFlags,
        uint PeriodInFrames,
        IntPtr pFormat,
        IntPtr AudioSessionGuid);
}

internal static class AudioClient3Helper
{
    private static readonly Guid IID_IAudioClient3 = new("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42");

    [DllImport("ole32.dll")]
    private static extern int CoTaskMemFree(IntPtr pv);

    public static bool TryGetAudioClient3(IntPtr devicePtr, out IAudioClient3? audioClient3)
    {
        audioClient3 = null;

        try
        {
            var iid = IID_IAudioClient3;
            var obj = Marshal.GetObjectForIUnknown(devicePtr);

            if (obj is IAudioClient3 client3)
            {
                audioClient3 = client3;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public static void FreeCoTaskMem(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            CoTaskMemFree(ptr);
        }
    }
}
