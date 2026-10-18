namespace Constitution.Domain.Health;

/// <summary>
/// One thing Body charts, and the Health Connect read permissions it needs. The id is a stable
/// word for code, not yet a stored key (the choices C-7.1.2 saves will decide that).
/// </summary>
public sealed record HealthSignal(string Id, IReadOnlyList<string> ReadPermissions);

/// <summary>
/// Every Health Connect permission Constitution may declare, in one place (C-7.1.1; TF §1.3, R5).
/// The head's manifest repeats these as string literals (a guard has to read them by content) and
/// <c>HealthManifestTests</c> fails if the two disagree or if the head declares anything else.
///
/// <para><b>Read only, by construction.</b> There is no write permission in this file and no way to
/// build one from it; <c>READ_HEALTH_DATA_HISTORY</c> is absent too, because the brief wants nothing
/// from before install day (the app also filters on its own install date, C-7.1.2).</para>
///
/// <para><b>What was assumed, not verified.</b> The device spike (C-11.4.2) was skipped (D97), so
/// these rest on the platform contract as known, not on a phone:
/// <list type="bullet">
/// <item>Names and the <c>android.permission.health.</c> prefix were read out of the reference
/// assembly's own metadata (Mono.Android, <c>Microsoft.Android.Ref.36</c>): every name below exists
/// there as a literal, so none is typed from memory.</item>
/// <item>"Body composition" is body fat plus lean body mass, the two record types TF §1.3 lists
/// (<c>BodyFatRecord</c>, <c>LeanBodyMassRecord</c>); one signal, two permissions.</item>
/// <item>"Active minutes" has no record type of its own. <c>ExerciseSessionRecord</c> carries a
/// start and an end, so its duration is minutes, which makes <c>READ_EXERCISE</c> the closest honest
/// source. <c>READ_ACTIVE_CALORIES_BURNED</c> is calories, not minutes, and so is left out;
/// <c>READ_ACTIVITY_INTENSITY</c> (moderate and vigorous minutes) exists only in the newest platform
/// and Constitution's floor is Android 14, so it is left out as well. Adding either later is a
/// deliberate edit here and to the guard.</item>
/// <item>"Heart rate variability" is RMSSD on the platform (<c>HeartRateVariabilityRmssdRecord</c>),
/// permission <c>READ_HEART_RATE_VARIABILITY</c>.</item>
/// <item>Whether the owner's scale, watch and bed actually write these records to Health Connect is
/// outside this app; that is for the owner's phone to show.</item>
/// </list></para>
/// </summary>
public static class HealthPermissions
{
    /// <summary>The prefix every Health Connect permission carries.</summary>
    public const string Prefix = "android.permission.health.";

    public const string ReadWeight = Prefix + "READ_WEIGHT";
    public const string ReadBodyFat = Prefix + "READ_BODY_FAT";
    public const string ReadLeanBodyMass = Prefix + "READ_LEAN_BODY_MASS";
    public const string ReadRestingHeartRate = Prefix + "READ_RESTING_HEART_RATE";
    public const string ReadHeartRateVariability = Prefix + "READ_HEART_RATE_VARIABILITY";
    public const string ReadSteps = Prefix + "READ_STEPS";
    public const string ReadExercise = Prefix + "READ_EXERCISE";
    public const string ReadSleep = Prefix + "READ_SLEEP";

    /// <summary>
    /// Lets a worker read while the app is closed, for the overnight recovery check (R5). Where the
    /// platform or the owner withholds it, values are refreshed each time the app opens instead.
    /// </summary>
    public const string ReadInBackground = Prefix + "READ_HEALTH_DATA_IN_BACKGROUND";

    /// <summary>The seven signals the brief charts, each asked for when its chart is first opened.</summary>
    public static IReadOnlyList<HealthSignal> Signals { get; } =
    [
        new("weight", [ReadWeight]),
        new("composition", [ReadBodyFat, ReadLeanBodyMass]),
        new("restingHeartRate", [ReadRestingHeartRate]),
        new("heartRateVariability", [ReadHeartRateVariability]),
        new("steps", [ReadSteps]),
        new("activeMinutes", [ReadExercise]),
        new("sleep", [ReadSleep]),
    ];

    /// <summary>The signals' permissions and the background one: the whole allowlist, and nothing else.</summary>
    public static IReadOnlyList<string> All { get; } =
        [.. Signals.SelectMany(signal => signal.ReadPermissions), ReadInBackground];
}
