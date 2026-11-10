using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Storyworthtelling.Diagnostics;

namespace LogLoopReader;

/// <summary>One fingerprint's occurrences across the week's batches.</summary>
public sealed record DigestGroup(
    string Fingerprint,
    string App,
    string Kind,
    string Key,
    int Count,
    IReadOnlyList<string> Phones,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    IReadOnlyList<string> Levels,
    string Sample,
    IReadOnlyList<long>? Versions = null,
    int Severity = Digest.Routine);

/// <summary>
/// Groups the week's lines by fingerprint (LOG-LOOP-PLAN.md §5.2, §7.2) so the session reads a summary
/// before any raw line. The key is built from shapes, never content: an exception's type and its top
/// three own frames; a tap verdict's screen, control and verdict; a sign-in's flow kind and diagnosis;
/// anything else, its level, source and message template. The fingerprint is the SHA-256 of app + kind +
/// key, which is what the PR's log-loop block and the commit trailers carry.
/// </summary>
public static partial class Digest
{
    /// <summary>The severity rank of a group of INFO lines: last, and folded into one line each.</summary>
    public const int Routine = 7;

    private static readonly string[] SeverityNames =
        ["Crashes", "Exceptions", "Exits", "Taps", "Sign-ins", "Worker failures", "Warnings", "Routine"];

    private static readonly string[] OwnNamespaces =
    [
        "Post.", "Flyleaf.", "Score.", "Plate.", "Pulp.", "Constitution.", "Index.", "Folio.", "Foreword.",
        "Codex.", "Storyworthtelling.", "Ledger.",
    ];

    public static IReadOnlyList<DigestGroup> Build(IEnumerable<LogBatch> batches)
    {
        var groups = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        foreach (var batch in batches)
        {
            foreach (var part in batch.Parts)
            {
                foreach (var entry in LogEntries.Parse(part.Text))
                {
                    var (kind, key) = KeyOf(entry);
                    var fingerprint = Fingerprint(batch.Header.App, kind, key);
                    if (!groups.TryGetValue(fingerprint, out var group))
                    {
                        group = new Accumulator(fingerprint, batch.Header.App, kind, key, Sample(entry));
                        groups.Add(fingerprint, group);
                    }
                    group.Add(batch.Header, entry, Rank(kind, key, entry.Level));
                }
            }
        }
        return groups.Values
            .Select(g => g.ToGroup())
            .OrderBy(g => g.Severity)
            .ThenByDescending(g => g.Count)
            .ThenBy(g => g.App, StringComparer.Ordinal)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
    }

    public static string Fingerprint(string app, string kind, string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{app}\n{kind}\n{key}"))).ToLowerInvariant();

    /// <summary>
    /// The digest as the session reads it (audit 08 §4, §34): what the loop itself lost first — dropped segments, apps that
    /// sent nothing, workers that did not run — then each app's groups by severity, a crash before any count of routine
    /// lines, which are folded to one line each. Every sample goes through the sanitizer.
    /// </summary>
    public static string Render(IReadOnlyList<DigestGroup> groups, LogExcerptSanitizer sanitizer, LoopHealth? health = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Log loop digest").AppendLine();
        if (health is not null)
        {
            RenderHealth(builder, health);
        }
        foreach (var app in groups.GroupBy(g => g.App).OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            builder.Append("## ").AppendLine(app.Key).AppendLine();
            foreach (var section in app.GroupBy(g => g.Severity).OrderBy(s => s.Key))
            {
                builder.Append("### ").AppendLine(SeverityNames[Math.Clamp(section.Key, 0, Routine)]).AppendLine();
                foreach (var group in section.OrderByDescending(g => g.Count))
                {
                    if (section.Key == Routine)
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"- {group.Count}× {sanitizer.Sanitize(group.Key)} · builds {Builds(group)}").AppendLine();
                        continue;
                    }
                    builder.Append(CultureInfo.InvariantCulture, $"- **{group.Count}×** {group.Kind} · {string.Join('/', group.Levels)} · phones {string.Join('/', group.Phones)}")
                        .Append(CultureInfo.InvariantCulture, $" · {group.FirstSeen:yyyy-MM-dd HH:mm} → {group.LastSeen:yyyy-MM-dd HH:mm} · builds {Builds(group)}").AppendLine()
                        .Append("  - fingerprint `").Append(group.Fingerprint).AppendLine("`")
                        .Append("  - ").AppendLine(sanitizer.Sanitize(group.Sample).Replace("\n", "\n    ", StringComparison.Ordinal));
                }
                builder.AppendLine();
            }
        }
        return builder.ToString();
    }

    private static string Builds(DigestGroup group) =>
        group.Versions is { Count: > 0 } versions ? string.Join(", ", versions) : "unknown";

    private static void RenderHealth(StringBuilder builder, LoopHealth health)
    {
        if (health.Dropped.Count > 0)
        {
            builder.AppendLine("## DROPPED — segments lost on the phone before upload").AppendLine();
            foreach (var dropped in health.Dropped)
            {
                builder.Append(CultureInfo.InvariantCulture, $"- {dropped.App} ({dropped.Phone}): {dropped.Line}").AppendLine();
            }
            builder.AppendLine();
        }
        if (health.Silent.Count > 0)
        {
            builder.AppendLine("## Sent nothing this week").AppendLine();
            foreach (var (app, phone) in health.Silent)
            {
                builder.Append(CultureInfo.InvariantCulture, $"- {app} ({phone})").AppendLine();
            }
            builder.AppendLine();
        }
        if (health.Unfinished.Count > 0)
        {
            builder.AppendLine("## Runs that started and never ended").AppendLine();
            foreach (var run in health.Unfinished)
            {
                builder.Append(CultureInfo.InvariantCulture, $"- {run.App} ({run.Phone}) {run.Worker}: {run.Count}").AppendLine();
            }
            builder.AppendLine();
        }
        if (health.MissedWorkers.Count > 0)
        {
            builder.AppendLine("## Workers that did not run").AppendLine();
            foreach (var gap in health.MissedWorkers)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"- {gap.App} ({gap.Phone}) {gap.Worker}: about {gap.MissedRuns} missed run(s), {gap.From:yyyy-MM-dd HH:mm} → {gap.To:yyyy-MM-dd HH:mm}").AppendLine();
            }
            builder.AppendLine();
        }
    }

    // CRASH / exception / Exit-problem / Tap / SignIn / Worker failure / WARN / INFO (audit 08 §4).
    internal static int Rank(string kind, string key, string level)
    {
        if (level is "CRASH" or "CRIT")
        {
            return 0;
        }
        return kind switch
        {
            "exception" => 1,
            "tap" => 3,
            "sign-in" => 4,
            _ when level is "INFO" or "TEXT" or "DEBUG" => Routine,
            _ when key.StartsWith(level + " Exit ", StringComparison.Ordinal) => 2,
            _ when key.StartsWith(level + " Worker ", StringComparison.Ordinal) => 5,
            _ => 6,
        };
    }

    internal static (string Kind, string Key) KeyOf(LogEntry entry)
    {
        if (entry.Source == "Tap")
        {
            var head = entry.Message.Split('\n')[0];
            return ("tap", RepeatCount().Replace(head, "REPEATED TAP"));
        }
        if (entry.Source == "SignIn")
        {
            var all = entry.Message + "\n" + entry.Detail;
            var kind = SignInHead().Match(all) is { Success: true } m ? m.Groups["kind"].Value : "unknown";
            var why = all.Split('\n').FirstOrDefault(l => l.StartsWith("Why: ", StringComparison.Ordinal))?["Why: ".Length..] ?? "no diagnosis";
            return ("sign-in", $"{kind} · {Template(why)}");
        }
        var stack = entry.Detail.Length > 0 ? entry.Detail : entry.Message;
        if (stack.Contains("\n   at ", StringComparison.Ordinal) || stack.TrimStart().StartsWith("at ", StringComparison.Ordinal))
        {
            var lines = stack.Split('\n');
            var first = lines[0].Trim();
            // An exception interpolated into the message ("… threw: {e}") leaves the detail opening on a frame; its type
            // is then the last exception-shaped name in the message (audit 08 §35).
            var type = first.StartsWith("at ", StringComparison.Ordinal)
                ? TypeName().Matches(entry.Message) is { Count: > 0 } named ? named[^1].Value : "unknown"
                : first.Split(':', 2)[0].Trim();
            var all = lines
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("at ", StringComparison.Ordinal))
                .Select(l => Frame().Match(l[3..]).Groups["method"].Value)
                .ToList();
            var own = all.Where(f => OwnNamespaces.Any(n => f.StartsWith(n, StringComparison.Ordinal))).Take(3).ToList();
            // A stack with none of the suite's own frames (a Java-only fault) keys on its top frames of any kind, so two
            // different platform faults of one type stay two groups.
            var frames = own.Count > 0 ? own : all.Take(3).ToList();
            return ("exception", $"{type} @ {string.Join(" < ", frames)}");
        }
        return ("message", $"{entry.Level} {entry.Source} {Template(entry.Message.Split('\n')[0])}");
    }

    /// <summary>A message with its variable parts reduced to placeholders, so one cause is one group.</summary>
    internal static string Template(string message)
    {
        var text = Email().Replace(message, "<email>");
        text = Url().Replace(text, "<url>");
        text = Quoted().Replace(text, "\"…\"");
        text = Hex().Replace(text, "<hex>");
        text = Digits().Replace(text, "<n>");
        return text.Trim();
    }

    private static string Sample(LogEntry entry) =>
        entry.Detail.Length == 0 ? $"{entry.Level} {entry.Source} — {entry.Message}" : $"{entry.Level} {entry.Source} — {entry.Message}\n{FirstLines(entry.Detail, 4)}";

    private static string FirstLines(string text, int count) => string.Join('\n', text.Split('\n').Take(count));

    private sealed class Accumulator(string fingerprint, string app, string kind, string key, string sample)
    {
        private readonly SortedSet<string> phones = new(StringComparer.Ordinal);
        private readonly SortedSet<string> levels = new(StringComparer.Ordinal);
        private readonly SortedSet<long> versions = [];
        private int count;
        private int severity = Routine;
        private DateTimeOffset first = DateTimeOffset.MaxValue;
        private DateTimeOffset last = DateTimeOffset.MinValue;

        public void Add(LogBatchHeader header, LogEntry entry, int rank)
        {
            count++;
            phones.Add(header.Phone);
            levels.Add(entry.Level);
            versions.Add(header.VersionCode);
            severity = Math.Min(severity, rank);
            // The entry's own time when its stamp reads; the batch's range otherwise (audit 08 §3).
            var when = EntryTime.Read(entry.Stamp, header);
            var from = when ?? header.FirstUtc;
            var to = when ?? header.LastUtc;
            if (from < first)
            {
                first = from;
            }
            if (to > last)
            {
                last = to;
            }
        }

        public DigestGroup ToGroup() =>
            new(fingerprint, app, kind, key, count, phones.ToList(), first, last, levels.ToList(), sample, versions.ToList(), severity);
    }

    [GeneratedRegex(@"REPEATED TAP ×\d+")]
    private static partial Regex RepeatCount();

    [GeneratedRegex(@"SIGN-IN (?<kind>\S+)")]
    private static partial Regex SignInHead();

    [GeneratedRegex(@"\b(?:[A-Za-z_][\w]*\.)*[A-Za-z_]\w*(?:Exception|Error)\b")]
    private static partial Regex TypeName();

    [GeneratedRegex(@"^(?<method>[^(\s]+)")]
    private static partial Regex Frame();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+")]
    private static partial Regex Email();

    [GeneratedRegex(@"[a-zA-Z][a-zA-Z0-9+.-]*://\S+")]
    private static partial Regex Url();

    [GeneratedRegex("\"[^\"]*\"")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\b[0-9a-f]{8,}\b")]
    private static partial Regex Hex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
