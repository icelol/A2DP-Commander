using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using BTAudioDriver.Localization;
using BTAudioDriver.Models;
using BTAudioDriver.Services;
using BTAudioDriver.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BTAudioDriver.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<MainViewModel>();

    private readonly IBluetoothService _bluetoothService;
    private readonly IAudioEndpointService _audioService;
    private readonly IProfileManager _profileManager;
    private readonly ISettingsService _settingsService;
    private readonly IProcessWatcherService _processWatcher;
    private readonly IAudioQualityService _audioQualityService;
    private readonly IBluetoothCodecMonitor _codecMonitor;
    private readonly IBluetoothAdapterService _adapterService;
    private readonly TrayIconManager _trayIcon;

    private bool _disposed;
    private DiagnosticsWindow? _diagnosticsWindow;
    private ProfileMode? _modeBeforeAutoSwitch;
    private readonly SemaphoreSlim _deviceConnectionGate = new(1, 1);
    private readonly SemaphoreSlim _modeSwitchGate = new(1, 1);
    private CancellationTokenSource? _deviceConnectionCts;

    [ObservableProperty]
    private DeviceProfileState? _currentState;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Инициализация...";

    private string DefaultDeviceName
    {
        get
        {
            var name = _settingsService.Settings.DefaultDeviceName;
            if (string.IsNullOrEmpty(name))
            {
                name = CurrentState?.DeviceName ?? SelectedDevice?.Name ?? "";
            }
            return name;
        }
    }

    public string DeviceStatus => CurrentState?.DeviceName ?? Strings.Device_NotConnected;

    public bool IsDeviceConnected => IsConnected;

    public string CurrentModeName => CurrentState?.CurrentMode switch
    {
        ProfileMode.Music => Strings.Mode_MusicFull,
        ProfileMode.Calls => Strings.Mode_CallsFull,
        _ => Strings.Mode_Unknown
    };

    public string CodecInfo
    {
        get
        {
            if (CurrentState == null) return "";

            var realCodec = _codecMonitor.CurrentCodec;
            if (realCodec != null)
            {
                return $"{realCodec.GetCodecName()} ({realCodec.GetEstimatedBitrate()} kbps)";
            }

            var qualityInfo = _audioQualityService.GetCurrentQualityInfo(DefaultDeviceName);
            return qualityInfo != null
                ? $"{qualityInfo.CodecName}, {qualityInfo.SampleRate / 1000.0:F1} kHz"
                : "";
        }
    }

    public bool IsMusicMode => CurrentState?.CurrentMode == ProfileMode.Music;

    public bool IsCallsMode => CurrentState?.CurrentMode == ProfileMode.Calls;

    #region Settings Properties

    [ObservableProperty]
    private List<BluetoothDeviceInfo> _availableDevices = new();

    [ObservableProperty]
    private BluetoothDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private ProfileMode _defaultMode = ProfileMode.Music;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private bool _autoSwitchOnConnect = true;

    [ObservableProperty]
    private bool _autoSwitchByApp = true;

    [ObservableProperty]
    private string _selectedLanguage = "ru";

    partial void OnSelectedLanguageChanged(string value)
    {
        Strings.CurrentLanguage = value == "en" ? Language.English : Language.Russian;
        Logger.Information("Language changed to: {Language}", value);
    }

    public List<LanguageOption> AvailableLanguages { get; } = new()
    {
        new LanguageOption { Code = "ru", Name = "Русский" },
        new LanguageOption { Code = "en", Name = "English" }
    };

    #endregion

    #region App Rules Properties

    [ObservableProperty]
    private ObservableCollection<AppProfileRule> _profileRules = new();

    [ObservableProperty]
    private AppProfileRule? _selectedRule;

    #endregion

    #region Audio Quality Properties

    [ObservableProperty]
    private string _currentCodecInfo = "";

    [ObservableProperty]
    private BluetoothCodec _preferredCodec = BluetoothCodec.Auto;

    [ObservableProperty]
    private string _codecDescription = string.Empty;

    [ObservableProperty]
    private bool _disableEnhancements = true;

    [ObservableProperty]
    private bool _setAsDefaultDevice = true;

    [ObservableProperty]
    private bool _mmcssOptimizationsEnabled;

    [ObservableProperty]
    private string _supportedCodecsText = "Загрузка...";

    [ObservableProperty]
    private string _sbcStatusColor = "Green";

    [ObservableProperty]
    private string _aacStatusColor = "Gray";

    [ObservableProperty]
    private string _aacStatusText = "Проверяется...";

    [ObservableProperty]
    private string _aptxStatusColor = "Gray";

    [ObservableProperty]
    private string _aptxStatusText = "Проверяется...";

    [ObservableProperty]
    private string _aptxHdStatusColor = "Gray";

    [ObservableProperty]
    private string _aptxHdStatusText = "Проверяется...";

    [ObservableProperty]
    private string _ldacStatusColor = "Gray";

    [ObservableProperty]
    private string _ldacStatusText = "Проверяется...";

    public BluetoothCodec[] AvailableCodecs => Enum.GetValues<BluetoothCodec>();


    partial void OnPreferredCodecChanged(BluetoothCodec value)
    {
        UpdateCodecDescription();
    }

    private void UpdateCodecDescription()
    {
        CodecDescription = PreferredCodec switch
        {
            BluetoothCodec.Auto => Strings.CodecDesc_Auto,
            BluetoothCodec.SBC => Strings.CodecDesc_SBC,
            BluetoothCodec.AAC => Strings.CodecDesc_AAC,
            BluetoothCodec.AptX => Strings.CodecDesc_AptX,
            BluetoothCodec.AptXHD => Strings.CodecDesc_AptXHD,
            BluetoothCodec.AptXLL => "Минимальная задержка для игр и видео.",
            BluetoothCodec.AptXAdaptive => "Адаптивный битрейт, автонастройка качества.",
            BluetoothCodec.LDAC => Strings.CodecDesc_LDAC,
            _ => ""
        };
    }

    #endregion

    #region Diagnostics Properties

    [ObservableProperty]
    private string _diagBluetoothInfo = "Загрузка...";

    [ObservableProperty]
    private string _diagAudioInfo = "Загрузка...";

    [ObservableProperty]
    private string _diagCodecInfo = "Загрузка...";

    [ObservableProperty]
    private string _bluetoothAdapterName = "";

    [ObservableProperty]
    private bool _isIntelAdapter;

    [ObservableProperty]
    private bool _showIntelAACWarning;

    [ObservableProperty]
    private bool _showReconnectWarning;

    #endregion

    #region Bluetooth Adapter Properties

    [ObservableProperty]
    private List<BluetoothAdapterInfo> _availableAdapters = new();

    [ObservableProperty]
    private BluetoothAdapterInfo? _selectedAdapter;

    [ObservableProperty]
    private bool _isAdapterSwitching;

    #endregion

    public event EventHandler? MinimizeToTrayRequested;

    public event EventHandler? ShowMainWindowRequested;

    public MainViewModel(
        IBluetoothService bluetoothService,
        IAudioEndpointService audioService,
        IProfileManager profileManager,
        ISettingsService settingsService,
        IProcessWatcherService processWatcher,
        IAudioQualityService audioQualityService,
        IBluetoothCodecMonitor codecMonitor,
        IBluetoothAdapterService adapterService)
    {
        _bluetoothService = bluetoothService;
        _audioService = audioService;
        _profileManager = profileManager;
        _settingsService = settingsService;
        _processWatcher = processWatcher;
        _audioQualityService = audioQualityService;
        _codecMonitor = codecMonitor;
        _adapterService = adapterService;

        _trayIcon = new TrayIconManager();
        _trayIcon.ExitRequested += (_, _) => ExitApplication();
        _trayIcon.ShowMainWindowRequested += (_, _) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);

        _profileManager.ProfileModeChanged += OnProfileModeChanged;

        _bluetoothService.DeviceAdded += OnDeviceAdded;
        _bluetoothService.DeviceConnectionChanged += OnDeviceConnectionChanged;

        _settingsService.SettingsChanged += OnSettingsChanged;

        _processWatcher.ProfileChangeRequired += OnProfileChangeRequired;

        Strings.LanguageChanged += OnLanguageChanged;

        _codecMonitor.CodecDetected += OnCodecDetected;

        InitializeInBackground();
    }

    private void OnCodecDetected(object? sender, BluetoothA2DPCodecInfo codecInfo)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Logger.Information("Real codec detected via ETW: {Codec}", codecInfo);
            OnPropertyChanged(nameof(CodecInfo));
            _ = RefreshDiagnosticsAsync();
        });
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(DeviceStatus));
        OnPropertyChanged(nameof(CurrentModeName));

        _ = RefreshDiagnosticsAsync();
    }

    private async void InitializeInBackground()
    {
        try
        {
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex, "Critical error during initialization");
            StatusText = Strings.Status_CriticalError;
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            Logger.Information("Initializing MainViewModel...");

            await _settingsService.LoadAsync();

            _processWatcher.UpdateRules(_settingsService.Settings.ProfileRules);

            var pairedDevices = await _bluetoothService.GetPairedAudioDevicesAsync();
            Logger.Information("Found {Count} paired audio devices on startup", pairedDevices.Count);

            await _bluetoothService.StartWatchingAsync();

            if (_settingsService.Settings.AutoSwitchByApp)
            {
                _processWatcher.StartWatching();
            }

            try
            {
                if (_codecMonitor.IsElevated)
                {
                    _codecMonitor.Start();
                    Logger.Information("Bluetooth codec ETW monitor started");
                }
                else
                {
                    Logger.Information("ETW codec monitor not started: requires administrator privileges");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to start ETW codec monitor");
            }

            LoadSettingsToUI();

            await RefreshDevicesAsync();
            RefreshAdapters();

            if (string.IsNullOrEmpty(_settingsService.Settings.DefaultDeviceName))
            {
                var connectedDevice = AvailableDevices.FirstOrDefault(d => d.IsConnected);
                if (connectedDevice != null)
                {
                    _settingsService.Settings.DefaultDeviceName = connectedDevice.Name;
                    _settingsService.Settings.DefaultDeviceId = connectedDevice.MacAddress;
                    SelectedDevice = connectedDevice;
                    Logger.Information("Auto-selected connected device: {Name}", connectedDevice.Name);
                }
            }

            var savedDeviceName = _settingsService.Settings.DefaultDeviceName;
            if (!string.IsNullOrEmpty(savedDeviceName))
            {
                Logger.Information("Saved device name: {Name}, waiting for audio endpoints...", savedDeviceName);
                await WaitForAudioEndpointsAndRefreshAsync(savedDeviceName);
            }
            else
            {
                await RefreshStateAsync();
            }

            await RefreshDiagnosticsAsync();

            Logger.Information("MainViewModel initialized");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize MainViewModel");
            StatusText = Strings.Status_InitError;
        }
    }

    private async Task RefreshStateAsync(string? deviceName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedDeviceName = deviceName ?? DefaultDeviceName;
            if (string.IsNullOrWhiteSpace(resolvedDeviceName))
            {
                await RunOnUiThreadAsync(() => ApplyProfileState(null));
                return;
            }

            var state = await _profileManager.GetDeviceProfileStateAsync(resolvedDeviceName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await RunOnUiThreadAsync(() => ApplyProfileState(state));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to refresh state");
            await RunOnUiThreadAsync(() => StatusText = Strings.Status_Error);
        }
    }

    private void ApplyProfileState(DeviceProfileState? state)
    {
        CurrentState = state;
        IsConnected = state != null;
        StatusText = state != null
            ? $"{state.DeviceName}: {state.CurrentMode}"
            : Strings.Status_DeviceNotConnected;

        _trayIcon.UpdateState(state);

        OnPropertyChanged(nameof(DeviceStatus));
        OnPropertyChanged(nameof(IsDeviceConnected));
        OnPropertyChanged(nameof(CurrentModeName));
        OnPropertyChanged(nameof(CodecInfo));
        OnPropertyChanged(nameof(IsMusicMode));
        OnPropertyChanged(nameof(IsCallsMode));
    }


    [RelayCommand]
    private void MinimizeToTray()
    {
        MinimizeToTrayRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ToggleModeAsync()
    {
        if (CurrentState?.CurrentMode == ProfileMode.Music)
        {
            await SetCallsModeAsync();
        }
        else
        {
            await SetMusicModeAsync();
        }
    }

    [RelayCommand]
    private async Task SetMusicModeAsync()
    {
        await ApplyModeAsync(ProfileMode.Music);
    }

    [RelayCommand]
    private async Task SetCallsModeAsync()
    {
        await ApplyModeAsync(ProfileMode.Calls);
    }

    private async Task ApplyModeAsync(ProfileMode mode, CancellationToken cancellationToken = default)
    {
        await _modeSwitchGate.WaitAsync(cancellationToken);
        try
        {
            if (!_profileManager.IsRunningAsAdmin)
            {
                await RunOnUiThreadAsync(() =>
                    ShowNotification(Strings.Notification_AdminRequired, System.Windows.Forms.ToolTipIcon.Warning));
                return;
            }

            var deviceName = DefaultDeviceName;
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                await RunOnUiThreadAsync(() => StatusText = Strings.Status_DeviceNotConnected);
                return;
            }

            Logger.Information("Setting {Mode} mode for {DeviceName}...", mode, deviceName);
            await RunOnUiThreadAsync(() =>
                StatusText = mode == ProfileMode.Music ? $"{Strings.Mode_Music}..." : $"{Strings.Mode_Calls}...");

            var success = mode == ProfileMode.Music
                ? await _profileManager.SetMusicModeAsync(deviceName, cancellationToken)
                : await _profileManager.SetCallsModeAsync(deviceName, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (success)
            {
                await RunOnUiThreadAsync(() =>
                {
                    _trayIcon.ShowModeChangedNotification(mode);
                    UpdateStateAfterModeChange(mode, deviceName);
                });

                await Task.Delay(2000, cancellationToken);
                _audioService.Refresh();
                await RefreshStateAsync(deviceName, cancellationToken);
            }
            else
            {
                await RunOnUiThreadAsync(() =>
                {
                    ShowNotification(Strings.Notification_SwitchFailed, System.Windows.Forms.ToolTipIcon.Error);
                    StatusText = Strings.Status_SwitchError;
                });
            }
        }
        finally
        {
            _modeSwitchGate.Release();
        }
    }

    private void UpdateStateAfterModeChange(ProfileMode newMode, string? deviceName = null)
    {
        var resolvedDeviceName = string.IsNullOrWhiteSpace(deviceName) ? DefaultDeviceName : deviceName;
        CurrentState = new DeviceProfileState
        {
            DeviceId = CurrentState?.DeviceId ?? "",
            DeviceName = resolvedDeviceName,
            CurrentMode = newMode,
            IsA2dpEnabled = true,
            IsHfpEnabled = newMode == ProfileMode.Calls,
            HfpDeviceInstanceId = CurrentState?.HfpDeviceInstanceId
        };

        IsConnected = true;
        StatusText = $"{resolvedDeviceName}: {newMode}";

        OnPropertyChanged(nameof(DeviceStatus));
        OnPropertyChanged(nameof(IsDeviceConnected));
        OnPropertyChanged(nameof(CurrentModeName));
        OnPropertyChanged(nameof(CodecInfo));
        OnPropertyChanged(nameof(IsMusicMode));
        OnPropertyChanged(nameof(IsCallsMode));

        _trayIcon.UpdateState(CurrentState);

        Logger.Information("UI updated: mode={Mode}, device={Device}", newMode, resolvedDeviceName);
    }

    private async Task SetModeAsync(ProfileMode mode, CancellationToken cancellationToken = default)
    {
        if (mode == ProfileMode.Music)
            await ApplyModeAsync(ProfileMode.Music, cancellationToken);
        else if (mode == ProfileMode.Calls)
            await ApplyModeAsync(ProfileMode.Calls, cancellationToken);
    }

    [RelayCommand]
    private void OpenDiagnostics()
    {
        if (_diagnosticsWindow != null)
        {
            _diagnosticsWindow.Activate();
            return;
        }

        Logger.Information("Opening diagnostics window");

        var viewModel = new DiagnosticsViewModel(
            _bluetoothService, _audioService, _profileManager, _settingsService, _audioQualityService);
        _diagnosticsWindow = new DiagnosticsWindow(viewModel);
        _diagnosticsWindow.Closed += (_, _) => _diagnosticsWindow = null;
        _diagnosticsWindow.Show();
    }

    private void ExitApplication()
    {
        Logger.Information("Exit requested");
        System.Windows.Application.Current.Shutdown();
    }

    private async void OnProfileModeChanged(object? sender, DeviceProfileState state)
    {
        await RunOnUiThreadAsync(() => ApplyProfileState(state));
        _ = RefreshDiagnosticsAsync();
    }

    private void OnDeviceAdded(object? sender, BluetoothDeviceInfo device)
    {
        Logger.Information("Device added to live list: {Name}", device.Name);
        _ = RefreshDevicesAsync(device);
    }

    private void OnDeviceConnectionChanged(object? sender, BluetoothDeviceInfo device)
    {
        Logger.Information("Device connection changed: {Name} - {Connected}",
            device.Name, device.IsConnected);

        var nextCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _deviceConnectionCts, nextCts);
        previousCts?.Cancel();

        _ = HandleDeviceConnectionChangedAsync(device, nextCts);
    }

    private async Task HandleDeviceConnectionChangedAsync(BluetoothDeviceInfo device, CancellationTokenSource cancellationTokenSource)
    {
        var cancellationToken = cancellationTokenSource.Token;
        var gateTaken = false;

        try
        {
            await _deviceConnectionGate.WaitAsync(cancellationToken);
            gateTaken = true;

            if (device.IsConnected)
            {
                await RefreshDevicesAsync(device);

                await RunOnUiThreadAsync(() =>
                {
                    _settingsService.Settings.DefaultDeviceName = device.Name;
                    _settingsService.Settings.DefaultDeviceId = device.MacAddress;
                    SelectedDevice = AvailableDevices.FirstOrDefault(d => d.Id == device.Id)
                                     ?? AvailableDevices.FirstOrDefault(d => d.MacAddress == device.MacAddress)
                                     ?? AvailableDevices.FirstOrDefault(d => d.Name == device.Name);
                    Logger.Information("Selected connected device: {Name}", device.Name);
                });

                await WaitForAudioEndpointsAndRefreshAsync(device.Name, cancellationToken);
            }
            else
            {
                var isCurrentDevice =
                    _settingsService.Settings.DefaultDeviceName?.Equals(device.Name, StringComparison.OrdinalIgnoreCase) == true ||
                    SelectedDevice?.Name?.Equals(device.Name, StringComparison.OrdinalIgnoreCase) == true ||
                    CurrentState?.DeviceName?.Equals(device.Name, StringComparison.OrdinalIgnoreCase) == true;

                if (isCurrentDevice)
                {
                    Logger.Information("Current device disconnected, clearing state: {Name}", device.Name);
                    await ClearDeviceStateAsync(clearSavedSelection: false);
                }

                await RefreshDevicesAsync();

                if (!isCurrentDevice)
                {
                    await RefreshStateAsync(cancellationToken: cancellationToken);
                }
            }

            if (_settingsService.Settings.ShowNotifications)
            {
                if (device.IsConnected)
                {
                    ShowNotification($"{device.Name} подключено");

                }
                else
                {
                    ShowNotification($"{device.Name} отключено");
                }
            }

            if (device.IsConnected && _settingsService.Settings.AutoSwitchOnConnect)
            {
                var defaultMode = _settingsService.Settings.DefaultMode;
                Logger.Information("Auto-switching to {Mode} mode", defaultMode);
                await SetModeAsync(defaultMode, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("Device connection handling canceled for {DeviceName}", device.Name);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to handle device connection change for {DeviceName}", device.Name);
        }
        finally
        {
            if (gateTaken)
            {
                _deviceConnectionGate.Release();
            }

            Interlocked.CompareExchange(ref _deviceConnectionCts, null, cancellationTokenSource);

            cancellationTokenSource.Dispose();
        }
    }

    private async Task ClearDeviceStateAsync(bool clearSavedSelection)
    {
        await RunOnUiThreadAsync(() =>
        {
            ApplyProfileState(null);

            if (clearSavedSelection)
            {
                _settingsService.Settings.DefaultDeviceName = "";
                _settingsService.Settings.DefaultDeviceId = null;
                SelectedDevice = null;
            }
        });

        if (clearSavedSelection)
        {
            try
            {
                await _settingsService.SaveAsync();
                Logger.Information("Settings saved after clearing selected device");
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to save settings after clearing selected device");
            }
        }

        await RefreshDiagnosticsAsync();
    }

    private async Task WaitForAudioEndpointsAndRefreshAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 18;
        const int delayMs = 1000;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.Debug("Waiting for audio endpoints, attempt {Attempt}/{MaxAttempts}", attempt, maxAttempts);

            _audioService.Refresh();

            await RefreshStateAsync(deviceName, cancellationToken);

            if (CurrentState != null)
            {
                Logger.Information("Audio endpoints found after {Attempt} attempts", attempt);
                return;
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        Logger.Warning("Audio endpoints not found after {MaxAttempts} attempts for device {DeviceName}",
            maxAttempts, deviceName);
    }

    private void OnProfileChangeRequired(object? sender, ProfileChangeEventArgs e)
    {
        if (!_settingsService.Settings.AutoSwitchByApp || !IsConnected)
            return;

        _ = HandleProfileChangeRequiredAsync(e);
    }

    private async Task HandleProfileChangeRequiredAsync(ProfileChangeEventArgs e)
    {
        try
        {
            if (e.RequiredProfile.HasValue)
            {
                _modeBeforeAutoSwitch ??= CurrentState?.CurrentMode;

                var ruleName = e.TriggeringRule?.DisplayName ?? e.ProcessName ?? Strings.App_Application;
                Logger.Information("Profile change required by {Rule}: switching to {Profile}",
                    ruleName, e.RequiredProfile.Value);

                await RunOnUiThreadAsync(() => ShowNotification($"{ruleName}: {GetModeName(e.RequiredProfile.Value)}"));
                await SetModeAsync(e.RequiredProfile.Value);
            }
            else
            {
                var targetMode = _modeBeforeAutoSwitch ?? _settingsService.Settings.DefaultMode;

                Logger.Information("No more profile rules active, switching back to {Mode}", targetMode);

                await RunOnUiThreadAsync(() => ShowNotification(GetModeName(targetMode)));
                await SetModeAsync(targetMode);

                _modeBeforeAutoSwitch = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to apply process-triggered profile change");
        }
    }

    private static string GetModeName(ProfileMode mode) => mode switch
    {
        ProfileMode.Music => Strings.Mode_Music,
        ProfileMode.Calls => Strings.Mode_Calls,
        _ => mode.ToString()
    };

    #region Settings Methods

    private void LoadSettingsToUI()
    {
        var settings = _settingsService.Settings;
        DefaultMode = settings.DefaultMode;
        AutoStart = settings.AutoStart;
        ShowNotifications = settings.ShowNotifications;
        AutoSwitchOnConnect = settings.AutoSwitchOnConnect;
        AutoSwitchByApp = settings.AutoSwitchByApp;
        SelectedLanguage = settings.Language;

        ProfileRules = new ObservableCollection<AppProfileRule>(
            settings.ProfileRules.Select(r => new AppProfileRule
            {
                ProcessName = r.ProcessName,
                DisplayName = r.DisplayName,
                TargetProfile = r.TargetProfile,
                Priority = r.Priority,
                IsEnabled = r.IsEnabled
            }));

        PreferredCodec = settings.AudioQuality.PreferredCodec;
        DisableEnhancements = settings.AudioQuality.DisableEnhancements;
        SetAsDefaultDevice = settings.AudioQuality.SetAsDefaultDevice;
        MmcssOptimizationsEnabled = _audioQualityService?.AreMMCSSOptimizationsApplied() ?? false;
        UpdateCodecDescription();

        RefreshCodecInfo();
    }

    partial void OnSelectedDeviceChanged(BluetoothDeviceInfo? value)
    {
        if (value != null)
        {
            _settingsService.Settings.DefaultDeviceName = value.Name;
            _settingsService.Settings.DefaultDeviceId = value.MacAddress;
        }
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        await RefreshDevicesAsync(null);
    }

    private async Task RefreshDevicesAsync(BluetoothDeviceInfo? preferredDevice, CancellationToken cancellationToken = default)
    {
        _audioService.Refresh();

        var scannedDevices = await _bluetoothService.GetPairedAudioDevicesAsync(cancellationToken);
        var devices = scannedDevices.ToList();

        foreach (var endpointDevice in GetAudioEndpointDevices())
        {
            if (devices.All(d => !IsSameSelectableDevice(d, endpointDevice)))
            {
                devices.Add(endpointDevice);
            }
        }

        if (preferredDevice != null && devices.All(d => d.Id != preferredDevice.Id))
        {
            devices.Add(preferredDevice);
        }

        devices = devices
            .GroupBy(d => d.Id)
            .Select(g => g.OrderByDescending(d => d.IsConnected).First())
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.Name)
            .ToList();

        await RunOnUiThreadAsync(() =>
        {
            AvailableDevices = devices;

            SelectedDevice = preferredDevice != null
                ? devices.FirstOrDefault(d => d.Id == preferredDevice.Id)
                  ?? devices.FirstOrDefault(d => d.MacAddress == preferredDevice.MacAddress)
                  ?? devices.FirstOrDefault(d => d.Name.Equals(preferredDevice.Name, StringComparison.OrdinalIgnoreCase))
                : devices.FirstOrDefault(d =>
                    d.Name.Equals(_settingsService.Settings.DefaultDeviceName, StringComparison.OrdinalIgnoreCase));
        });

        Logger.Debug("Found {Count} paired devices", devices.Count);
    }

    private IEnumerable<BluetoothDeviceInfo> GetAudioEndpointDevices()
    {
        var endpoints = _audioService.GetPlaybackEndpoints()
            .Where(e => e.IsActive && e.IsPlayback)
            .Where(e => e.IsBluetooth || e.IsA2dp || LooksLikeBluetoothAudioEndpoint(e))
            .ToList();

        var defaultPlayback = _audioService.GetDefaultPlaybackDevice();
        if (defaultPlayback is { IsActive: true, IsPlayback: true } &&
            endpoints.All(e => e.Id != defaultPlayback.Id) &&
            (defaultPlayback.IsBluetooth || defaultPlayback.IsA2dp || LooksLikeBluetoothAudioEndpoint(defaultPlayback)))
        {
            endpoints.Add(defaultPlayback);
        }

        foreach (var endpoint in endpoints)
        {
            var displayName = GetSelectableEndpointName(endpoint);
            yield return new BluetoothDeviceInfo
            {
                Id = $"audio-endpoint:{endpoint.Id}",
                Name = displayName,
                BluetoothAddress = 0,
                IsConnected = true,
                IsPaired = true,
                SupportsA2dp = true,
                SupportsHfp = endpoint.IsHfp
            };
        }
    }

    private static bool IsSameSelectableDevice(BluetoothDeviceInfo left, BluetoothDeviceInfo right)
    {
        if (left.Id.Equals(right.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        if (left.BluetoothAddress != 0 && left.BluetoothAddress == right.BluetoothAddress)
            return true;

        return left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSelectableEndpointName(Models.AudioEndpointInfo endpoint)
    {
        var name = endpoint.FriendlyName;

        var openParen = name.LastIndexOf('(');
        var closeParen = name.LastIndexOf(')');
        if (openParen >= 0 && closeParen > openParen)
        {
            var inner = name.Substring(openParen + 1, closeParen - openParen - 1).Trim();
            if (!string.IsNullOrWhiteSpace(inner) &&
                !inner.Contains("Stereo", StringComparison.OrdinalIgnoreCase) &&
                !inner.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) &&
                !inner.Contains("Headset", StringComparison.OrdinalIgnoreCase))
            {
                return inner;
            }
        }

        return name;
    }

    private static bool LooksLikeBluetoothAudioEndpoint(Models.AudioEndpointInfo endpoint)
    {
        var text = $"{endpoint.Id} {endpoint.Name} {endpoint.FriendlyName}";
        string[] bluetoothHints =
        {
            "a2dp",
            "bluetooth",
            "bth",
            "bthenum",
            "bthhfenum",
            "hands-free",
            "handsfree",
            "headphones",
            "headset",
            "stereo",
            "беспровод",
            "bluetooth",
            "гарнитура",
            "наушники"
        };

        if (bluetoothHints.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (LooksLikeExternalNamedEndpoint(endpoint.FriendlyName))
            return true;

        string[] likelySpeakerBrands =
        {
            "anker",
            "bose",
            "charge",
            "flip",
            "harman",
            "jbl",
            "marshall",
            "sony",
            "soundcore",
            "ultimate ears",
            "wonderboom",
            "xtreme"
        };

        return likelySpeakerBrands.Any(brand => text.Contains(brand, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeExternalNamedEndpoint(string friendlyName)
    {
        if (!friendlyName.Contains('(') || !friendlyName.Contains(')'))
            return false;

        string[] builtInHints =
        {
            "amd",
            "conexant",
            "display audio",
            "high definition audio",
            "intel",
            "nvidia",
            "realtek",
            "usb audio",
            "аудио высокой четкости",
            "дисплей",
            "реалтек"
        };

        return builtInHints.All(hint => !friendlyName.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = _settingsService.Settings;

            settings.DefaultMode = DefaultMode;
            settings.ShowNotifications = ShowNotifications;
            settings.AutoSwitchOnConnect = AutoSwitchOnConnect;
            settings.AutoSwitchByApp = AutoSwitchByApp;
            settings.Language = SelectedLanguage;

            settings.ProfileRules = ProfileRules.ToList();

            if (settings.AutoStart != AutoStart)
            {
                _settingsService.SetAutoStart(AutoStart);
            }

            await _settingsService.SaveAsync();

            Logger.Information("Settings saved with {Count} rules", ProfileRules.Count);

            System.Windows.MessageBox.Show(
                Strings.Settings_Saved,
                Strings.AppName,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save settings");
            System.Windows.MessageBox.Show(
                $"{Strings.Settings_SaveError}:\n{ex.Message}",
                Strings.Settings_Error,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    #endregion

    #region Audio Quality Methods

    [RelayCommand]
    private void RefreshCodecInfo()
    {
        try
        {
            var deviceName = _settingsService.Settings.DefaultDeviceName;
            var info = _audioQualityService.GetCurrentQualityInfo(deviceName);
            if (info != null)
            {
                CurrentCodecInfo = $"{Strings.Diag_CodecLabel}: {info.CodecName}\n" +
                                  $"{Strings.Diag_Frequency}: {info.SampleRate / 1000.0:F1} kHz\n" +
                                  $"{Strings.Diag_BitDepth}: {info.BitDepth} bit\n" +
                                  $"{Strings.Diag_Channels}: {info.Channels}\n" +
                                  $"{Strings.Diag_Bitrate}: ~{info.Bitrate} kbps";

                SupportedCodecsText = string.Join(", ", info.SupportedCodecs.Select(GetCodecDisplayName));

                UpdateCodecStatuses(info.SupportedCodecs, info.CurrentCodec);
            }
            else
            {
                CurrentCodecInfo = Strings.Device_NotConnected;
                UpdateCodecStatusesDefault();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to get codec info");
            CurrentCodecInfo = $"Ошибка: {ex.Message}";
            UpdateCodecStatusesDefault();
        }
    }

    private void UpdateCodecStatuses(List<BluetoothCodec> supported, BluetoothCodec current)
    {
        SbcStatusColor = current == BluetoothCodec.SBC ? "#007ACC" : "Green";

        var aacSupported = supported.Contains(BluetoothCodec.AAC);
        AacStatusColor = current == BluetoothCodec.AAC ? "#007ACC" : (aacSupported ? "Green" : "Gray");
        AacStatusText = aacSupported ? "Доступен (Windows 10+)" : Strings.Codec_Note_RequiresWin10;

        var aptxSupported = supported.Contains(BluetoothCodec.AptX);
        AptxStatusColor = current == BluetoothCodec.AptX ? "#007ACC" : (aptxSupported ? "Green" : "Gray");
        AptxStatusText = aptxSupported ? $"{Strings.Audio_AptxHdAvailable} (Qualcomm)" : Strings.Codec_Note_NeedsAdapter;

        var aptxHdSupported = supported.Contains(BluetoothCodec.AptXHD);
        AptxHdStatusColor = current == BluetoothCodec.AptXHD ? "#007ACC" : (aptxHdSupported ? "Green" : "Gray");
        AptxHdStatusText = aptxHdSupported ? Strings.Audio_AptxHdAvailable : Strings.Codec_Note_NeedsAdapter;

        var ldacSupported = supported.Contains(BluetoothCodec.LDAC);
        LdacStatusColor = current == BluetoothCodec.LDAC ? "#007ACC" : (ldacSupported ? "Green" : "Gray");
        LdacStatusText = ldacSupported ? $"{Strings.Audio_AptxHdAvailable} (Sony)" : Strings.Codec_Note_NeedsAdapter;
    }

    private void UpdateCodecStatusesDefault()
    {
        SupportedCodecsText = "SBC";
        SbcStatusColor = "Green";
        AacStatusColor = Environment.OSVersion.Version.Major >= 10 ? "Green" : "Gray";
        AacStatusText = Environment.OSVersion.Version.Major >= 10 ? $"{Strings.Audio_AptxHdAvailable} (Windows 10+)" : Strings.Codec_Note_RequiresWin10;
        AptxStatusColor = "Gray";
        AptxStatusText = Strings.Codec_Note_NeedsAdapter;
        AptxHdStatusColor = "Gray";
        AptxHdStatusText = Strings.Codec_Note_NeedsAdapter;
        LdacStatusColor = "Gray";
        LdacStatusText = Strings.Codec_Note_NeedsAdapter;
    }

    private static string GetCodecDisplayName(BluetoothCodec codec) => codec switch
    {
        BluetoothCodec.Auto => Strings.Codec_Auto,
        BluetoothCodec.SBC => "SBC",
        BluetoothCodec.AAC => "AAC",
        BluetoothCodec.AptX => "aptX",
        BluetoothCodec.AptXHD => "aptX HD",
        BluetoothCodec.AptXLL => "aptX LL",
        BluetoothCodec.AptXAdaptive => "aptX Adaptive",
        BluetoothCodec.LDAC => "LDAC",
        _ => codec.ToString()
    };

    [RelayCommand]
    private async Task ApplyQualitySettingsAsync()
    {
        try
        {
            var deviceName = _settingsService.Settings.DefaultDeviceName;
            var settings = new AudioQualitySettings
            {
                PreferredCodec = PreferredCodec,
                DisableEnhancements = DisableEnhancements,
                SetAsDefaultDevice = SetAsDefaultDevice
            };

            var success = await _audioQualityService.ApplyQualitySettingsAsync(deviceName, settings);

            var currentMMCSS = _audioQualityService.AreMMCSSOptimizationsApplied();
            var mmcssChanged = false;

            if (MmcssOptimizationsEnabled && !currentMMCSS)
            {
                mmcssChanged = _audioQualityService.ApplyMMCSSOptimizations();
            }
            else if (!MmcssOptimizationsEnabled && currentMMCSS)
            {
                mmcssChanged = _audioQualityService.RevertMMCSSOptimizations();
            }

            if (success)
            {
                _settingsService.Settings.AudioQuality = settings;
                await _settingsService.SaveAsync();

                var message = Strings.CurrentLanguage == Language.Russian
                    ? "Настройки качества применены."
                    : "Quality settings applied.";

                if (mmcssChanged)
                {
                    message += Strings.CurrentLanguage == Language.Russian
                        ? "\n\nИзменения MMCSS требуют перезагрузки компьютера."
                        : "\n\nMMCSS changes require a computer restart.";
                }

                System.Windows.MessageBox.Show(
                    message,
                    Strings.AppName,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);

                RefreshCodecInfo();
            }
            else
            {
                System.Windows.MessageBox.Show(
                    Strings.Settings_SaveError,
                    Strings.Dialog_Warning,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to apply quality settings");
            System.Windows.MessageBox.Show(
                $"{Strings.Status_Error}: {ex.Message}",
                Strings.Settings_Error,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    #endregion

    #region App Rules Methods

    [RelayCommand]
    private void AddRule()
    {
        var newRule = new AppProfileRule
        {
            ProcessName = "app.exe",
            DisplayName = "New Application",
            TargetProfile = ProfileMode.Calls,
            Priority = 100,
            IsEnabled = true
        };

        ProfileRules.Add(newRule);
        SelectedRule = newRule;

        Logger.Debug("Added new rule");
    }

    [RelayCommand]
    private void RemoveRule()
    {
        if (SelectedRule != null)
        {
            var rule = SelectedRule;
            ProfileRules.Remove(rule);
            SelectedRule = ProfileRules.FirstOrDefault();

            Logger.Debug("Removed rule: {Rule}", rule.DisplayName);
        }
    }

    [RelayCommand]
    private void MoveRuleUp()
    {
        if (SelectedRule == null) return;

        var index = ProfileRules.IndexOf(SelectedRule);
        if (index > 0)
        {
            ProfileRules.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveRuleDown()
    {
        if (SelectedRule == null) return;

        var index = ProfileRules.IndexOf(SelectedRule);
        if (index < ProfileRules.Count - 1)
        {
            ProfileRules.Move(index, index + 1);
        }
    }

    [RelayCommand]
    private void ResetRulesToDefaults()
    {
        var result = System.Windows.MessageBox.Show(
            Strings.Rules_Reset + "?",
            Strings.Dialog_Confirm,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            ProfileRules = new ObservableCollection<AppProfileRule>(AppProfileRule.GetDefaultRules());
            SelectedRule = ProfileRules.FirstOrDefault();

            Logger.Information("Rules reset to defaults");
        }
    }

    #endregion

    #region Diagnostics Methods

    [RelayCommand]
    private async Task RefreshDiagnosticsAsync()
    {
        try
        {
            var devices = await _bluetoothService.GetPairedAudioDevicesAsync();
            var connectedDevices = devices.Where(d => d.IsConnected).ToList();

            var deviceName = _settingsService.Settings.DefaultDeviceName;
            var deviceEndpoints = string.IsNullOrEmpty(deviceName)
                ? new List<Models.AudioEndpointInfo>()
                : _audioService.GetEndpointsForBluetoothDevice(deviceName).ToList();

            var hasA2dpEndpoint = deviceEndpoints.Any(e => e.IsA2dp);
            var hasHfpEndpoint = deviceEndpoints.Any(e => e.IsHfp);

            var yes = Strings.Diag_Yes;
            var no = Strings.Diag_No;

            if (connectedDevices.Any())
            {
                var device = connectedDevices.First();
                DiagBluetoothInfo = $"{Strings.Diag_Device}: {device.Name}\n" +
                                   $"MAC: {device.MacAddress}\n" +
                                   $"{Strings.Diag_AudioEndpoints}: {deviceEndpoints.Count}\n" +
                                   $"{Strings.Diag_A2dpEndpoint}: {(hasA2dpEndpoint ? yes : no)}\n" +
                                   $"{Strings.Diag_HfpEndpoint}: {(hasHfpEndpoint ? yes : no)}";
            }
            else
            {
                DiagBluetoothInfo = $"{Strings.Diag_NoConnectedDevices}\n{Strings.Diag_PairedDevices}: {devices.Count}";
            }

            var playbackEndpoints = _audioService.GetPlaybackEndpoints();
            var btEndpoints = _audioService.GetBluetoothEndpoints();

            var audioInfo = $"{Strings.Diag_PlaybackDevices}: {playbackEndpoints.Count}\n" +
                           $"{Strings.Diag_BluetoothEndpoints}: {btEndpoints.Count}";

            if (deviceEndpoints.Count > 0)
            {
                audioInfo += $"\n\n{Strings.Diag_EndpointsForDevice}:";
                foreach (var ep in deviceEndpoints)
                {
                    audioInfo += $"\n  - {ep.FriendlyName} ({ep.BluetoothProfile})";
                }
            }

            DiagAudioInfo = audioInfo;

            var realCodec = _codecMonitor.CurrentCodec;
            var qualityInfo = _audioQualityService.GetCurrentQualityInfo(deviceName);

            if (realCodec != null)
            {
                var codecSource = Strings.Diag_RealCodec;
                DiagCodecInfo = $"{Strings.Diag_CodecLabel}: {realCodec.GetCodecName()} ({codecSource})\n" +
                               $"{Strings.Diag_Bitrate}: {realCodec.GetEstimatedBitrate()} kbps";

                if (qualityInfo != null)
                {
                    DiagCodecInfo += $"\n{Strings.Diag_Frequency}: {qualityInfo.SampleRate / 1000.0:F1} kHz\n" +
                                    $"{Strings.Diag_BitDepth}: {qualityInfo.BitDepth} bit\n" +
                                    $"{Strings.Diag_Channels}: {qualityInfo.Channels}";
                }
            }
            else if (qualityInfo != null)
            {
                var codecSource = Strings.Diag_EstimatedCodec;
                DiagCodecInfo = $"{Strings.Diag_CodecLabel}: {qualityInfo.CodecName} ({codecSource})\n" +
                               $"{Strings.Diag_Frequency}: {qualityInfo.SampleRate / 1000.0:F1} kHz\n" +
                               $"{Strings.Diag_BitDepth}: {qualityInfo.BitDepth} bit\n" +
                               $"{Strings.Diag_Channels}: {qualityInfo.Channels}\n" +
                               $"{Strings.Diag_Bitrate}: ~{qualityInfo.Bitrate} kbps";
            }
            else
            {
                DiagCodecInfo = hasA2dpEndpoint
                    ? Strings.Diag_CodecDataUnavailable
                    : Strings.Diag_NoActiveA2dp;
            }

            var codecRegistry = _audioQualityService.GetCodecRegistryInfo();
            var availableCodecs = string.Join(", ", codecRegistry.GetAvailableCodecs());

            var adapterInfo = _audioQualityService.GetBluetoothAdapterInfo();
            BluetoothAdapterName = adapterInfo.Name;
            IsIntelAdapter = adapterInfo.IsIntel;

            var isRealCodecAAC = realCodec?.GetCodecName().Contains("AAC", StringComparison.OrdinalIgnoreCase) == true
                              || qualityInfo?.CodecName.Contains("AAC", StringComparison.OrdinalIgnoreCase) == true;

            var needsReboot = !codecRegistry.AACEnabled && isRealCodecAAC;
            AacNeedsReboot = needsReboot;

            ShowIntelAACWarning = (adapterInfo.IsIntel && codecRegistry.AACEnabled) || needsReboot;

            OnPropertyChanged(nameof(IntelAACWarningText));

            if (needsReboot)
            {
                ShowReconnectWarning = true;
            }

            DiagCodecInfo += $"\n\n{Strings.Diag_BluetoothAdapter} {adapterInfo.Name}";
            DiagCodecInfo += $"\n\n{Strings.Diag_WindowsCodecs}:\n";

            var aacRegistryStatus = codecRegistry.AACEnabled ? Strings.Diag_Enabled : Strings.Diag_Disabled;
            DiagCodecInfo += $"AAC: {aacRegistryStatus}";

            if (needsReboot)
            {
                var warningText = Strings.CurrentLanguage == Language.Russian
                    ? " (но ещё используется - требуется перезагрузка)"
                    : " (still in use - reboot required)";
                DiagCodecInfo += warningText;
            }

            DiagCodecInfo += $"\n{Strings.Diag_AvailableCodecs}: {availableCodecs}";

            IsAACEnabled = codecRegistry.AACEnabled;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to refresh diagnostics");
            DiagBluetoothInfo = Strings.Diag_LoadError;
            DiagAudioInfo = Strings.Diag_LoadError;
            DiagCodecInfo = Strings.Diag_LoadError;
        }
    }

    [ObservableProperty]
    private bool _isAACEnabled = true;

    [ObservableProperty]
    private bool _aacNeedsReboot = false;

    public string IntelAACWarningText => AacNeedsReboot
        ? (Strings.CurrentLanguage == Language.Russian
            ? "AAC отключён в настройках, но всё ещё используется.\nПерезагрузите компьютер для применения изменений."
            : "AAC is disabled in settings but still in use.\nRestart your computer to apply changes.")
        : Strings.Diag_IntelAACWarning;

    [RelayCommand]
    private void ToggleAAC()
    {
        var newState = !IsAACEnabled;
        var result = _audioQualityService.SetAACEnabled(newState);

        if (result)
        {
            IsAACEnabled = newState;

            ShowReconnectWarning = true;

            _ = RefreshDiagnosticsAsync();
        }
        else
        {
            System.Windows.MessageBox.Show(
                Strings.Diag_AACToggleFailed,
                Strings.AppName,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void DismissReconnectWarning()
    {
        ShowReconnectWarning = false;
    }

    [RelayCommand]
    private void RequestReboot()
    {
        var message = Strings.CurrentLanguage == Language.Russian
            ? "Для применения изменений настроек AAC требуется перезагрузка компьютера.\n\nПерезагрузить сейчас?"
            : "A computer restart is required to apply AAC settings changes.\n\nRestart now?";

        var result = System.Windows.MessageBox.Show(
            message,
            Strings.AppName,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            Logger.Information("User requested system reboot to apply AAC changes");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /t 5 /c \"A2DP Commander: Applying Bluetooth codec settings\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initiate system reboot");
                var errorMessage = Strings.CurrentLanguage == Language.Russian
                    ? "Не удалось инициировать перезагрузку.\nПожалуйста, перезагрузите компьютер вручную."
                    : "Failed to initiate reboot.\nPlease restart your computer manually.";
                System.Windows.MessageBox.Show(errorMessage, Strings.AppName, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        var logPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "logs");

        try
        {
            if (Directory.Exists(logPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true
                });
            }
            else
            {
                System.Windows.MessageBox.Show(
                    Strings.Notification_LogsFolderNotFound,
                    Strings.AppName,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open log folder");
        }
    }

    #endregion

    #region Bluetooth Adapter Methods

    [RelayCommand]
    private void RefreshAdapters()
    {
        try
        {
            AvailableAdapters = _adapterService.GetAllAdapters();
            SelectedAdapter = AvailableAdapters.FirstOrDefault(a => a.IsActive)
                            ?? AvailableAdapters.FirstOrDefault(a => a.IsEnabled);
            Logger.Information("Found {Count} Bluetooth adapters", AvailableAdapters.Count);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to refresh adapters");
            AvailableAdapters = new List<BluetoothAdapterInfo>();
        }
    }

    [RelayCommand]
    private async Task SwitchAdapterAsync()
    {
        if (SelectedAdapter == null || SelectedAdapter.IsActive)
            return;

        var result = System.Windows.MessageBox.Show(
            Strings.Adapter_SwitchWarning,
            Strings.Adapter_Warning,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        IsAdapterSwitching = true;

        try
        {
            Logger.Information("Switching to adapter: {Adapter}", SelectedAdapter.Name);

            var success = _adapterService.SetActiveAdapter(SelectedAdapter.DeviceInstanceId);

            if (success)
            {
                Logger.Information("Adapter switch successful");

                System.Windows.MessageBox.Show(
                    Strings.Adapter_SwitchSuccess,
                    Strings.AppName,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);

                await Task.Delay(2000);
                RefreshAdapters();
                await RefreshDevicesAsync();
                await RefreshDiagnosticsAsync();
            }
            else
            {
                Logger.Warning("Failed to switch adapter");

                System.Windows.MessageBox.Show(
                    Strings.Adapter_SwitchFailed,
                    Strings.Dialog_Warning,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error switching adapter");

            System.Windows.MessageBox.Show(
                $"{Strings.Adapter_SwitchFailed}\n{ex.Message}",
                Strings.Status_Error,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsAdapterSwitching = false;
        }
    }

    #endregion

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Logger.Information("Settings changed, updating...");

        _processWatcher.UpdateRules(settings.ProfileRules);

        if (settings.AutoSwitchByApp)
        {
            _processWatcher.StartWatching();
        }
        else
        {
            _processWatcher.StopWatching();
        }

        _ = RefreshStateAsync();
    }

    private static async Task RunOnUiThreadAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }

    private void ShowNotification(string message, System.Windows.Forms.ToolTipIcon icon = System.Windows.Forms.ToolTipIcon.Info)
    {
        if (_settingsService.Settings.ShowNotifications)
        {
            _trayIcon.ShowNotification(Strings.AppName, message, icon);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _profileManager.ProfileModeChanged -= OnProfileModeChanged;
        _bluetoothService.DeviceAdded -= OnDeviceAdded;
        _bluetoothService.DeviceConnectionChanged -= OnDeviceConnectionChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _processWatcher.ProfileChangeRequired -= OnProfileChangeRequired;
        Strings.LanguageChanged -= OnLanguageChanged;
        _codecMonitor.CodecDetected -= OnCodecDetected;

        _deviceConnectionCts?.Cancel();
        _deviceConnectionCts?.Dispose();
        _deviceConnectionGate.Dispose();
        _modeSwitchGate.Dispose();

        _trayIcon.Dispose();
        _processWatcher.Dispose();
        _bluetoothService.Dispose();
        _audioService.Dispose();
        _profileManager.Dispose();
        _codecMonitor.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
