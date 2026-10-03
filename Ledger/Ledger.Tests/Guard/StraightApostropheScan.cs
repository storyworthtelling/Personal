using System.Text.RegularExpressions;
using Index.Tests.Design;

namespace Index.Tests.Guard;

/// <summary>
/// The one scan behind both apostrophe guards (<c>AccountsCosmeticSourceTests</c> and
/// <c>PlanCosmeticsSourceTests</c>): every non-test source in Ledger.Domain, Ledger.Infrastructure and
/// Ledger.Android is read, so a new screen's copy is covered the day it is written rather than the day somebody
/// remembers to add its path to a list.
///
/// <para>What counts: a straight apostrophe between a letter, digit, closing brace or quote and a letter, which is
/// a contraction ("can't") or a possessive, including one that follows an interpolation ("{name}'s") or a quoted
/// name. Comments are stripped first.</para>
///
/// <para>Three exemptions, each deliberate. <b>Log lines</b> are read by the developer, not the owner (a call to
/// <c>logger.Log…</c> or <c>.LogWarning(</c> and friends, on the line itself or on the line before it when that
/// call is still open, i.e. wrapped onto this one). <b>PDF page text</b> in <c>Ledger.Infrastructure/Worksheet/</c> intentionally keeps the
/// mockup's straight apostrophes, because the printed worksheet and follow-up page are drawn that way.
/// <b>Model instructions</b> (the same folder's read-back prompts, and <c>VoiceSeatPrompts</c>) are text a model
/// reads, never the owner. Everything else a person can read must use U+2019.</para>
/// </summary>
internal static class StraightApostropheScan
{
    private static readonly string[] Projects = ["Ledger.Domain", "Ledger.Infrastructure", "Ledger.Android"];

    private static readonly string[] ExemptPrefixes =
    [
        "Ledger.Infrastructure/Worksheet/",
        "Ledger.Domain/Voice/VoiceSeatPrompts.cs",
    ];

    private static readonly Regex LogCall = new(@"\.Log(Trace|Debug|Information|Warning|Error|Critical)?\(", RegexOptions.Compiled);

    private static readonly Regex Straight = new(@"[\p{L}\p{N}}""]'\p{L}", RegexOptions.Compiled);

    public static IReadOnlyList<string> Offenders()
    {
        var root = DesignTestHelpers.LedgerRoot();
        var offenders = new List<string>();

        foreach (var project in Projects)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, project), "*.*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!(relative.EndsWith(".cs", StringComparison.Ordinal) || relative.EndsWith(".xaml", StringComparison.Ordinal))
                    || relative.Contains("/obj/", StringComparison.Ordinal)
                    || relative.Contains("/bin/", StringComparison.Ordinal)
                    || ExemptPrefixes.Any(prefix => relative.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    continue;
                }

                var lines = StripComments(File.ReadAllText(file)).Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var previous = i > 0 ? lines[i - 1] : string.Empty;
                    if (LogCall.IsMatch(lines[i]) || (LogCall.IsMatch(previous) && !previous.TrimEnd().EndsWith(';')))
                    {
                        continue;
                    }

                    if (Straight.IsMatch(lines[i]))
                    {
                        offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        return offenders;
    }

    private static string StripComments(string source)
    {
        // Keep line breaks inside block comments so reported line numbers stay true.
        var withoutBlocks = Regex.Replace(source, @"/\*[\s\S]*?\*/|<!--[\s\S]*?-->", m => new string('\n', m.Value.Count(c => c == '\n')));
        return Regex.Replace(withoutBlocks, @"(?<!:)//.*$", string.Empty, RegexOptions.Multiline);
    }
}
