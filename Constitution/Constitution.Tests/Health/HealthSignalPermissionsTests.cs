using Constitution.Domain.Body;
using Constitution.Domain.Health;

namespace Constitution.Tests.Health;

/// <summary>Each Health Connect signal maps to the read permissions already declared, and to nothing else.</summary>
public class HealthSignalPermissionsTests
{
    [Fact]
    public void EverySignalMapsToDeclaredReadPermissions()
    {
        foreach (var signal in BodySignals.HealthConnect)
        {
            var permissions = HealthSignalPermissions.For(signal);
            Assert.NotEmpty(permissions);
            Assert.All(permissions, permission => Assert.Contains(permission, HealthPermissions.All));
            Assert.DoesNotContain(HealthPermissions.ReadInBackground, permissions);
        }
    }

    [Fact]
    public void ThePrimaryPermissionIsTheFirstListed()
    {
        Assert.Equal(HealthPermissions.ReadWeight, HealthSignalPermissions.Primary(BodySignal.Weight));
        Assert.Equal(HealthPermissions.ReadBodyFat, HealthSignalPermissions.Primary(BodySignal.BodyComposition));
        Assert.Equal(HealthPermissions.ReadExercise, HealthSignalPermissions.Primary(BodySignal.ActiveMinutes));
    }

    [Fact]
    public void TheSevenSignalsUseSevenDistinctPrimaries()
    {
        var primaries = BodySignals.HealthConnect.Select(HealthSignalPermissions.Primary).ToList();

        Assert.Equal(7, primaries.Distinct().Count());
    }

    [Theory]
    [InlineData(BodySignal.Strength)]
    [InlineData(BodySignal.Calories)]
    public void ADerivedSignalHasNoPermission(BodySignal signal)
    {
        Assert.Throws<ArgumentException>(() => HealthSignalPermissions.For(signal));
    }
}
