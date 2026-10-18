using Constitution.Domain.Health;

namespace Constitution.Tests.Health;

/// <summary>
/// The Domain record of what Constitution may ask Health Connect for: the seven signals the brief
/// charts, and the one background permission (R5). The manifest guard compares the head against it.
/// </summary>
public class HealthPermissionsTests
{
    [Fact]
    public void ThereAreSevenSignals()
    {
        Assert.Equal(
            ["weight", "composition", "restingHeartRate", "heartRateVariability", "steps", "activeMinutes", "sleep"],
            HealthPermissions.Signals.Select(signal => signal.Id));
    }

    [Fact]
    public void EverySignalAsksForAtLeastOneReadPermission()
    {
        Assert.All(HealthPermissions.Signals, signal =>
        {
            Assert.NotEmpty(signal.ReadPermissions);
            Assert.All(signal.ReadPermissions, permission =>
                Assert.StartsWith(HealthPermissions.Prefix + "READ_", permission, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AllIsTheSignalsPlusBackgroundAndNothingElse()
    {
        var expected = HealthPermissions.Signals.SelectMany(signal => signal.ReadPermissions)
            .Append(HealthPermissions.ReadInBackground)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, HealthPermissions.All.Order(StringComparer.Ordinal));
        Assert.Equal(HealthPermissions.All.Count, HealthPermissions.All.Distinct().Count());
    }

    [Fact]
    public void NoWriteAndNoHistoryPermissionIsAllowed()
    {
        Assert.DoesNotContain(HealthPermissions.All, permission => permission.Contains("WRITE_", StringComparison.Ordinal));
        Assert.DoesNotContain(HealthPermissions.All, permission => permission.EndsWith("READ_HEALTH_DATA_HISTORY", StringComparison.Ordinal));
    }

    [Fact]
    public void BackgroundIsNotASignal()
    {
        // A signal is asked when its chart opens; the background permission is asked once, separately.
        Assert.DoesNotContain(HealthPermissions.Signals, signal => signal.ReadPermissions.Contains(HealthPermissions.ReadInBackground));
    }
}
