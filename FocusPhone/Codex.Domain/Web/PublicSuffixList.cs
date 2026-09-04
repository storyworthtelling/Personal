using System.Reflection;

namespace Codex.Domain.Web;

/// <summary>
/// Implements the publicsuffix.org algorithm over an embedded snapshot of the
/// Public Suffix List, refreshed by shipping an app update. Used to compute the
/// registrable domain (eTLD+1) that the restricted browser confines navigation to.
/// </summary>
public class PublicSuffixList
{
    private static readonly Lazy<PublicSuffixList> Embedded = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "Codex.Domain.Resources.public_suffix_list.dat";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' is missing from {assembly.GetName().Name}; the Public Suffix List snapshot was not packed into the build.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    });

    private readonly HashSet<string> normalRules = [];
    private readonly HashSet<string> wildcardBases = [];
    private readonly HashSet<string> exceptionRules = [];

    private PublicSuffixList()
    {
    }

    public static PublicSuffixList LoadEmbedded() => Embedded.Value;

    public static PublicSuffixList Parse(string text)
    {
        var list = new PublicSuffixList();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//"))
            {
                continue;
            }
            line = line.ToLowerInvariant();
            if (line.StartsWith('!'))
            {
                list.exceptionRules.Add(line[1..]);
            }
            else if (line.StartsWith("*."))
            {
                list.wildcardBases.Add(line[2..]);
            }
            else
            {
                list.normalRules.Add(line);
            }
        }
        return list;
    }

    /// <summary>
    /// Returns the registrable domain (public suffix plus one label) of the host,
    /// or null when the host itself is a public suffix and nothing is registrable.
    /// </summary>
    public string? GetRegistrableDomain(string host)
    {
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        var labels = host.Split('.');
        var suffixLength = MatchSuffixLength(labels);

        if (labels.Length <= suffixLength)
        {
            return null;
        }
        return string.Join('.', labels[(labels.Length - suffixLength - 1)..]);
    }

    private int MatchSuffixLength(string[] labels)
    {
        var longestMatch = 1; // The implicit default rule "*": an unknown TLD is itself a suffix.

        for (var start = 0; start < labels.Length; start++)
        {
            var candidate = string.Join('.', labels[start..]);

            // An exception rule prevails over every other match; its suffix is
            // the rule with the leftmost label removed.
            if (exceptionRules.Contains(candidate))
            {
                return labels.Length - start - 1;
            }
            if (normalRules.Contains(candidate))
            {
                longestMatch = Math.Max(longestMatch, labels.Length - start);
            }
            if (start + 1 < labels.Length && wildcardBases.Contains(string.Join('.', labels[(start + 1)..])))
            {
                longestMatch = Math.Max(longestMatch, labels.Length - start);
            }
        }
        return longestMatch;
    }
}
