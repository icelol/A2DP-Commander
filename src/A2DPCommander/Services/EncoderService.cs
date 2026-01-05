using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace BTAudioDriver.Services;

public sealed class EncoderService : IEncoderService
{
    private const string PipeName = "a2dp-encoder";
    private const string EncoderExeName = "a2dp-encoder.exe";
    private const int ConnectionTimeoutMs = 5000;
    private const int CommandTimeoutMs = 3000;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Process? _serviceProcess;
    private bool _disposed;

    public event EventHandler<EncoderStatusChangedEventArgs>? StatusChanged;

    public bool IsServiceAvailable { get; private set; }
    public bool IsRunning { get; private set; }
    public string? CurrentCodec { get; private set; }
    public uint? CurrentBitrate { get; private set; }
    public ulong FramesEncoded { get; private set; }

    public async Task<bool> CheckServiceAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendCommandAsync(new PingCommand(), cancellationToken);
            IsServiceAvailable = response?.Type == "pong";
            return IsServiceAvailable;
        }
        catch (Exception ex)
        {
            Log.Debug("Encoder service not available: {Error}", ex.Message);
            IsServiceAvailable = false;
            return false;
        }
    }

    public async Task<bool> StartEncoderAsync(string codec, string? quality = null, CancellationToken cancellationToken = default)
    {
        var command = new StartCommand
        {
            Codec = codec,
            Quality = quality
        };

        var response = await SendCommandAsync(command, cancellationToken);
        if (response?.Type == "ok")
        {
            IsRunning = true;
            CurrentCodec = codec;
            OnStatusChanged();
            return true;
        }

        Log.Warning("Failed to start encoder: {Message}", response?.Message);
        return false;
    }

    public async Task<bool> SetQualityAsync(string quality, CancellationToken cancellationToken = default)
    {
        var command = new SetQualityCommand { Quality = quality };
        var response = await SendCommandAsync(command, cancellationToken);
        if (response?.Type == "ok")
        {
            OnStatusChanged();
            return true;
        }

        Log.Warning("Failed to set encoder quality: {Message}", response?.Message);
        return false;
    }

    public async Task<bool> StopEncoderAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(new StopCommand(), cancellationToken);
        if (response?.Type == "ok")
        {
            IsRunning = false;
            CurrentCodec = null;
            CurrentBitrate = null;
            OnStatusChanged();
            return true;
        }

        Log.Warning("Failed to stop encoder: {Message}", response?.Message);
        return false;
    }

    public async Task<EncoderStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(new StatusCommand(), cancellationToken);
        if (response?.Type == "status")
        {
            IsRunning = response.Running ?? false;
            CurrentCodec = response.Codec;
            CurrentBitrate = response.Bitrate;
            FramesEncoded = response.FramesEncoded ?? 0;

            OnStatusChanged();

            return new EncoderStatus(
                IsRunning,
                CurrentCodec,
                CurrentBitrate,
                FramesEncoded
            );
        }

        return null;
    }

    public async Task<bool> StartServiceProcessAsync(CancellationToken cancellationToken = default)
    {
        if (_serviceProcess is { HasExited: false })
        {
            return true;
        }

        var exePath = FindEncoderExecutable();
        if (exePath == null)
        {
            Log.Error("Encoder service executable not found");
            return false;
        }

        try
        {
            _serviceProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            _serviceProcess.Start();
            Log.Information("Started encoder service process: {Pid}", _serviceProcess.Id);

            await Task.Delay(500, cancellationToken);
            return await CheckServiceAvailableAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start encoder service process");
            return false;
        }
    }

    public async Task StopServiceProcessAsync(CancellationToken cancellationToken = default)
    {
        if (_serviceProcess is { HasExited: false })
        {
            try
            {
                await StopEncoderAsync(cancellationToken);
            }
            catch
            {
            }

            try
            {
                _serviceProcess.Kill();
                await _serviceProcess.WaitForExitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Warning("Error stopping encoder service: {Error}", ex.Message);
            }

            _serviceProcess.Dispose();
            _serviceProcess = null;
        }

        IsServiceAvailable = false;
        IsRunning = false;
        OnStatusChanged();
    }

    private async Task<CommandResponse?> SendCommandAsync(ICommand command, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ConnectionTimeoutMs);

            await pipe.ConnectAsync(cts.Token);

            var json = JsonSerializer.Serialize(command, command.GetType(), JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await pipe.WriteAsync(bytes, cancellationToken);
            await pipe.FlushAsync(cancellationToken);

            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(CommandTimeoutMs);

            var buffer = new byte[4096];
            var bytesRead = await pipe.ReadAsync(buffer, readCts.Token);

            if (bytesRead == 0)
            {
                return null;
            }

            var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
            return JsonSerializer.Deserialize<CommandResponse>(responseJson, JsonOptions);
        }
        catch (TimeoutException)
        {
            Log.Warning("Timeout communicating with encoder service");
            return null;
        }
        catch (IOException ex)
        {
            Log.Debug("Pipe error: {Error}", ex.Message);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string? FindEncoderExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        var processDir = Path.GetDirectoryName(Environment.ProcessPath) ?? baseDir;

        var locations = new[]
        {
            Path.Combine(baseDir, EncoderExeName),
            Path.Combine(baseDir, "encoder", EncoderExeName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "A2DPCommander", EncoderExeName),
            Path.Combine(processDir, "..", "..", "..", "encoder-service", "target", "release", EncoderExeName),
            Path.Combine(processDir, "..", "..", "..", "..", "..", "encoder-service", "target", "release", EncoderExeName),
            Path.Combine(processDir, "..", "..", "..", "..", "encoder-service", "target", "release", EncoderExeName),
        };

        foreach (var path in locations)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    Log.Debug("Found encoder at: {Path}", fullPath);
                    return fullPath;
                }
            }
            catch
            {
            }
        }

        Log.Warning("Encoder executable not found in any of the search paths");
        return null;
    }

    private void OnStatusChanged()
    {
        StatusChanged?.Invoke(this, new EncoderStatusChangedEventArgs
        {
            IsRunning = IsRunning,
            Codec = CurrentCodec,
            Bitrate = CurrentBitrate,
            FramesEncoded = FramesEncoded
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _serviceProcess?.Kill();
        }
        catch
        {
        }

        _serviceProcess?.Dispose();
        _lock.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private interface ICommand
    {
        [JsonPropertyName("type")]
        string Type { get; }
    }

    private class PingCommand : ICommand
    {
        [JsonPropertyName("type")]
        public string Type => "ping";
    }

    private class StartCommand : ICommand
    {
        [JsonPropertyName("type")]
        public string Type => "start";

        [JsonPropertyName("codec")]
        public string Codec { get; init; } = "ldac";

        [JsonPropertyName("quality")]
        public string? Quality { get; init; }

        [JsonPropertyName("sample_rate")]
        public uint? SampleRate { get; init; }
    }

    private class StopCommand : ICommand
    {
        [JsonPropertyName("type")]
        public string Type => "stop";
    }

    private class StatusCommand : ICommand
    {
        [JsonPropertyName("type")]
        public string Type => "status";
    }

    private class SetQualityCommand : ICommand
    {
        [JsonPropertyName("type")]
        public string Type => "set_quality";

        [JsonPropertyName("quality")]
        public string Quality { get; init; } = "high";
    }

    private class CommandResponse
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("running")]
        public bool? Running { get; set; }

        [JsonPropertyName("codec")]
        public string? Codec { get; set; }

        [JsonPropertyName("bitrate")]
        public uint? Bitrate { get; set; }

        [JsonPropertyName("frames_encoded")]
        public ulong? FramesEncoded { get; set; }
    }
}
