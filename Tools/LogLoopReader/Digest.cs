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
    string Sample);

/// <summary>
/// Groups the week's lines by fingerprint (LOG-LOOP-PLAN.md §5.2, §7.2) so the session reads a summary
/// before any raw line. The key is built from shapes, never content: an exception's type and its top
/// three own frames; a tap verdict's screen, control and verdict; a sign-in's flow kind and diagnosis;
/// anything else, its level, source and message template. The fingerprint is the SHA-256 of app + kind +
/// key, which is what the PR's log-loop block and the commit trailers carry.
/// </summary>
public static partial class Digest
{
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
                    group.Add(batch.Header, entry.Level);
                }
            }
        }
        return groups.Values
            .Select(g => g.ToGroup())
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.App, StringComparer.Ordinal)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
    }

    public static string Fingerprint(string app, string kind, string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{app}\n{kind}\n{key}"))).ToLowerInvariant();

    /// <summary>The digest as the session reads it: biggest first, every sample through the sanitizer.</summary>
    public static string Render(IReadOnlyList<DigestGroup> groups, LogExcerptSanitizer sanitizer)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Log loop digest").AppendLine();
        foreach (var app in groups.GroupBy(g => g.App).OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            builder.Append("## ").AppendLine(app.Key).AppendLine();
            foreach (var group in app)
            {
                builder.Append(CultureInfo.InvariantCulture, $"- **{group.Count}×** {group.Kind} · {string.Join('/', group.Levels)} · phones {string.Join('/', group.Phones)}")
                    .Append(CultureInfo.InvariantCulture, $" · {group.FirstSeen:yyyy-MM-dd} → {group.LastSeen:yyyy-MM-dd}").AppendLine()
                    .Append("  - fingerprint `").Append(group.Fingerprint).AppendLine("`")
                    .Append("  - ").AppendLine(sanitizer.Sanitize(group.Sample).Replace("\n", "\n    ", StringComparison.Ordinal));
            }
            builder.AppendLine();
        }
        return builder.ToString();
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
            var type = lines[0].Split(':', 2)[0].Trim();
            var frames = lines
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("at ", StringComparison.Ordinal))
                .Select(l => Frame().Match(l[3..]).Groups["method"].Value)
                .Where(f => OwnNamespaces.Any(n => f.StartsWith(n, StringComparison.Ordinal)))
                .Take(3);
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
        private int count;
        private DateTimeOffset first = DateTimeOffset.MaxValue;
        private DateTimeOffset last = DateTimeOffset.MinValue;

        public void Add(LogBatchHeader header, string level)
        {
            count++;
            phones.Add(header.Phone);
            levels.Add(level);
            if (header.FirstUtc < first)
            {
                first = header.FirstUtc;
            }
            if (header.LastUtc > last)
            {
                last = header.LastUtc;
            }
        }

        public DigestGroup ToGroup() => new(fingerprint, app, kind, key, count, phones.ToList(), first, last, levels.ToList(), sample);
    }

    [GeneratedRegex(@"REPEATED TAP ×\d+")]
    private static partial Regex RepeatCount();

    [GeneratedRegex(@"SIGN-IN (?<kind>\S+)")]
    private static partial Regex SignInHead();

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
