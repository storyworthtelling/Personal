using System.Formats.Tar;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogLoopReader;

/// <summary>Where a leak was found — never the token itself.</summary>
public sealed record LeakHit(string Where, int Line, int Length)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Where} line {Line}: a {Length}-character token from this week's logs");
}

/// <summary>
/// The session's leak scan (LOG-LOOP-PLAN.md §6.1, layer 4). Every distinct token of four or more
/// characters in the week's plaintext — less those the repository already contained before this change —
/// is kept as a salted hash; a diff, PR title or body is scanned against it and any hit blocks the push.
/// Case-insensitive, so a capitalised copy still hits. The hit says where, never what.
/// </summary>
public sealed partial class LeakScan
{
    private const int MinLength = 4;
    private readonly byte[] salt;
    private readonly HashSet<string> hashes;

    private LeakScan(byte[] salt, HashSet<string> hashes)
    {
        this.salt = salt;
        this.hashes = hashes;
    }

    public int Count => hashes.Count;

    public static LeakScan Build(IEnumerable<string> plaintexts, IEnumerable<string> repoTexts)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in repoTexts)
        {
            known.UnionWith(Tokens(text));
        }
        var salt = RandomNumberGenerator.GetBytes(16);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in plaintexts)
        {
            foreach (var token in Tokens(text))
            {
                if (!known.Contains(token))
                {
                    hashes.Add(Hash(salt, token));
                }
            }
        }
        return new LeakScan(salt, hashes);
    }

    public IReadOnlyList<LeakHit> Scan(string text, string where)
    {
        var hits = new List<LeakHit>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var token in Tokens(lines[i]))
            {
                if (hashes.Contains(Hash(salt, token)))
                {
                    hits.Add(new LeakHit(where, i + 1, token.Length));
                }
            }
        }
        return hits;
    }

    /// <summary>The set as it may be written to the session's temp folder: a salt and hashes, no tokens.</summary>
    public string DescribeForDisk() =>
        JsonSerializer.Serialize(new { salt = Convert.ToBase64String(salt), hashes = hashes.Order(StringComparer.Ordinal) });

    /// <summary>The text of every file at a git ref — the repository as it was before the change under scan.</summary>
    public static IEnumerable<string> RepoTextsAt(string repoRoot, string gitRef)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git", ["-C", repoRoot, "archive", "--format=tar", gitRef])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var git = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Could not start git to read the base of the change.");
        using var buffer = new MemoryStream();
        git.StandardOutput.BaseStream.CopyTo(buffer);
        git.WaitForExit();
        if (git.ExitCode != 0)
        {
            throw new InvalidOperationException($"git archive {gitRef} failed with exit code {git.ExitCode}.");
        }
        buffer.Position = 0;
        using var tar = new TarReader(buffer);
        var texts = new List<string>();
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && entry.DataStream is not null && entry.Length < 4 * 1024 * 1024)
            {
                using var reader = new StreamReader(entry.DataStream, Encoding.UTF8);
                texts.Add(reader.ReadToEnd());
            }
        }
        return texts;
    }

    internal static IEnumerable<string> Tokens(string text) =>
        Token().Matches(text).Select(m => m.Value.ToLowerInvariant()).Where(t => t.Length >= MinLength);

    private static string Hash(byte[] salt, string token) =>
        Convert.ToHexString(HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(token)));

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Token();
}

/// <summary>One finding of the content-free CI scan.</summary>
public sealed record CiHit(string Path, int Line, string Rule)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Path}:{Line}: {Rule}");
}

/// <summary>
/// The content-free subset CI runs on every claude/** push (§6.1): it holds no plaintext, so it looks
/// only for shapes — a Luhn-valid card-like number, an address outside example.com, a phone number that
/// is not a 555 number, an OAuth-code- or token-shaped string, and (on a claude/logfix-* branch, where a
/// figure in a test is most likely copied from a log) a dollar amount in a test.
/// </summary>
public static partial class CiLeakScan
{
    private static readonly string[] AllowedEmailDomains =
        ["example.com", "example.org", "example.net", "anthropic.com", "users.noreply.github.com"];

    public static IReadOnlyList<CiHit> Scan(string path, IEnumerable<(int Line, string Text)> addedLines, bool logfixBranch)
    {
        var hits = new List<CiHit>();
        var isTest = IsTestPath(path);
        foreach (var (line, text) in addedLines)
        {
            if (Card().Matches(text).Any(m => Luhn(m.Value)))
            {
                hits.Add(new CiHit(path, line, "card-number"));
            }
            if (Email().Matches(text).Any(m => !AllowedEmail(m.Groups["domain"].Value)))
            {
                hits.Add(new CiHit(path, line, "email"));
            }
            var phones = Phone().Matches(text);
            var shortOutside = ShortPhone().Matches(text)
                .Where(m => !phones.Any(p => m.Index >= p.Index && m.Index < p.Index + p.Length));
            if (phones.Any(m => !Fictional(m.Value)) || shortOutside.Any(m => !m.Value.StartsWith("555", StringComparison.Ordinal)))
            {
                hits.Add(new CiHit(path, line, "phone"));
            }
            if (OauthShape().IsMatch(text))
            {
                hits.Add(new CiHit(path, line, "oauth-code"));
            }
            if (logfixBranch && isTest && DollarAmount().IsMatch(text))
            {
                hits.Add(new CiHit(path, line, "amount-in-test"));
            }
        }
        return hits;
    }

    public static bool IsTestPath(string path) =>
        path.Replace('\\', '/').Split('/').Any(s => s.EndsWith(".Tests", StringComparison.Ordinal) || s == "Tests")
        || path.EndsWith("Tests.cs", StringComparison.Ordinal);

    private static bool AllowedEmail(string domain) =>
        AllowedEmailDomains.Any(a => domain.Equals(a, StringComparison.OrdinalIgnoreCase)
            || domain.EndsWith("." + a, StringComparison.OrdinalIgnoreCase));

    private static bool Fictional(string phone) =>
        Regex.Matches(phone, @"\d{3,4}").Any(m => m.Value == "555");

    private static bool Luhn(string candidate)
    {
        var digits = candidate.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19)
        {
            return false;
        }
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i];
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }
            sum += d;
        }
        return sum % 10 == 0;
    }

    [GeneratedRegex(@"\b(?:\d[ -]?){12,18}\d\b")]
    private static partial Regex Card();

    [GeneratedRegex(@"[\w.+-]+@(?<domain>[\w-]+(\.[\w-]+)+)")]
    private static partial Regex Email();

    [GeneratedRegex(@"\(?\b\d{3}\)?[-. ]\d{3}[-. ]\d{4}\b")]
    private static partial Regex Phone();

    [GeneratedRegex(@"(?<![\d-])\b\d{3}-\d{4}\b(?!-)")]
    private static partial Regex ShortPhone();

    [GeneratedRegex(@"4/0[A-Za-z0-9_-]{20,}|1//0[A-Za-z0-9_-]{20,}|ya29\.[A-Za-z0-9_-]{20,}|\b(public|access|link)-(sandbox|development|production)-[0-9a-f]{8}-[0-9a-f-]{27}|\b(code|state)=(?!<)[A-Za-z0-9_\-/]{16,}")]
    private static partial Regex OauthShape();

    [GeneratedRegex(@"\$\d[\d,]*(\.\d{2})?")]
    private static partial Regex DollarAmount();
}

/// <summary>The added lines of a unified diff, with their new line numbers.</summary>
public static partial class DiffLines
{
    public static IEnumerable<(string Path, int Line, string Text)> Added(string unifiedDiff)
    {
        string? path = null;
        var line = 0;
        foreach (var raw in unifiedDiff.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.StartsWith("+++ ", StringComparison.Ordinal))
            {
                var target = raw[4..].Trim();
                path = target == "/dev/null" ? null : target.StartsWith("b/", StringComparison.Ordinal) ? target[2..] : target;
                continue;
            }
            if (raw.StartsWith("--- ", StringComparison.Ordinal) || raw.StartsWith("diff ", StringComparison.Ordinal))
            {
                continue;
            }
            var hunk = Hunk().Match(raw);
            if (hunk.Success)
            {
                line = int.Parse(hunk.Groups["start"].Value, CultureInfo.InvariantCulture);
                continue;
            }
            if (path is null)
            {
                continue;
            }
            if (raw.StartsWith('+'))
            {
                yield return (path, line, raw[1..]);
                line++;
            }
            else if (raw.StartsWith(' '))
            {
                line++;
            }
        }
    }

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(?<start>\d+)(?:,\d+)? @@")]
    private static partial Regex Hunk();
}
