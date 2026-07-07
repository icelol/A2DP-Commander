using System.Collections.Concurrent;
using System.Threading.Channels;
using BTAudioDriver.Models;
using Serilog;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BTAudioDriver.Services;

public class BluetoothService : IBluetoothService
{
    private static readonly ILogger Logger = Log.ForContext<BluetoothService>();

    private static readonly Guid A2dpSinkServiceClass = new("0000110b-0000-1000-8000-00805f9b34fb");
    private static readonly Guid HfpServiceClass = new("0000111e-0000-1000-8000-00805f9b34fb");
    private static readonly Guid AvrcpServiceClass = new("0000110e-0000-1000-8000-00805f9b34fb");

    private readonly ConcurrentDictionary<string, BluetoothDeviceInfo> _devices = new();
    private readonly object _watcherLock = new();
    private DeviceWatcher? _deviceWatcher;
    private Channel<DeviceWatcherEvent>? _deviceEvents;
    private CancellationTokenSource? _watcherCts;
    private Task? _processingTask;
    private long _eventSequence;
    private bool _isWatching;
    private bool _disposed;

    public event EventHandler<BluetoothDeviceInfo>? DeviceAdded;
    public event EventHandler<BluetoothDeviceInfo>? DeviceRemoved;
    public event EventHandler<BluetoothDeviceInfo>? DeviceConnectionChanged;

    public async Task<IReadOnlyList<BluetoothDeviceInfo>> GetPairedAudioDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<BluetoothDeviceInfo>();

        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var deviceInfos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);

        foreach (var deviceInfo in deviceInfos)
        {
            try
            {
                var btDevice = await BluetoothDevice.FromIdAsync(deviceInfo.Id).AsTask(cancellationToken);
                if (btDevice == null) continue;

                var info = await CreateDeviceInfoAsync(btDevice, cancellationToken);
                if (info.IsAudioDevice)
                {
                    devices.Add(info);
                    _devices.TryAdd(info.Id, info);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to get device info for {DeviceId}", deviceInfo.Id);
            }
        }

        Logger.Information("Found {Count} paired audio devices", devices.Count);
        return devices;
    }

    public async Task<IReadOnlyList<BluetoothDeviceInfo>> GetConnectedAudioDevicesAsync(CancellationToken cancellationToken = default)
    {
        var allDevices = await GetPairedAudioDevicesAsync(cancellationToken);
        var connectedDevices = allDevices.Where(d => d.IsConnected).ToList();

        Logger.Information("Found {Count} connected audio devices", connectedDevices.Count);
        return connectedDevices;
    }

    public IEnumerable<BluetoothDeviceInfo> GetPairedDevices()
    {
        return _devices.Values.ToList();
    }

    public async Task<BluetoothDeviceInfo?> GetDeviceByIdAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (_devices.TryGetValue(deviceId, out var cachedDevice))
        {
            try
            {
                var btDevice = await BluetoothDevice.FromIdAsync(deviceId).AsTask(cancellationToken);
                if (btDevice != null)
                {
                    cachedDevice.IsConnected = btDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to update connection status for {DeviceId}", deviceId);
            }
            return cachedDevice;
        }

        try
        {
            var btDevice = await BluetoothDevice.FromIdAsync(deviceId).AsTask(cancellationToken);
            if (btDevice == null) return null;

            var info = await CreateDeviceInfoAsync(btDevice, cancellationToken);
            _devices.TryAdd(info.Id, info);
            return info;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to get device by ID: {DeviceId}", deviceId);
            return null;
        }
    }

    public Task StartWatchingAsync(CancellationToken cancellationToken = default)
    {
        lock (_watcherLock)
        {
            if (_isWatching) return Task.CompletedTask;

            _watcherCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _deviceEvents = Channel.CreateUnbounded<DeviceWatcherEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _processingTask = Task.Run(() => ProcessDeviceEventsAsync(_watcherCts.Token));

            string[] requestedProperties =
            {
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.IsPaired",
                "System.Devices.Aep.DeviceAddress",
                "System.Devices.ContainerId"
            };

            var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);

            _deviceWatcher = DeviceInformation.CreateWatcher(
                selector,
                requestedProperties,
                DeviceInformationKind.AssociationEndpoint);

            _deviceWatcher.Added += OnDeviceAdded;
            _deviceWatcher.Removed += OnDeviceRemoved;
            _deviceWatcher.Updated += OnDeviceUpdated;
            _deviceWatcher.EnumerationCompleted += OnEnumerationCompleted;
            _deviceWatcher.Stopped += OnWatcherStopped;

            _deviceWatcher.Start();
            _isWatching = true;
        }

        Logger.Information("Started watching for Bluetooth devices");
        return Task.CompletedTask;
    }

    public void StopWatching()
    {
        DeviceWatcher? watcher;
        Channel<DeviceWatcherEvent>? events;
        CancellationTokenSource? cts;
        Task? processingTask;

        lock (_watcherLock)
        {
            if (!_isWatching || _deviceWatcher == null) return;

            watcher = _deviceWatcher;
            events = _deviceEvents;
            cts = _watcherCts;
            processingTask = _processingTask;

            _deviceWatcher = null;
            _deviceEvents = null;
            _watcherCts = null;
            _processingTask = null;
            _isWatching = false;
        }

        cts?.Cancel();
        events?.Writer.TryComplete();

        if (watcher.Status == DeviceWatcherStatus.Started ||
            watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
        {
            watcher.Stop();
        }

        watcher.Added -= OnDeviceAdded;
        watcher.Removed -= OnDeviceRemoved;
        watcher.Updated -= OnDeviceUpdated;
        watcher.EnumerationCompleted -= OnEnumerationCompleted;
        watcher.Stopped -= OnWatcherStopped;

        try
        {
            processingTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
        }

        Logger.Information("Stopped watching for Bluetooth devices");
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation deviceInfo)
    {
        EnqueueDeviceEvent(DeviceWatcherEventKind.Added, deviceInfo.Id, deviceInfo, null);
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
    {
        EnqueueDeviceEvent(DeviceWatcherEventKind.Removed, deviceInfoUpdate.Id, null, deviceInfoUpdate);
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
    {
        EnqueueDeviceEvent(DeviceWatcherEventKind.Updated, deviceInfoUpdate.Id, null, deviceInfoUpdate);
    }

    private void EnqueueDeviceEvent(
        DeviceWatcherEventKind kind,
        string deviceId,
        DeviceInformation? deviceInfo,
        DeviceInformationUpdate? deviceInfoUpdate)
    {
        var events = _deviceEvents;
        if (events == null) return;

        var sequence = Interlocked.Increment(ref _eventSequence);
        var queued = events.Writer.TryWrite(new DeviceWatcherEvent(sequence, kind, deviceId, deviceInfo, deviceInfoUpdate));
        if (!queued)
        {
            Logger.Warning("Failed to queue Bluetooth watcher event {Kind} for {DeviceId}", kind, deviceId);
        }
    }

    private async Task ProcessDeviceEventsAsync(CancellationToken cancellationToken)
    {
        var events = _deviceEvents;
        if (events == null) return;

        try
        {
            await foreach (var deviceEvent in events.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await ProcessDeviceEventAsync(deviceEvent, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Error processing Bluetooth watcher event {Kind} for {DeviceId}",
                        deviceEvent.Kind, deviceEvent.DeviceId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessDeviceEventAsync(DeviceWatcherEvent deviceEvent, CancellationToken cancellationToken)
    {
        Logger.Debug("Bluetooth event #{Sequence}: {Kind} {DeviceId}",
            deviceEvent.Sequence, deviceEvent.Kind, deviceEvent.DeviceId);

        switch (deviceEvent.Kind)
        {
            case DeviceWatcherEventKind.Added:
            case DeviceWatcherEventKind.Updated:
                await Task.Delay(250, cancellationToken);
                await UpsertDeviceAsync(deviceEvent.DeviceId, deviceEvent.Kind == DeviceWatcherEventKind.Added, cancellationToken);
                break;

            case DeviceWatcherEventKind.Removed:
                if (_devices.TryRemove(deviceEvent.DeviceId, out var removedDevice))
                {
                    var wasConnected = removedDevice.IsConnected;
                    removedDevice.IsConnected = false;

                    Logger.Information("Device removed: {DeviceName}", removedDevice.Name);
                    DeviceRemoved?.Invoke(this, removedDevice);

                    if (wasConnected)
                    {
                        DeviceConnectionChanged?.Invoke(this, removedDevice);
                    }
                }
                break;
        }
    }

    private async Task UpsertDeviceAsync(string deviceId, bool isAddedEvent, CancellationToken cancellationToken)
    {
        var btDevice = await BluetoothDevice.FromIdAsync(deviceId).AsTask(cancellationToken);
        if (btDevice == null) return;

        var info = await CreateDeviceInfoAsync(btDevice, cancellationToken);
        if (!info.IsAudioDevice) return;

        var hadExisting = _devices.TryGetValue(info.Id, out var existingDevice);
        var wasConnected = existingDevice?.IsConnected ?? false;
        _devices[info.Id] = info;

        if (!hadExisting || isAddedEvent)
        {
            Logger.Information("Device added: {DeviceName} ({Mac})", info.Name, info.MacAddress);
            DeviceAdded?.Invoke(this, info);
        }

        if (!hadExisting || wasConnected != info.IsConnected)
        {
            Logger.Information("Device {DeviceName} connection changed: {Status}",
                info.Name,
                info.IsConnected ? "Connected" : "Disconnected");
            DeviceConnectionChanged?.Invoke(this, info);
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        Logger.Debug("Device enumeration completed. Found {Count} devices", _devices.Count);
    }

    private void OnWatcherStopped(DeviceWatcher sender, object args)
    {
        Logger.Debug("Device watcher stopped");
    }

    private enum DeviceWatcherEventKind
    {
        Added,
        Updated,
        Removed
    }

    private sealed record DeviceWatcherEvent(
        long Sequence,
        DeviceWatcherEventKind Kind,
        string DeviceId,
        DeviceInformation? DeviceInfo,
        DeviceInformationUpdate? DeviceInfoUpdate);

    private async Task<BluetoothDeviceInfo> CreateDeviceInfoAsync(BluetoothDevice btDevice, CancellationToken cancellationToken = default)
    {
        var supportsA2dp = false;
        var supportsHfp = false;
        var supportsAvrcp = false;

        try
        {
            var rfcommServices = await btDevice.GetRfcommServicesAsync().AsTask(cancellationToken);

            foreach (var service in rfcommServices.Services)
            {
                var serviceId = service.ServiceId.Uuid;

                if (serviceId == A2dpSinkServiceClass) supportsA2dp = true;
                else if (serviceId == HfpServiceClass) supportsHfp = true;
                else if (serviceId == AvrcpServiceClass) supportsAvrcp = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not get RFCOMM services for {DeviceName}, assuming audio device", btDevice.Name);
            supportsA2dp = true;
            supportsHfp = true;
        }

        if (!supportsA2dp && !supportsHfp && LooksLikeAudioDevice(btDevice.Name))
        {
            Logger.Information("Treating paired Bluetooth device as audio by name: {DeviceName}", btDevice.Name);
            supportsA2dp = true;
            supportsHfp = true;
        }

        return new BluetoothDeviceInfo
        {
            Id = btDevice.DeviceId,
            Name = btDevice.Name,
            BluetoothAddress = btDevice.BluetoothAddress,
            IsConnected = btDevice.ConnectionStatus == BluetoothConnectionStatus.Connected,
            IsPaired = btDevice.DeviceInformation.Pairing.IsPaired,
            SupportsA2dp = supportsA2dp,
            SupportsHfp = supportsHfp,
            SupportsAvrcp = supportsAvrcp
        };
    }

    private static bool LooksLikeAudioDevice(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        string[] audioNameParts =
        {
            "audio",
            "buds",
            "earbuds",
            "earphone",
            "earphones",
            "hands-free",
            "handsfree",
            "headphone",
            "headphones",
            "headset",
            "speaker",
            "stereo",
            "sound",
            "гарнитура",
            "динамик",
            "звук",
            "наушники",
            "колонка"
        };

        return audioNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopWatching();
        _devices.Clear();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
