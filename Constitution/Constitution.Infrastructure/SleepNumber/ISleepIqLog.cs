namespace Constitution.Infrastructure.SleepNumber;

/// <summary>
/// Where the SleepIQ client says what the service answered. A phrase is an outcome ("sleepiq sign-in: 200"),
/// never a body, token, name or reading, so the Android head can hand it straight to the diagnostics log.
/// </summary>
public interface ISleepIqLog
{
    /// <summary>Records one short outcome phrase.</summary>
    void Outcome(string phrase);
}
