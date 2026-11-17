using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// Audit W11 (report 12), the owner's rulings: fixed copy is American English in sentence case, contracted
/// ("couldn’t"), with the typographer’s apostrophe, one word per concept (DESIGN.md §15), no codename, no "(s)" and no
/// three dots for an ellipsis. Read from every string literal a screen, notice or printed page can show: heads, kits
/// and domains, not tests or tools, and not a log line, an exception's message, a model's prompt or a parser's
/// pattern, which say what they need to. A literal that is a wire value or an id says so: <c>// not copy: why</c>.
/// </summary>
public partial class FixedWordingTests
{
    // The words that read British (report 12 §1), in a literal.
    private static readonly (string Kind, Regex Pattern)[] Rules =
    [
        ("British spelling (cancel, catalog, recognize, favorite, color)", new(@"\b(?:[Cc]ancell(?:ed|ing)|[Cc]atalogue|[Rr]ecognis(?:e|ed|es|ing)|[Ff]avourite|[Cc]olour)", RegexOptions.CultureInvariant)),
        ("British idiom (holiday, tick, come round, log into)", new(@"\b(?:[Hh]oliday|[Tt]ick(?:ed)?\b(?! ?tock)|comes? round|[Ll]og into)", RegexOptions.CultureInvariant)),
        ("the codename (FocusPhone)", new(@"FocusPhone", RegexOptions.CultureInvariant)),
        ("one word per concept (Vault, not Secrets vault)", new(@"[Ss]ecrets vault", RegexOptions.CultureInvariant)),
        ("a plural written (s)", new(@"[a-z]\(s\)", RegexOptions.CultureInvariant)),
        ("three dots for an ellipsis", new(@"\.\.\.", RegexOptions.CultureInvariant)),
        ("glyphs in copy (★ ✓)", new(@"[★✓⚠]", RegexOptions.CultureInvariant)),
        ("forced capitals (three words in capitals)", new(@"\b[A-Z]{2,}[’']?[A-Z]*(?:[ ·]+[A-Z]{2,}[’']?[A-Z]*){2,}\b", RegexOptions.CultureInvariant)),
        ("please (copy asks plainly)", new(@"\b[Pp]lease\b", RegexOptions.CultureInvariant)),
        ("a straight apostrophe inside a word", new(@"[A-Za-z]'[a-z]", RegexOptions.CultureInvariant)),
        ("spelled out where the copy contracts (couldn’t)", new(@"\b(?:[Cc]ould|[Ww]ould|[Dd]oes|[Dd]id|[Ii]s|[Aa]re|[Ww]as|[Ww]ere|[Hh]as|[Hh]ave) not\b|\b[Cc]annot\b", RegexOptions.CultureInvariant)),
    ];

    [Fact]
    public void FixedCopyFollowsTheGlossaryAndTheHouseStyle()
    {
        var found = new List<string>();
        foreach (var (relative, number, literal) in CopyLiterals())
        {
            foreach (var (kind, pattern) in Rules)
            {
                if (pattern.IsMatch(literal))
                {
                    found.Add($"{relative}:{number}: {kind}");
                }
            }
        }

        Assert.True(found.Count == 0, "Fixed copy that breaks the house style (DESIGN.md §14, §15):\n" + string.Join("\n", found));
    }

    [Fact]
    public void TheScanReadsTheScreens()
    {
        var files = CopyLiterals().Select(hit => hit.File).Distinct().ToList();
        Assert.Contains(files, file => file.StartsWith("FocusPhone/Post.Android/", StringComparison.Ordinal));
        Assert.Contains(files, file => file.StartsWith("Pulp/Pulp.Domain/", StringComparison.Ordinal));
        Assert.Contains(files, file => file.StartsWith("Ledger/Ledger.Android/", StringComparison.Ordinal) && file.EndsWith(".xaml", StringComparison.Ordinal));
    }

    /// <summary>The string literals of the copy files, line by line, with the lines and files that are not copy left out.</summary>
    internal static IEnumerable<(string File, int Line, string Literal)> CopyLiterals()
    {
        var root = GuardScan.RepoRoot();
        foreach (var file in GuardScan.Files(root))
        {
            var extension = Path.GetExtension(file);
            if (extension is not (".cs" or ".xaml"))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!IsCopyFile(relative))
            {
                continue;
            }

            var number = 0;
            var open = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                var trimmed = line.TrimStart();
                if (open > 0)
                {
                    // Still inside a log or exception call that began on an earlier line.
                    open = Math.Max(0, open + Depth(line));
                    continue;
                }

                if (NotCopy().IsMatch(line))
                {
                    open = Math.Max(0, Depth(line));
                    continue;
                }

                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*')
                    || line.Contains("// not copy", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match literal in Literal().Matches(line))
                {
                    yield return (relative, number, literal.Value);
                }
            }
        }
    }

    // How many more brackets a line opens than it closes, its literals left out.
    private static int Depth(string line)
    {
        var bare = Literal().Replace(line, "\"\"");
        return bare.Count(c => c == '(') - bare.Count(c => c == ')');
    }

    private static bool IsCopyFile(string relative) =>
        !relative.StartsWith("Tools/", StringComparison.Ordinal)
        && !relative.StartsWith("audits/", StringComparison.Ordinal)
        && !relative.Contains("Tests/", StringComparison.Ordinal)
        && !relative.StartsWith("Storyworthtelling.Diagnostics/", StringComparison.Ordinal)
        && !NotCopyFile().IsMatch(relative);

    // A literal: "…" with escapes, on one line.
    [GeneratedRegex(@"""(?:[^""\\\n]|\\.)*""")]
    private static partial Regex Literal();

    // A line whose words are for a log, an exception, a pattern or a query rather than a person.
    [GeneratedRegex(@"\bthrow\b|Exception\(|Log[A-Z]\w*\(|\.Log\(|Warn\(|Info\(|Trail|Debug\(|Error\(|Fail\(|Wrong\(|Failed\(|Regex|@""|""""""|\$@|@\$|CommandText|json_extract|\bSELECT\b|\bWHERE\b|ApplicationName|""@style/|nameof\(|\[JsonPropertyName|PreferenceKeys|Name\s*=\s*""com\.|Package")]
    private static partial Regex NotCopy();

    // Files whose literals are a model's prompt, a parser's or a classifier's patterns, a codec's wire text or a
    // generated resource, not copy.
    [GeneratedRegex(@"Prompt|Instruction|CallSheet\.cs|Groundedness|Client\.cs|Codec|Parser|Reader\.cs|Rules\.cs|Mapper|Normalizer|NumberWords|Detector|Classifier|Persona|Resource\.designer|/Resources/|Trail\.cs|LogLines|/Storage/")]
    private static partial Regex NotCopyFile();
}
