using Constitution.Domain.Body;

namespace Constitution.Domain.Health;

/// <summary>
/// Which of <see cref="HealthPermissions"/> each Body signal needs. The permission list is keyed by its own
/// words (<see cref="HealthSignal.Id"/>); Body's signals by theirs (<see cref="BodySignals.Id"/>); this is the one
/// place the two meet, so a reader asks about a <see cref="BodySignal"/> and never about a string.
/// </summary>
public static class HealthSignalPermissions
{
    private static string PermissionKey(BodySignal signal) => signal switch
    {
        BodySignal.Weight => "weight",
        BodySignal.BodyComposition => "composition",
        BodySignal.RestingHeartRate => "restingHeartRate",
        BodySignal.HeartRateVariability => "heartRateVariability",
        BodySignal.Steps => "steps",
        BodySignal.ActiveMinutes => "activeMinutes",
        BodySignal.Sleep => "sleep",
        _ => throw new ArgumentException($"{signal} is worked out from the pages and meals; it has no Health Connect permission.", nameof(signal)),
    };

    /// <summary>Every read permission the signal's chart asks for.</summary>
    public static IReadOnlyList<string> For(BodySignal signal)
    {
        var key = PermissionKey(signal);
        return HealthPermissions.Signals.Single(entry => entry.Id == key).ReadPermissions;
    }

    /// <summary>
    /// The permission whose grant lets the signal be read. For body composition that is body fat (the
    /// charted unit is body-fat percent); lean body mass is asked for beside it but is not what the chart draws.
    /// </summary>
    public static string Primary(BodySignal signal) => For(signal)[0];
}
