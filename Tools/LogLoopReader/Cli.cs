using System.Text.Json;
using Storyworthtelling.Diagnostics;

namespace LogLoopReader;

/// <summary>
/// <c>LogLoopReader &lt;command&gt;</c> — see the project file and <c>.claude/log-loop/RUNBOOK.md</c>.
/// Nothing printed here carries a log line: counts, paths, fingerprints and sanitized shapes only.
/// </summary>
public static class Cli
{
    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, TextWriter errors)
    {
        if (args.Length == 0)
        {
            errors.WriteLine("Usage: LogLoopReader fetch|digest|triage-check|backlog|processed|sanitize|leakscan|gate [options]");
            return 2;
        }
        var options = Options(args.Skip(1).ToArray());
        try
        {
            return args[0] switch
            {
                "fetch" => await FetchAsync(options, output),
                "digest" => Digest(options, output),
                "processed" => Processed(options, output),
                "sanitize" => Sanitize(input, output),
                "leakscan" => LeakScanCommand(options, input, output),
                "gate" => Gate(options, input, output),
                "triage-check" => TriageCheck(options, output),
                "backlog" => BacklogCommand(options, output),
                _ => Unknown(args[0], errors),
            };
        }
        catch (Exception e) when (e is OutputInsideRepoException or ArgumentException or InvalidOperationException or IOException)
        {
            errors.WriteLine($"{args[0]}: {e.Message}");
            return 1;
        }
    }

    private static async Task<int> FetchAsync(IReadOnlyDictionary<string, string> options, TextWriter output)
    {
        var key = Environment.GetEnvironmentVariable("SWT_LOG_KEY")
            ?? throw new ArgumentException("SWT_LOG_KEY is not set.");
        var token = Environment.GetEnvironmentVariable("SWT_DRIVE_READ_TOKEN")
            ?? throw new ArgumentException("SWT_DRIVE_READ_TOKEN is not set.");
        var outDir = options.GetValueOrDefault("out") ?? OutputGuard.DefaultOutput();
        var fetched = await new FetchCommand(new DriveLogSource(token), key).RunAsync(outDir, RepoRoot(), CancellationToken.None);
        foreach (var status in fetched.GroupBy(f => f.Status))
        {
            output.WriteLine($"{status.Key}: {status.Count()}");
        }
        output.WriteLine($"plaintext under {Path.Combine(outDir, FetchCommand.PlainFolder)}; manifest at {Path.Combine(outDir, Manifest.FileName)}");
        return 0;
    }

    // Phase 4: names her phone's finished files for it to trash; --previous is last week's list, as downloaded.
    private static int Processed(IReadOnlyDictionary<string, string> options, TextWriter output)
    {
        var dir = options.GetValueOrDefault("in") ?? OutputGuard.DefaultOutput();
        OutputGuard.EnsureOutside(dir, RepoRoot());
        var plain = Path.Combine(dir, FetchCommand.PlainFolder);
        string? PhoneOf(string name)
        {
            var path = Path.Combine(plain, name);
            return File.Exists(path) ? FetchCommand.ReadPlain(path).Header.Phone : null;
        }
        var previous = options.GetValueOrDefault("previous") is { } previousPath && File.Exists(previousPath)
            ? ProcessedList.Previous(File.ReadAllText(previousPath))
            : [];
        var names = ProcessedList.Names(Manifest.Read(dir), PhoneOf, previous);
        var path = Path.Combine(dir, ProcessedList.FileName);
        File.WriteAllText(path, ProcessedList.Json(names));
        output.WriteLine($"{names.Count} of her phone's files listed; {path}");
        return 0;
    }

    private static int Digest(IReadOnlyDictionary<string, string> options, TextWriter output)
    {
        var inDir = options.GetValueOrDefault("in") ?? Path.Combine(OutputGuard.DefaultOutput(), FetchCommand.PlainFolder);
        var outDir = options.GetValueOrDefault("out") ?? OutputGuard.DefaultOutput();
        OutputGuard.EnsureOutside(outDir, RepoRoot());
        var batches = Directory.EnumerateFiles(inDir, "*.json").Order(StringComparer.Ordinal).Select(FetchCommand.ReadPlain).ToList();
        var groups = LogLoopReader.Digest.Build(batches);
        Directory.CreateDirectory(outDir);
        var markdown = Path.Combine(outDir, "digest.md");
        var ledgerPath = options.GetValueOrDefault("ledger") ?? LedgerPath();
        var ledger = File.Exists(ledgerPath) ? TriageLedger.FromJson(File.ReadAllText(ledgerPath)) : TriageLedger.Empty;
        var plan = Triage.Plan(groups, ledger);
        var folded = plan.Where(i => i.Status == TriageStatus.Folded).Select(i => i.Fingerprint).ToHashSet(StringComparer.Ordinal);
        var shown = groups.Where(g => !folded.Contains(g.Fingerprint)).ToList();
        File.WriteAllText(markdown, LogLoopReader.Digest.Render(shown, new LogExcerptSanitizer(LogExcerptSanitizer.VocabularyFrom(RepoRoot())))
            + $"{folded.Count} routine INFO fingerprints folded ({groups.Where(g => folded.Contains(g.Fingerprint)).Sum(g => g.Count)} lines); not listed.{Environment.NewLine}");
        var triageDir = Path.Combine(outDir, TriageFolder);
        if (Directory.Exists(triageDir))
        {
            Directory.Delete(triageDir, recursive: true);
        }
        Directory.CreateDirectory(triageDir);
        foreach (var (app, items) in Triage.ByApp(plan))
        {
            File.WriteAllText(Path.Combine(triageDir, app + ".json"), JsonSerializer.Serialize(items, TriageJson));
        }
        var pending = plan.Count(i => i.Status == TriageStatus.Pending);
        output.WriteLine($"triage: {pending} to decide, {plan.Count(i => i.Status == TriageStatus.Carried)} carried from the ledger, {folded.Count} folded; one checklist per app under {triageDir}");
        File.WriteAllText(Path.Combine(outDir, "digest.json"), JsonSerializer.Serialize(groups, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"{groups.Count} fingerprints from {batches.Count} batches; digest at {markdown}");
        return 0;
    }

    // triage-check [--in DIR] [--ledger PATH]: exits 1 while any pending fingerprint lacks an outcome and class; once
    // complete it writes the merged ledger to PATH (default: the committed one) for the session to commit.
    private static int TriageCheck(IReadOnlyDictionary<string, string> options, TextWriter output)
    {
        var dir = options.GetValueOrDefault("in") ?? OutputGuard.DefaultOutput();
        var items = Directory.EnumerateFiles(Path.Combine(dir, TriageFolder), "*.json")
            .SelectMany(f => JsonSerializer.Deserialize<List<TriageItem>>(File.ReadAllText(f), TriageJson) ?? [])
            .ToList();
        var result = Triage.Check(items);
        var tally = Triage.Counts(items);
        output.WriteLine($"triage-check: fix {tally.Fix}, report {tally.Report}, no-action {tally.NoAction}, untriaged {tally.Untriaged}");
        if (!result.Complete)
        {
            output.WriteLine($"triage-check: {result.Open.Count} of {items.Count(i => i.Status == TriageStatus.Pending)} fingerprints have no outcome and class. Do not report or trash.");
            result.Open.Take(20).ToList().ForEach(f => output.WriteLine($"  open {f}"));
            return 1;
        }
        var ledgerPath = options.GetValueOrDefault("ledger") ?? LedgerPath();
        var ledger = File.Exists(ledgerPath) ? TriageLedger.FromJson(File.ReadAllText(ledgerPath)) : TriageLedger.Empty;
        File.WriteAllText(ledgerPath, Triage.Merge(ledger, items, DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).ToJson());
        output.WriteLine($"triage-check: complete; ledger written to {ledgerPath}");
        return 0;
    }

    private const string TriageFolder = "triage";

    private static readonly JsonSerializerOptions TriageJson = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static string LedgerPath() => Path.Combine(RepoRoot(), ".claude", "log-loop", "triage-ledger.json");

    // backlog [--in DIR]: the files this week did not finish and how long they have waited (names and days only).
    private static int BacklogCommand(IReadOnlyDictionary<string, string> options, TextWriter output)
    {
        var dir = options.GetValueOrDefault("in") ?? OutputGuard.DefaultOutput();
        var plain = Path.Combine(dir, FetchCommand.PlainFolder);
        DateTimeOffset? LastWritten(string name)
        {
            var path = Path.Combine(plain, name);
            return File.Exists(path) ? FetchCommand.ReadPlain(path).Header.LastUtc : null;
        }
        var lines = Backlog.Lines(Manifest.Read(dir), LastWritten, DateTimeOffset.UtcNow);
        output.WriteLine($"backlog: {lines.Count} file(s) not finished");
        lines.ToList().ForEach(output.WriteLine);
        return 0;
    }

    private static int Sanitize(TextReader input, TextWriter output)
    {
        output.Write(new LogExcerptSanitizer(LogExcerptSanitizer.VocabularyFrom(RepoRoot())).Sanitize(input.ReadToEnd()));
        return 0;
    }

    // leakscan --ci --diff FILE --branch NAME           the content-free subset (CI)
    // leakscan --plain DIR --base REF --diff FILE [--title FILE] [--body FILE]   the session's full scan
    private static int LeakScanCommand(IReadOnlyDictionary<string, string> options, TextReader input, TextWriter output)
    {
        var diff = options.TryGetValue("diff", out var diffPath) ? File.ReadAllText(diffPath) : input.ReadToEnd();
        if (options.ContainsKey("ci"))
        {
            var branch = options.GetValueOrDefault("branch") ?? string.Empty;
            var logfix = branch.StartsWith("claude/logfix-", StringComparison.Ordinal);
            var hits = DiffLines.Added(diff)
                .GroupBy(a => a.Path)
                .SelectMany(file => CiLeakScan.Scan(file.Key, file.Select(a => (a.Line, a.Text)), logfix))
                .ToList();
            hits.ForEach(h => output.WriteLine(h));
            output.WriteLine(hits.Count == 0 ? "log-loop-guard: no personal-data shapes in the added lines." : $"log-loop-guard: {hits.Count} finding(s).");
            return hits.Count == 0 ? 0 : 1;
        }

        var plainDir = options.GetValueOrDefault("plain") ?? Path.Combine(OutputGuard.DefaultOutput(), FetchCommand.PlainFolder);
        var baseRef = options.GetValueOrDefault("base") ?? "origin/main";
        var plaintexts = Directory.EnumerateFiles(plainDir, "*.json")
            .Select(FetchCommand.ReadPlain)
            .SelectMany(b => b.Parts.Select(p => p.Text));
        var scan = LeakScan.Build(plaintexts, LeakScan.RepoTextsAt(RepoRoot(), baseRef));
        var found = new List<LeakHit>();
        foreach (var (path, line, text) in DiffLines.Added(diff))
        {
            found.AddRange(scan.Scan(text, path).Select(h => h with { Line = line }));
        }
        foreach (var part in new[] { "title", "body" })
        {
            if (options.TryGetValue(part, out var file))
            {
                found.AddRange(scan.Scan(File.ReadAllText(file), part));
            }
        }
        found.ForEach(h => output.WriteLine(h));
        output.WriteLine(found.Count == 0 ? "leakscan: nothing from this week's logs." : $"leakscan: {found.Count} hit(s); do not push.");
        return found.Count == 0 ? 0 : 1;
    }

    private static int Gate(IReadOnlyDictionary<string, string> options, TextReader input, TextWriter output)
    {
        var json = options.TryGetValue("input", out var path) ? File.ReadAllText(path) : input.ReadToEnd();
        var decision = AutoMergeGate.Decide(GateInput.FromJson(json));
        output.WriteLine(JsonSerializer.Serialize(new { enable = decision.Enable, reasons = decision.Reasons }));
        return 0;
    }

    private static int Unknown(string command, TextWriter errors)
    {
        errors.WriteLine($"Unknown command {command}.");
        return 2;
    }

    private static string RepoRoot() =>
        OutputGuard.FindRepoRoot(Environment.CurrentDirectory)
        ?? OutputGuard.FindRepoRoot(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("Run LogLoopReader from inside the repository, so it knows where plaintext must not go.");

    private static Dictionary<string, string> Options(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument {args[i]}.");
            }
            var name = args[i][2..];
            options[name] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
        }
        return options;
    }
}
