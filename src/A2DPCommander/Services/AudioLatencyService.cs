using System.Runtime.InteropServices;
using BTAudioDriver.Models;
using BTAudioDriver.Native;
using NAudio.CoreAudioApi;
using Serilog;

namespace BTAudioDriver.Services;

public class AudioLatencyService : IAudioLatencyService
{
    private static readonly ILogger Logger = Log.ForContext<AudioLatencyService>();
    private static readonly Guid IID_IAudioClient3 = new("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42");

    private readonly MMDeviceEnumerator _enumerator;

    public AudioLatencyService()
    {
        _enumerator = new MMDeviceEnumerator();
    }

    public bool IsSupported => Environment.OSVersion.Version.Major >= 10;

    public LatencyInfo GetLatencyInfo(string deviceId)
    {
        if (!IsSupported)
        {
            return LatencyInfo.NotSupported("IAudioClient3 requires Windows 10 or later");
        }

        try
        {
            using var device = _enumerator.GetDevice(deviceId);
            return GetLatencyInfoFromDevice(device);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to get latency info for device {DeviceId}", deviceId);
            return LatencyInfo.NotSupported($"Error: {ex.Message}");
        }
    }

    private LatencyInfo GetLatencyInfoFromDevice(MMDevice device)
    {
        var formatPtr = IntPtr.Zero;
        var currentFormatPtr = IntPtr.Zero;

        try
        {
            var deviceInterfacePtr = GetIMMDevicePtr(device);
            if (deviceInterfacePtr == IntPtr.Zero)
            {
                return LatencyInfo.NotSupported("Failed to get IMMDevice pointer");
            }

            var immDevice = (IMMDevice)Marshal.GetObjectForIUnknown(deviceInterfacePtr);

            var iid = IID_IAudioClient3;
            var hr = immDevice.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var audioClientObj);

            if (hr != 0)
            {
                Logger.Debug("IAudioClient3 activation failed: 0x{HR:X8}", hr);
                return LatencyInfo.NotSupported($"IAudioClient3 not supported: 0x{hr:X8}");
            }

            var audioClient3 = audioClientObj as IAudioClient3;
            if (audioClient3 == null)
            {
                return LatencyInfo.NotSupported("Failed to cast to IAudioClient3");
            }

            var hrMixFormat = audioClient3.GetMixFormat(out formatPtr);
            if (hrMixFormat != 0 || formatPtr == IntPtr.Zero)
            {
                return LatencyInfo.NotSupported($"GetMixFormat failed: 0x{hrMixFormat:X8}");
            }

            var waveFormat = Marshal.PtrToStructure<WaveFormatEx>(formatPtr);
            var sampleRate = (int)waveFormat.SampleRate;

            var hrPeriod = audioClient3.GetSharedModeEnginePeriod(
                formatPtr,
                out var defaultPeriod,
                out var fundamentalPeriod,
                out var minPeriod,
                out var maxPeriod);

            if (hrPeriod != 0)
            {
                return LatencyInfo.NotSupported($"GetSharedModeEnginePeriod failed: 0x{hrPeriod:X8}");
            }

            var hrCurrent = audioClient3.GetCurrentSharedModeEnginePeriod(out currentFormatPtr, out var currentPeriod);
            if (hrCurrent != 0)
            {
                currentPeriod = defaultPeriod;
                Logger.Debug("GetCurrentSharedModeEnginePeriod failed (0x{HR:X8}), using default period", hrCurrent);
            }

            Logger.Information(
                "Latency info for {Device}: Current={Current} frames ({CurrentMs:F1}ms), " +
                "Min={Min} ({MinMs:F1}ms), Max={Max} ({MaxMs:F1}ms), Default={Default} ({DefaultMs:F1}ms), SampleRate={SampleRate}Hz",
                device.FriendlyName,
                currentPeriod, currentPeriod * 1000.0 / sampleRate,
                minPeriod, minPeriod * 1000.0 / sampleRate,
                maxPeriod, maxPeriod * 1000.0 / sampleRate,
                defaultPeriod, defaultPeriod * 1000.0 / sampleRate,
                sampleRate);

            return LatencyInfo.FromPeriods(currentPeriod, minPeriod, maxPeriod, defaultPeriod, sampleRate);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Exception getting latency info for {Device}", device.FriendlyName);
            return LatencyInfo.NotSupported($"Exception: {ex.Message}");
        }
        finally
        {
            if (currentFormatPtr != IntPtr.Zero)
                AudioClient3Helper.FreeCoTaskMem(currentFormatPtr);
            if (formatPtr != IntPtr.Zero)
                AudioClient3Helper.FreeCoTaskMem(formatPtr);
        }
    }

    private static IntPtr GetIMMDevicePtr(MMDevice device)
    {
        var field = typeof(MMDevice).GetField("devicePtr",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (field == null)
        {
            var prop = typeof(MMDevice).GetProperty("DevicePointer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (prop != null)
            {
                return (IntPtr)(prop.GetValue(device) ?? IntPtr.Zero);
            }
            return IntPtr.Zero;
        }

        return (IntPtr)(field.GetValue(device) ?? IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SampleRate;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    private const uint CLSCTX_ALL = 0x17;

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }
}
