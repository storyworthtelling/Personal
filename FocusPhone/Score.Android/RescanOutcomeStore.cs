using System.Text.Json;
using Android.Content;
using Codex.Domain.Music;

namespace Score;

/// <summary>
/// What the last incremental library scan found, for the owner's-voice greeting to read on
/// the day's first open (see <see cref="ScoreActivity.ShowGreetingAsync"/>).
///
/// A single logical scan can span more than one WorkManager execution when a large backlog
/// needs several chained runs (<see cref="MusicScanner.ScanAsync"/>'s own time-budget
/// retry). <see cref="RecordRun"/> folds each run's contribution into a running
/// accumulator and only publishes it as <see cref="Completed"/> once a run reports
/// <see cref="MusicScanRunResult.FullyDrained"/> — so a scan still mid-backlog never shows
/// up as the day's finding, and nothing is lost between the runs that got it there.
/// </summary>
public class RescanOutcomeStore(Context context)
{
    private const string PreferencesName = "musicRescan";
    private const string AccumulatedKey = "accumulated";
    private const string CompletedKey = "completed";
    private const string CompletedAtKey = "completedUtc";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private ISharedPreferences Preferences =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    /// <summary>The last fully-drained scan's findings, or null before the first one has
    /// ever completed on this device.</summary>
    public RescanOutcome? Completed
    {
        get
        {
            var json = Preferences.GetString(CompletedKey, null);
            return string.IsNullOrWhiteSpace(json) ? null : Deserialize(json);
        }
    }

    /// <summary>When <see cref="Completed"/> was recorded (UTC), or null before the first one.</summary>
    public DateTimeOffset? CompletedAtUtc
    {
        get
        {
            var ticks = Preferences.GetLong(CompletedAtKey, 0);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    private RescanOutcome Accumulated
    {
        get
        {
            var json = Preferences.GetString(AccumulatedKey, null);
            return string.IsNullOrWhiteSpace(json) ? RescanOutcome.None : Deserialize(json);
        }
    }

    /// <summary>Records one worker execution's contribution.</summary>
    public void RecordRun(MusicScanRunResult run)
    {
        var accumulated = Accumulated.Combine(run.Outcome);
        var edit = Preferences.Edit()!;

        if (run.FullyDrained)
        {
            edit.PutString(CompletedKey, Serialize(accumulated))!
                .PutLong(CompletedAtKey, DateTimeOffset.UtcNow.UtcTicks)!
                .PutString(AccumulatedKey, Serialize(RescanOutcome.None));
        }
        else
        {
            edit.PutString(AccumulatedKey, Serialize(accumulated));
        }

        edit.Apply();
    }

    private static string Serialize(RescanOutcome outcome) => JsonSerializer.Serialize(outcome, SerializerOptions);

    private static RescanOutcome Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RescanOutcome>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return RescanOutcome.None;
        }
    }
}
