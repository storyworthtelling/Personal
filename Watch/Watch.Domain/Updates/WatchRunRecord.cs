using System.Text.Json;
using Storyworthtelling.Time;

namespace Watch.Domain.Updates;

/// <summary>
/// The watch's last run as Flyleaf keeps it for Settings' quiet line: when, how far it got, what the watch was behind
/// on, what went and what didn't, and the watch's latest report (replaced by every report that arrives later).
/// </summary>
public sealed record WatchRunRecord(
    DateTimeOffset At,
    WatchReach Reach,
    IReadOnlyList<WatchApp> Due,
    IReadOnlyList<WatchApp> Sent,
    IReadOnlyList<WatchApp> NotSent,
    InstalledReport? Report)
{
    /// <summary>The record built from a run's answer and decisions.</summary>
    public static WatchRunRecord Of(
        DateTimeOffset at,
        WatchAnswer answer,
        IReadOnlyList<WatchSendDecision> decisions,
        IReadOnlyList<WatchApp> sent,
        IReadOnlyList<WatchApp> notSent) =>
        new(at, answer.Reach,
            [.. decisions.Where(decision => decision.Verdict != WatchSendVerdict.AlreadyCurrent)
                .Select(decision => new WatchApp(decision.Apk.Candidate.Package, decision.Apk.Candidate.VersionCode))],
            sent, notSent, answer.Report);

    /// <summary>The same run, with a report that came after it.</summary>
    public WatchRunRecord WithReport(InstalledReport report) => this with { Report = report };
}

/// <summary>The stored run: UTF-8 JSON in Flyleaf's preferences. Never shown, never sent.</summary>
public static class WatchRunCodec
{
    // Stored words, never shown or sent.
    private const string AtField = "at";
    private const string ReachField = "reach";
    private const string DueField = "due";
    private const string SentField = "sent";
    private const string NotSentField = "notSent";
    private const string ReportField = "report";

    private static readonly (WatchReach Reach, string Word)[] ReachWords =
    [
        (WatchReach.NoWatch, "no-watch"),
        (WatchReach.Unreachable, "unreachable"),
        (WatchReach.NoAnswer, "no-answer"),
        (WatchReach.Unreadable, "unreadable"),
        (WatchReach.Newer, "newer"),
        (WatchReach.Answered, "answered"),
    ];

    public static string Encode(WatchRunRecord run)
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteString(AtField, run.At);
            writer.WriteString(ReachField, ReachWords.Single(pair => pair.Reach == run.Reach).Word);
            WriteApps(writer, DueField, run.Due);
            WriteApps(writer, SentField, run.Sent);
            WriteApps(writer, NotSentField, run.NotSent);
            if (run.Report is { } report)
            {
                // The report as the watch's own codec writes it, so one reader reads both.
                writer.WritePropertyName(ReportField);
                writer.WriteRawValue(InstalledCodec.Encode(report));
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(json.ToArray());
    }

    /// <summary>The stored run, or none: a run that will not read leaves the line quiet until the next one.</summary>
    public static WatchRunRecord? Decode(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(stored);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(AtField, out var at) || at.ValueKind != JsonValueKind.String || !at.TryGetDateTimeOffset(out var when)
                || !root.TryGetProperty(ReachField, out var said) || said.ValueKind != JsonValueKind.String
                || ReachWords.Where(pair => string.Equals(pair.Word, said.GetString(), StringComparison.Ordinal)).Select(pair => (WatchReach?)pair.Reach).FirstOrDefault() is not { } reach
                || ReadApps(root, DueField) is not { } due
                || ReadApps(root, SentField) is not { } sent
                || ReadApps(root, NotSentField) is not { } notSent)
            {
                return null;
            }

            InstalledReport? report = null;
            if (root.TryGetProperty(ReportField, out var storedReport))
            {
                if (InstalledCodec.Decode(System.Text.Encoding.UTF8.GetBytes(storedReport.GetRawText())).Message is not InstalledReport read)
                {
                    return null;
                }
                report = read;
            }
            return new WatchRunRecord(when, reach, due, sent, notSent, report);
        }
        catch (JsonException)
        {
            // Damaged in Flyleaf's preferences: the line stays quiet until the next run writes a fresh one.
            return null;
        }
    }

    private static void WriteApps(Utf8JsonWriter writer, string field, IReadOnlyList<WatchApp> apps)
    {
        writer.WritePropertyName(field);
        writer.WriteStartArray();
        foreach (var app in apps)
        {
            writer.WriteStartObject();
            writer.WriteString(InstalledCodec.PackageField, app.Package);
            writer.WriteNumber(InstalledCodec.VersionCodeField, app.VersionCode);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static List<WatchApp>? ReadApps(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var apps = new List<WatchApp>();
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty(InstalledCodec.PackageField, out var package) || package.ValueKind != JsonValueKind.String
                || package.GetString() is not { Length: > 0 } name
                || !entry.TryGetProperty(InstalledCodec.VersionCodeField, out var code) || code.ValueKind != JsonValueKind.Number
                || !code.TryGetInt64(out var versionCode))
            {
                return null;
            }
            apps.Add(new WatchApp(name, versionCode));
        }
        return apps;
    }
}

/// <summary>Settings' one sentence about the watch's last update run, under the phone's own line.</summary>
public static class WatchUpdateLine
{
    /// <summary>The sentence, or null when there is nothing to say: no run yet, or no watch paired.</summary>
    public static string? Say(WatchRunRecord? run, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (run is null || run.Reach == WatchReach.NoWatch)
        {
            return null;
        }

        var what = run.Reach switch
        {
            WatchReach.Unreachable => "it was out of reach, so nothing was sent",
            WatchReach.NoAnswer => "it didn’t answer, so nothing was sent",
            WatchReach.Unreadable => "its answer couldn’t be read, so nothing was sent",
            WatchReach.Newer => "its installer is newer than Flyleaf, so nothing was sent",
            _ => HowItWent(run),
        };
        return $"Watch checked {RelativeTime.Format(run.At, now, zone)}: {what}.";
    }

    private enum Standing
    {
        Failed,
        Waiting,
        Declined,
        Sending,
        Installed,
    }

    private static string HowItWent(WatchRunRecord run)
    {
        var report = run.Report ?? new InstalledReport([], []);
        var standings = run.Due.Select(app => (App: app, Standing: StandingOf(app, run, report))).ToList();
        if (standings.Count == 0)
        {
            return "everything is up to date";
        }

        var clauses = new List<string>();
        foreach (var standing in Enum.GetValues<Standing>())
        {
            var apps = standings.Where(entry => entry.Standing == standing).Select(entry => entry.App).ToList();
            if (apps.Count > 0)
            {
                clauses.Add(Clause(standing, apps, report));
            }
        }
        return Join(clauses);
    }

    private static Standing StandingOf(WatchApp app, WatchRunRecord run, InstalledReport report)
    {
        var result = report.Results.FirstOrDefault(entry =>
            string.Equals(entry.Package, app.Package, StringComparison.Ordinal) && entry.VersionCode == app.VersionCode);
        var has = report.Apps.Any(entry => string.Equals(entry.Package, app.Package, StringComparison.Ordinal) && entry.VersionCode >= app.VersionCode);

        if (has || result?.Outcome is WatchInstallOutcome.Installed or WatchInstallOutcome.InstalledAfterAsking or WatchInstallOutcome.AlreadyCurrent)
        {
            return Standing.Installed;
        }
        if (run.NotSent.Contains(app))
        {
            return Standing.Failed;
        }
        return result?.Outcome switch
        {
            WatchInstallOutcome.Failed or WatchInstallOutcome.Damaged or WatchInstallOutcome.NotForThisWatch or WatchInstallOutcome.NotOurs => Standing.Failed,
            WatchInstallOutcome.Waiting => Standing.Waiting,
            WatchInstallOutcome.Declined => Standing.Declined,
            _ => Standing.Sending,
        };
    }

    private static string Clause(Standing standing, IReadOnlyList<WatchApp> apps, InstalledReport report)
    {
        var one = apps.Count == 1;
        var who = one ? InstallPrompt.NameOf(apps[0].Package) : Quantities.Count(apps.Count, "app");
        return standing switch
        {
            Standing.Failed => $"{who} didn’t install{(one ? Why(apps[0], report) : string.Empty)}",
            Standing.Waiting => one ? $"{who} is waiting for a tap" : $"{who} are waiting for a tap",
            Standing.Declined => one ? $"{who} was turned down" : $"{who} were turned down",
            Standing.Sending => one ? $"{who} is on its way" : $"{who} are on their way",
            _ => $"{who} installed",
        };
    }

    // The platform's word for a failure, as the phone's own line gives a reason: "(conflict)", "(storage)".
    private static string Why(WatchApp app, InstalledReport report) =>
        report.Results.FirstOrDefault(entry =>
                string.Equals(entry.Package, app.Package, StringComparison.Ordinal) && entry.VersionCode == app.VersionCode
                && entry.Outcome == WatchInstallOutcome.Failed)?.Status is { } status
            ? $" ({WatchInstallOutcomes.Word(status)})"
            : string.Empty;

    private static string Join(IReadOnlyList<string> clauses) => clauses.Count switch
    {
        1 => clauses[0],
        _ => $"{string.Join(", ", clauses.Take(clauses.Count - 1))} and {clauses[^1]}",
    };
}

/// <summary>Flyleaf's log lines for the watch half: outcomes, packages and numbers only.</summary>
public static class WatchSendLines
{
    public static string Run(WatchReach reach) => $"WATCH run: {reach}";

    public static string Decision(WatchSendDecision decision) =>
        $"WATCH {decision.Apk.Candidate.Package} {decision.Apk.Candidate.VersionCode}: {decision.Verdict}";

    /// <param name="failure">The exception's type name; never its message.</param>
    public static string NotSent(string package, long versionCode, string failure) => $"WATCH {package} {versionCode}: not sent, {failure}";

    /// <summary>A message heard on <c>update/installed</c>: its version and whether it read, never its body.</summary>
    public static string Heard(ContractRead<InstalledMessage> read) => read.Refusal switch
    {
        ContractRefusal.None => $"CONTRACT {UpdateLines.InstalledContract}: received v{read.Version}",
        ContractRefusal.Newer => $"CONTRACT {UpdateLines.InstalledContract}: refused v{read.Version}, newer than Flyleaf reads",
        _ => $"CONTRACT {UpdateLines.InstalledContract}: refused, unreadable",
    };

    public static string LeftForNextRun(string package, long versionCode) => $"WATCH {package} {versionCode}: left for the next run";
}
