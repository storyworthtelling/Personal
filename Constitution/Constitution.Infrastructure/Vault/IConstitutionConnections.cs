namespace Constitution.Infrastructure.Vault;

/// <summary>
/// One connection value the vault can restore into this install, named without ever carrying the value.
/// A restore result lists these so a screen can say which connections came back, and a log line can say
/// how many, with nothing secret anywhere near either.
/// </summary>
public enum VaultField
{
    /// <summary>The household Sleep Number login (shared vault, Constitution's section).</summary>
    SleepNumberLogin,

    /// <summary>The household Sleep Number password (shared vault, Constitution's section).</summary>
    SleepNumberPassword,

    /// <summary>The reMarkable device token (shared vault, the section Foreword also uses).</summary>
    RemarkableDeviceToken,

    /// <summary>The shared Claude key (shared vault).</summary>
    ClaudeApiKey,

    /// <summary>The shared Gemini key (shared vault).</summary>
    GeminiApiKey,

    /// <summary>This person's Health Connect choices (personal vault).</summary>
    HealthConnectChoices,

    /// <summary>This person's work schedule, written by Foreword and only ever read here (personal vault).</summary>
    WorkSchedule,
}

/// <summary>
/// The app's local copy of its connections: what the settings screen shows and the clients read. The
/// Android head implements it over the phone's preferences; tests use an in-memory one. A value that
/// has never been entered is the empty string, never null, and the empty string is how "missing" is
/// told apart from "set" everywhere in the vault sync.
/// </summary>
public interface IConstitutionConnections
{
    /// <summary>The household Sleep Number login, or empty.</summary>
    string SleepNumberLogin { get; set; }

    /// <summary>The household Sleep Number password, or empty.</summary>
    string SleepNumberPassword { get; set; }

    /// <summary>The reMarkable device token, or empty.</summary>
    string RemarkableDeviceToken { get; set; }

    /// <summary>The shared Claude key, or empty.</summary>
    string ClaudeApiKey { get; set; }

    /// <summary>The shared Gemini key, or empty.</summary>
    string GeminiApiKey { get; set; }

    /// <summary>This person's Health Connect choices as the JSON the app keeps, or empty.</summary>
    string HealthConnectChoicesJson { get; set; }

    /// <summary>This person's work schedule as the JSON Foreword keeps, or empty. Restored here, never published from here.</summary>
    string WorkScheduleJson { get; set; }
}
