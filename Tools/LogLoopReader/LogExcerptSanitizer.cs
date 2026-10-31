using System.Text.RegularExpressions;

namespace LogLoopReader;

/// <summary>
/// The only path from a log line to a PR or an issue (LOG-LOOP-PLAN.md §6.1, layer 3). It keeps templates,
/// exception types and control ids, and replaces with typed placeholders: URLs (whole, so every OAuth
/// <c>code</c>, <c>state</c> and <c>public_token</c> goes with them), email addresses, bare OAuth
/// parameters, bare domains, phone shapes, amounts, runs of three or more digits, and every capitalised
/// word the repository does not already use — the positions a name takes. All-caps words (verdicts,
/// result codes) and dotted code identifiers (exception types, frames) are kept.
/// </summary>
public sealed partial class LogExcerptSanitizer(IReadOnlySet<string> vocabulary)
{
    public string Sanitize(string text)
    {
        var result = Url().Replace(text, "<url>");
        result = Email().Replace(result, "<email>");
        result = OauthParameter().Replace(result, m => m.Groups["name"].Value + "=<redacted>");
        result = Domain().Replace(result, "<domain>");
        result = Phone().Replace(result, "<phone>");
        result = Amount().Replace(result, "<amount>");
        result = Digits().Replace(result, "<n>");
        return Names(result);
    }

    /// <summary>
    /// Every identifier-like word in the repository's own source and documents — test folders excluded,
    /// since fixtures hold invented names a real log could share. A word in here is the code's, not a person's.
    /// </summary>
    public static IReadOnlySet<string> VocabularyFrom(string repoRoot)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in RepoFiles(repoRoot))
        {
            foreach (Match match in Word().Matches(File.ReadAllText(path)))
            {
                words.Add(match.Value);
            }
        }
        return words;
    }

    private static readonly string[] Extensions = [".cs", ".md", ".xaml", ".yml", ".csproj", ".sh"];

    private static IEnumerable<string> RepoFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => Extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .Where(p =>
            {
                var segments = Path.GetRelativePath(root, p).Split(Path.DirectorySeparatorChar);
                return !segments.Any(s => s is "bin" or "obj" or ".git" or "graphify-out" or "node_modules"
                    || s.EndsWith(".Tests", StringComparison.Ordinal) || s == "Tests");
            });

    private string Names(string text)
    {
        // Dotted identifiers are code, and are stepped over whole.
        var protectedSpans = DottedIdentifier().Matches(text).Select(m => (m.Index, End: m.Index + m.Length)).ToList();
        return Capitalised().Replace(text, m =>
        {
            if (protectedSpans.Any(s => m.Index >= s.Index && m.Index < s.End))
            {
                return m.Value;
            }
            var word = m.Value;
            var allCaps = word.Length > 1 && !word.Any(char.IsLower);
            return word.Length == 1 || allCaps || vocabulary.Contains(word) ? word : "<Name>";
        });
    }

    [GeneratedRegex(@"[a-zA-Z][a-zA-Z0-9+.-]*://\S+")]
    private static partial Regex Url();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b(?<name>code|state|public_token|access_token|refresh_token|id_token|link_token)=[^&\s]+")]
    private static partial Regex OauthParameter();

    [GeneratedRegex(@"\b[a-z0-9-]+(\.[a-z0-9-]+)*\.(com|org|net|io|co|us|uk|edu|gov|app|dev)\b")]
    private static partial Regex Domain();

    [GeneratedRegex(@"(\+\d{1,2}\s?)?\(?\b\d{3}\)?[\s.-]\d{3}[\s.-]\d{4}\b")]
    private static partial Regex Phone();

    [GeneratedRegex(@"\$\s?\d[\d,]*(\.\d+)?|\b\d[\d,]*\.\d{2}\s?(USD|EUR|GBP|CAD)\b")]
    private static partial Regex Amount();

    [GeneratedRegex(@"\d{3,}")]
    private static partial Regex Digits();

    [GeneratedRegex(@"\b[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+\b")]
    private static partial Regex DottedIdentifier();

    [GeneratedRegex(@"\b[A-Z][\p{L}'’-]*\b")]
    private static partial Regex Capitalised();

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9_]*")]
    private static partial Regex Word();
}
