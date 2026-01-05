using BTAudioDriver.Models;

namespace BTAudioDriver.Services.Features;

public static class FeatureConflicts
{
    private static readonly Dictionary<FeatureId, FeatureId[]> ConflictMatrix = new()
    {
        [FeatureId.ProcessingPeriodControl] = [FeatureId.ExternalEncoder],
        [FeatureId.ExternalEncoder] = [FeatureId.ProcessingPeriodControl, FeatureId.LdacRegistry],
        [FeatureId.LdacRegistry] = [FeatureId.ExternalEncoder]
    };

    public static IReadOnlyList<FeatureId> GetConflicts(FeatureId featureId)
    {
        return ConflictMatrix.TryGetValue(featureId, out var conflicts)
            ? conflicts
            : [];
    }

    public static bool HasConflict(FeatureId featureId, FeatureId other)
    {
        var conflicts = GetConflicts(featureId);
        return conflicts.Contains(other);
    }

    public static IReadOnlyList<FeatureId> GetActiveConflicts(
        FeatureId featureId,
        Func<FeatureId, bool> isEnabled)
    {
        return GetConflicts(featureId)
            .Where(isEnabled)
            .ToList();
    }
}
