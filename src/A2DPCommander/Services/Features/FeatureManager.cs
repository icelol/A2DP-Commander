using System.IO;
using System.Text.Json;
using BTAudioDriver.Models;
using Serilog;

namespace BTAudioDriver.Services.Features;

public sealed class FeatureManager : IFeatureManager, IDisposable
{
    private readonly Dictionary<FeatureId, IFeatureHandler> _handlers = new();
    private readonly Dictionary<FeatureId, FeatureStateInfo> _states = new();
    private readonly Dictionary<FeatureId, BackupData?> _backups = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _stateFilePath;

    private bool _disposed;

    public event EventHandler<FeatureStateChangedEventArgs>? StateChanged;

    public FeatureManager()
    {
        var appFolder = AppDomain.CurrentDomain.BaseDirectory;
        _stateFilePath = Path.Combine(appFolder, "feature-state.json");

        foreach (FeatureId featureId in Enum.GetValues<FeatureId>())
        {
            _states[featureId] = new FeatureStateInfo
            {
                FeatureId = featureId,
                State = FeatureState.Disabled
            };
            _backups[featureId] = null;
        }
    }

    public void RegisterHandler(IFeatureHandler handler)
    {
        _handlers[handler.FeatureId] = handler;
        Log.Debug("Registered handler for {FeatureId}", handler.FeatureId);
    }

    public bool IsEnabled(FeatureId featureId)
    {
        return _states.TryGetValue(featureId, out var info)
            && info.State == FeatureState.Enabled;
    }

    public FeatureState GetState(FeatureId featureId)
    {
        return _states.TryGetValue(featureId, out var info)
            ? info.State
            : FeatureState.Disabled;
    }

    public FeatureStateInfo GetStateInfo(FeatureId featureId)
    {
        return _states.TryGetValue(featureId, out var info)
            ? info
            : new FeatureStateInfo { FeatureId = featureId };
    }

    public IReadOnlyList<FeatureStateInfo> GetAllStates()
    {
        return _states.Values.ToList();
    }

    public (bool CanEnable, string? Reason) CanEnable(FeatureId featureId)
    {
        if (!_handlers.TryGetValue(featureId, out var handler))
        {
            return (false, "Handler not registered");
        }

        return (true, null);
    }

    public async Task<(bool CanActivate, string? Reason)> CanActivateAsync(FeatureId featureId)
    {
        if (!_handlers.TryGetValue(featureId, out var handler))
        {
            return (false, "Handler not registered");
        }

        return await handler.CanActivateAsync();
    }

    public IReadOnlyList<FeatureId> GetConflicts(FeatureId featureId)
    {
        return FeatureConflicts.GetConflicts(featureId);
    }

    public async Task<FeatureOperationResult> EnableAsync(FeatureId featureId)
    {
        await _lock.WaitAsync();
        try
        {
            Log.Information("Enabling feature {FeatureId}", featureId);

            if (!_handlers.TryGetValue(featureId, out var handler))
            {
                Log.Warning("Handler not registered for {FeatureId}", featureId);
                return FeatureOperationResult.Fail("Handler not registered");
            }

            var currentState = GetState(featureId);
            if (currentState == FeatureState.Enabled)
            {
                Log.Debug("Feature {FeatureId} already enabled", featureId);
                return FeatureOperationResult.Ok(FeatureState.Enabled);
            }

            SetState(featureId, FeatureState.Enabling);

            var (canActivate, reason) = await handler.CanActivateAsync();
            if (!canActivate)
            {
                Log.Warning("Cannot activate {FeatureId}: {Reason}", featureId, reason);
                SetState(featureId, FeatureState.Disabled);
                return FeatureOperationResult.Fail(reason ?? "Cannot activate");
            }

            var result = await handler.ActivateAsync();
            if (!result.Success)
            {
                Log.Error("Failed to activate {FeatureId}: {Error}",
                    featureId, result.ErrorMessage);
                SetState(featureId, FeatureState.Error, result.ErrorMessage);
                return FeatureOperationResult.Fail(result.ErrorMessage ?? "Activation failed");
            }

            _backups[featureId] = result.Backup;
            SetState(featureId, FeatureState.Enabled);

            await SaveStateAsync();

            Log.Information("Feature {FeatureId} enabled successfully", featureId);
            return FeatureOperationResult.Ok(FeatureState.Enabled);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while enabling {FeatureId}", featureId);
            SetState(featureId, FeatureState.Error, ex.Message);
            return FeatureOperationResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<FeatureOperationResult> DisableAsync(FeatureId featureId)
    {
        await _lock.WaitAsync();
        try
        {
            Log.Information("Disabling feature {FeatureId}", featureId);

            if (!_handlers.TryGetValue(featureId, out var handler))
            {
                return FeatureOperationResult.Fail("Handler not registered");
            }

            var currentState = GetState(featureId);
            if (currentState == FeatureState.Disabled)
            {
                return FeatureOperationResult.Ok(FeatureState.Disabled);
            }

            SetState(featureId, FeatureState.Disabling);

            var backup = _backups[featureId];
            var result = await handler.DeactivateAsync(backup);

            if (!result.Success)
            {
                Log.Error("Failed to deactivate {FeatureId}: {Error}",
                    featureId, result.ErrorMessage);
                SetState(featureId, FeatureState.Error, result.ErrorMessage);
                return FeatureOperationResult.Fail(result.ErrorMessage ?? "Deactivation failed");
            }

            _backups[featureId] = null;
            SetState(featureId, FeatureState.Disabled);

            await SaveStateAsync();

            Log.Information("Feature {FeatureId} disabled successfully", featureId);
            return FeatureOperationResult.Ok(FeatureState.Disabled);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while disabling {FeatureId}", featureId);
            SetState(featureId, FeatureState.Error, ex.Message);
            return FeatureOperationResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<FeatureOperationResult> RollbackAsync(FeatureId featureId)
    {
        await _lock.WaitAsync();
        try
        {
            Log.Information("Rolling back feature {FeatureId}", featureId);

            if (!_handlers.TryGetValue(featureId, out var handler))
            {
                return FeatureOperationResult.Fail("Handler not registered");
            }

            var backup = _backups[featureId];
            if (backup == null)
            {
                Log.Debug("No backup available for {FeatureId}", featureId);
                SetState(featureId, FeatureState.Disabled);
                return FeatureOperationResult.Ok(FeatureState.Disabled);
            }

            SetState(featureId, FeatureState.RollingBack);

            var result = await handler.DeactivateAsync(backup);

            _backups[featureId] = null;
            SetState(featureId, FeatureState.Disabled);

            await SaveStateAsync();

            if (!result.Success)
            {
                Log.Warning("Rollback completed with errors for {FeatureId}: {Error}",
                    featureId, result.ErrorMessage);
            }

            Log.Information("Feature {FeatureId} rolled back", featureId);
            return FeatureOperationResult.Ok(FeatureState.Disabled);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while rolling back {FeatureId}", featureId);
            SetState(featureId, FeatureState.Error, ex.Message);
            return FeatureOperationResult.Fail(ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<FeatureOperationResult> RollbackAllAsync()
    {
        Log.Information("Rolling back all features");

        var errors = new List<string>();
        var enabledFeatures = _states
            .Where(kvp => kvp.Value.State == FeatureState.Enabled)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var featureId in enabledFeatures)
        {
            var result = await RollbackAsync(featureId);
            if (!result.Success)
            {
                errors.Add($"{featureId}: {result.ErrorMessage}");
            }
        }

        if (errors.Count > 0)
        {
            return FeatureOperationResult.Fail(string.Join("; ", errors));
        }

        return FeatureOperationResult.Ok(FeatureState.Disabled);
    }

    public async Task<FeatureHealthStatus> ValidateAsync(FeatureId featureId)
    {
        if (!_handlers.TryGetValue(featureId, out var handler))
        {
            return FeatureHealthStatus.Unavailable("Handler not registered");
        }

        if (GetState(featureId) != FeatureState.Enabled)
        {
            return FeatureHealthStatus.Unavailable("Feature not enabled");
        }

        return await handler.ValidateAsync();
    }

    public async Task LoadStateAsync()
    {
        foreach (var (featureId, handler) in _handlers)
        {
            if (GetState(featureId) == FeatureState.Enabled)
                continue;

            try
            {
                var health = await handler.ValidateAsync();
                if (health.Level == HealthLevel.Ok)
                {
                    Log.Information("Feature {FeatureId} detected as active, marking enabled", featureId);
                    SetState(featureId, FeatureState.Enabled);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to check feature {FeatureId} state", featureId);
            }
        }
    }

    public async Task SaveStateAsync()
    {
        try
        {
            var enabledFeatures = _states
                .Where(kvp => kvp.Value.State == FeatureState.Enabled)
                .Select(kvp => kvp.Key)
                .ToList();

            var data = new FeaturePersistenceData
            {
                EnabledFeatures = enabledFeatures,
                LastSaved = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_stateFilePath, json);
            Log.Debug("Feature state saved");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save feature state");
        }
    }

    private void SetState(FeatureId featureId, FeatureState newState, string? error = null)
    {
        var info = _states[featureId];
        var oldState = info.State;

        info.State = newState;
        info.ErrorMessage = error;
        info.LastStateChange = DateTime.UtcNow;
        info.HasBackup = _backups[featureId] != null;

        if (oldState != newState)
        {
            StateChanged?.Invoke(this, new FeatureStateChangedEventArgs
            {
                FeatureId = featureId,
                OldState = oldState,
                NewState = newState
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}

internal sealed class FeaturePersistenceData
{
    public List<FeatureId> EnabledFeatures { get; set; } = [];

    public DateTime LastSaved { get; set; }
}
