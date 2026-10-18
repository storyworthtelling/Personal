namespace Constitution.Domain.Body;

/// <summary>
/// The nine things Body charts: seven read from Health Connect and two derived from the pages and
/// meals (Release C/D). A signal's wire name is its id in <see cref="BodySignals.Id"/>, never this
/// member's name, so renaming a member cannot orphan a stored reading.
/// </summary>
/// <remarks>
/// Canonical units, which every reader converts into before a <see cref="BodyReading"/> exists:
/// weight in pounds (D22); body composition as body-fat percent; resting heart rate in beats a
/// minute; heart-rate variability in milliseconds; steps as a count a day; active minutes a day;
/// sleep in hours a night; strength as the day's done load in pounds; calories as estimated kcal a day.
/// </remarks>
public enum BodySignal
{
    /// <summary>Body weight, pounds.</summary>
    Weight,

    /// <summary>Body composition, charted as body-fat percent.</summary>
    BodyComposition,

    /// <summary>Resting heart rate, beats a minute.</summary>
    RestingHeartRate,

    /// <summary>Heart-rate variability, milliseconds.</summary>
    HeartRateVariability,

    /// <summary>Steps a day.</summary>
    Steps,

    /// <summary>Active minutes a day.</summary>
    ActiveMinutes,

    /// <summary>Sleep, hours a night.</summary>
    Sleep,

    /// <summary>Strength progress: the done workout load, pounds. Derived from the pages.</summary>
    Strength,

    /// <summary>Estimated calories a day. Derived from the meals.</summary>
    Calories,
}
