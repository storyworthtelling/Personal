using System.Text.RegularExpressions;
using System.Xml;
using Xunit;

namespace Foreword.Tests.Design;

/// <summary>
/// Every XAML file parses as XML.
///
/// <para>Trivial-sounding, and it earns its place because this head cannot be compiled without
/// the MAUI/Android workload — CI is its only compiler, so a malformed page costs a full CI
/// round trip to discover. It was written after exactly that: section dividers written in C#
/// habit (<c>&lt;!-- ---- Delivery ---- --&gt;</c>) are illegal, because XML forbids a double
/// hyphen inside a comment. Nothing in the editor complains and the page simply fails to
/// build.</para>
///
/// <para>The other two tests here reach further than well-formedness, because the compiler cannot
/// help with either: a style applied to the wrong control throws when the page opens rather than
/// when it builds, and since the styles moved into the shared kit that mismatch now spans an
/// assembly boundary, where nothing checks it at all.</para>
/// </summary>
public class XamlWellFormedTests
{
    [Fact]
    public void EveryXamlFileParses()
    {
        var files = Directory
            .EnumerateFiles(Root(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(files);

        var broken = new List<string>();
        foreach (var file in files)
        {
            try
            {
                new XmlDocument().Load(file);
            }
            catch (XmlException e)
            {
                broken.Add($"{Path.GetRelativePath(Root(), file)}: {e.Message}");
            }
        }

        Assert.True(broken.Count == 0,
            "These XAML files are not well-formed XML, so the head will not build:\n"
            + string.Join("\n", broken));
    }

    /// <summary>
    /// Every <c>Style="{StaticResource X}"</c> names a style whose <c>TargetType</c> is the
    /// element it is applied to.
    ///
    /// <para>Written after applying <c>MonoEntry</c> (TargetType Entry) to an <c>Editor</c>. A
    /// mismatched style is not a compile error and not a XAML-parse error — it throws when the
    /// page is opened, on the device, which is the worst place to find it.</para>
    /// </summary>
    [Fact]
    public void EveryStyleIsAppliedToItsTargetType()
    {
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dictionary in KitDictionaries())
        {
            foreach (Match m in Regex.Matches(
                File.ReadAllText(dictionary),
                @"<Style\s+x:Key=""(?<key>[^""]+)""\s+TargetType=""(?<type>[^""]+)"""))
            {
                targets[m.Groups["key"].Value] = m.Groups["type"].Value;
            }
        }

        Assert.NotEmpty(targets);

        var wrong = new List<string>();
        foreach (var page in XamlFiles())
        {
            var text = File.ReadAllText(page);
            foreach (Match m in Regex.Matches(
                text, @"<(?<element>[A-Za-z]+)\b[^>]*?Style=""\{StaticResource (?<key>[^}]+)\}""", RegexOptions.Singleline))
            {
                var element = m.Groups["element"].Value;
                var key = m.Groups["key"].Value;
                if (targets.TryGetValue(key, out var target) && target != element)
                {
                    wrong.Add($"{Path.GetFileName(page)}: <{element}> uses {key}, whose TargetType is {target}");
                }
            }
        }

        Assert.True(wrong.Count == 0,
            "A style applied to the wrong control throws when the page opens, on the device:\n"
            + string.Join("\n", wrong));
    }

    /// <summary>
    /// Every handler named in XAML exists in the page's code-behind. The compiler catches this,
    /// but only in CI, because this head needs the MAUI workload to build at all.
    /// </summary>
    [Fact]
    public void EveryXamlHandlerExistsInItsCodeBehind()
    {
        var missing = new List<string>();
        foreach (var page in XamlFiles().Where(f => File.Exists(f + ".cs")))
        {
            var code = File.ReadAllText(page + ".cs");
            foreach (Match m in Regex.Matches(
                File.ReadAllText(page), @"(?:Clicked|Tapped|TextChanged|Unfocused|Toggled)=""(?<handler>[A-Za-z_][A-Za-z0-9_]*)"""))
            {
                var handler = m.Groups["handler"].Value;
                if (!code.Contains($"void {handler}(", StringComparison.Ordinal))
                {
                    missing.Add($"{Path.GetFileName(page)}: {handler}");
                }
            }
        }

        Assert.True(missing.Count == 0,
            "These handlers are named in XAML but not defined in the code-behind:\n"
            + string.Join("\n", missing));
    }

    /// <summary>
    /// The styles live in the shared <c>Storyworthtelling.Maui</c> kit now, not in this app, so the
    /// TargetTypes have to be read from there. Looked up by repository rather than by solution: the
    /// kit sits beside the app, not inside it.
    /// </summary>
    private static IEnumerable<string> KitDictionaries()
    {
        var kit = new DirectoryInfo(Root());
        while (kit is not null && !Directory.Exists(Path.Combine(kit.FullName, "Storyworthtelling.Maui")))
        {
            kit = kit.Parent;
        }

        Assert.True(kit is not null, "Could not find Storyworthtelling.Maui above " + Root());
        return Directory.EnumerateFiles(
            Path.Combine(kit!.FullName, "Storyworthtelling.Maui"), "*.xaml", SearchOption.AllDirectories);
    }

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(Root(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Foreword.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
