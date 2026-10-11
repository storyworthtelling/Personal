using Codex.Domain.Diagnostics;
using Pulp.Domain.Catalogue;

namespace Pulp.App;

/// <summary>
/// Pulp's rolling Diagnostics log (Codex's <see cref="DiagnosticLog"/>, in the app's files directory).
/// The one place background trouble goes — a lookup that failed, a cached title that could not be
/// read, a household record a newer writer filled with words this build does not know — since none
/// of it is ever voiced or raised as a notice.
/// </summary>
public sealed class PulpDiagnostics
{
    private const string Tag = "Pulp";

    private readonly DiagnosticLog log;

    public PulpDiagnostics(string filesDirectory) => log = new DiagnosticLog(filesDirectory);

    public void Warn(string source, string message, Exception? exception = null)
    {
        try
        {
            log.Append(DiagnosticLogFormatter.FromException(DateTimeOffset.Now, DiagnosticLevel.Warning, source, message, exception));
        }
        catch (IOException failure)
        {
            // The log must never fail the caller's own work; logcat keeps the line instead.
            Android.Util.Log.Warn(Tag, $"{source}: {message} (diagnostics log unwritable: {failure.Message})");
        }
    }
}

/// <summary>
/// Counts each outside lookup into this month's tally per source and writes it to Diagnostics only
/// (D261): a line at the month's first lookup and every <see cref="LookupMonth.LineEvery"/> after, and
/// the closing total when a month turns over. The tallies are kept in a small file beside the log so
/// a restart does not reset the month.
/// </summary>
public sealed class DiagnosticsLookupCounter : ILookupCounter
{
    private const string FileName = "pulp-lookups.txt";

    private readonly PulpDiagnostics diagnostics;
    private readonly string path;
    private readonly object gate = new();
    private readonly Dictionary<LookupSource, MonthTally> tallies = [];

    public DiagnosticsLookupCounter(string filesDirectory, PulpDiagnostics diagnostics)
    {
        this.diagnostics = diagnostics;
        path = Path.Combine(filesDirectory, FileName);
        Load();
    }

    public void Counted(LookupSource source)
    {
        MonthCount counted;
        lock (gate)
        {
            counted = LookupMonth.Count(tallies.GetValueOrDefault(source), DateTimeOffset.UtcNow);
            tallies[source] = counted.Tally;
            Save();
        }

        if (counted.Closed is { } closed)
        {
            diagnostics.Warn("lookups", $"{source}: {closed.Count} lookups in {closed.Month}.");
        }

        if (LookupMonth.IsMilestone(counted.Tally.Count))
        {
            var allowance = source == LookupSource.StreamingAvailability ? $" of the {LookupMonth.Allowance} allowance" : string.Empty;
            diagnostics.Warn("lookups", $"{source}: {counted.Tally.Count} lookups so far in {counted.Tally.Month}{allowance}.");
        }
    }

    private void Load()
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split('\t');
            if (parts.Length == 3
                && Enum.TryParse<LookupSource>(parts[0], out var source)
                && int.TryParse(parts[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count))
            {
                tallies[source] = new MonthTally(parts[1], count);
            }
            else
            {
                diagnostics.Warn("lookups", $"{FileName} holds a line that is not a tally and was skipped: '{line}'.");
            }
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllLines(path, tallies.Select(pair => $"{pair.Key}\t{pair.Value.Month}\t{pair.Value.Count}"));
        }
        catch (IOException failure)
        {
            diagnostics.Warn("lookups", "The month's lookup tally could not be saved.", failure);
        }
    }
}
