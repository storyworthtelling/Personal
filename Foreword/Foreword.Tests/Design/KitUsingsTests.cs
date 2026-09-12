using System.Text.RegularExpressions;
using Xunit;

namespace Foreword.Tests.Design;

/// <summary>
/// Every kit type the phone names is reachable from the file that names it.
///
/// <para>This head cannot be compiled here, so CI is its only compiler — which means a missing
/// <c>using</c> costs a full round trip through a six-minute Android build to discover, and it has
/// now done that twice. The kit is the trap because its types are spread across three namespaces
/// (<c>Views</c>, <c>State</c>, <c>Layout</c>) under one assembly whose root namespace is the one
/// every page already imports: <c>using Storyworthtelling.Maui;</c> looks like it brings the kit in,
/// and it brings in exactly the half that is not the components.</para>
///
/// <para>So the rule is checked by reading the repository, like the other guards on this head. It is
/// not a type checker and does not pretend to be one — it answers one question, which is the one
/// that keeps being answered by a build failure.</para>
/// </summary>
public class KitUsingsTests
{
    [Fact]
    public void EveryKitTypeThePhoneNamesHasItsUsing()
    {
        var kit = Path.Combine(Repo(), "Storyworthtelling.Maui");
        Assert.True(Directory.Exists(kit), "the shared kit has moved; this guard is watching nothing.");

        // The kit's public types, by the namespace each one lives in.
        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(kit, "*.cs", SearchOption.AllDirectories))
        {
            if (Skip(file))
            {
                continue;
            }

            var source = File.ReadAllText(file);

            if (Regex.Match(source, @"^namespace\s+(?<ns>[\w.]+)\s*;", RegexOptions.Multiline)
                is not { Success: true } declared)
            {
                continue;
            }

            foreach (Match type in Regex.Matches(
                source,
                @"^public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+)*(?:class|record|struct|enum|interface)\s+(?<name>\w+)",
                RegexOptions.Multiline))
            {
                namespaces[type.Groups["name"].Value] = declared.Groups["ns"].Value;
            }
        }

        Assert.NotEmpty(namespaces);

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(Repo(), "Foreword", "Foreword.Android"), "*.cs", SearchOption.AllDirectories))
        {
            if (Skip(file))
            {
                continue;
            }

            var source = File.ReadAllText(file);
            var stripped = WithoutCommentsOrStrings(source);

            foreach (var (type, ns) in namespaces)
            {
                // A fully-qualified mention needs no using, and neither does one inside a comment.
                if (!Regex.IsMatch(stripped, $@"(?<![\w.]){Regex.Escape(type)}\b"))
                {
                    continue;
                }

                if (source.Contains($"using {ns};", StringComparison.Ordinal))
                {
                    continue;
                }

                offenders.Add($"{Path.GetFileName(file)} names {type} but has no 'using {ns};'");
            }
        }

        Assert.True(offenders.Count == 0,
            "These will not compile, and the only thing that would have said so is a six-minute "
            + "Android build:\n" + string.Join("\n", offenders.Distinct()));
    }

    private static bool Skip(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>
    /// Comments and string literals are where a type name appears without being a reference to it —
    /// a doc comment explaining which kit component a screen deliberately does <i>not</i> use would
    /// otherwise be reported as a missing using, which is a guard that cries wolf until it is
    /// switched off.
    /// </summary>
    private static string WithoutCommentsOrStrings(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        var withoutLines = Regex.Replace(withoutBlocks, @"//[^\n]*", " ");
        var withoutVerbatim = Regex.Replace(withoutLines, @"""""""[\s\S]*?""""""", " \"\" ");

        return Regex.Replace(withoutVerbatim, @"@?""(?:[^""\\\n]|\\.)*""", " \"\" ");
    }

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DESIGN.md")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
