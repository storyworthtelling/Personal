using System.Text.RegularExpressions;
using Xunit;

namespace Folio.Tests.Guard;

/// <summary>
/// G5 — this head owns no palette of its own.
///
/// <para><b>Why a guard and not just a deletion.</b> The design system used to be written twice and
/// enforced once: every MAUI head kept a private copy of the palette, and its leak-guard test held a
/// <i>second</i> hand-typed copy to check the first against — so both could be wrong together, and
/// were. The parity run that followed Foreword's adoption found four live bugs at once, among them
/// an accent still set to the retired terracotta and two fonts shipping in the APK with no alias
/// registered, which MAUI does not report: it substitutes the system face and says nothing.</para>
///
/// <para>Deleting Folio's dictionary fixes today. This is what stops it coming back — because the
/// thing that brings it back is reasonable. A head needs one more colour, adding it to the shared
/// kit touches four apps, and a local dictionary with "just this one" in it is a five-minute job
/// that silently re-forks the palette.</para>
///
/// <para>Read by content rather than by path, like the rest of this head's guards: the head is found
/// from the frozen ApplicationId, so a later rename cannot quietly move the thing being guarded.</para>
/// </summary>
public class KitAdoptionTests
{
    // A palette this head defines itself, rather than consumes. The kit is the only source.
    [Fact]
    public void TheHeadDeclaresNoResourceDictionaryOfItsOwn()
    {
        var dictionaries = HeadFiles("*.xaml")
            .Where(file => File.ReadAllText(file).Contains("<ResourceDictionary", StringComparison.Ordinal))
            .Select(FolioInvariantsTests.Relative)
            .ToList();

        Assert.True(dictionaries.Count == 0,
            "This head declares its own ResourceDictionary. The Paper & Ink tokens live in the shared "
            + "Storyworthtelling.Maui kit and nowhere else; a local dictionary re-forks the palette.\n"
            + string.Join("\n", dictionaries));
    }

    [Fact]
    public void TheHeadReferencesTheSharedKit()
    {
        Assert.Contains("Storyworthtelling.Maui.csproj", HeadProject(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Merged in code, not named in App.xaml. A XAML <c>Source=</c> resolves only inside its own
    /// assembly, so the kit's dictionary cannot be referenced that way from a head at all — it has to
    /// be constructed and merged, and it has to happen before anything reads a style out of it.
    /// </summary>
    [Fact]
    public void TheHeadMergesThePaperKit()
    {
        var app = HeadFiles("*.cs")
            .Select(File.ReadAllText)
            .FirstOrDefault(text => text.Contains("InitializeComponent()", StringComparison.Ordinal)
                                    && text.Contains(": Application", StringComparison.Ordinal));

        Assert.NotNull(app);
        Assert.Contains("MergedDictionaries.Add(new PaperKit())", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// The eight fonts ship in every APK regardless; what makes them usable is the alias. An
    /// unregistered family is not an error in MAUI — the platform substitutes the system face and
    /// reports nothing, which is how Serif Black and Pinyon Script were silently absent before.
    /// </summary>
    [Fact]
    public void TheHeadRegistersTheKitsFonts()
    {
        var program = HeadFiles("*.cs")
            .Select(File.ReadAllText)
            .FirstOrDefault(text => text.Contains("ConfigureFonts", StringComparison.Ordinal));

        Assert.NotNull(program);
        Assert.Contains("PaperFonts.Register", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every token the screens ask for is one the kit defines.
    ///
    /// <para>This is the check that makes the swap safe rather than merely green. A
    /// <c>{StaticResource Whatever}</c> that resolves to nothing is not a build error — MAUI raises it
    /// when the page is inflated, on the device, which is the one place nobody here can look. The head
    /// cannot be run in this environment and CI only compiles it, so without this the first report of
    /// a missing key would be the owner opening a screen.</para>
    /// </summary>
    [Fact]
    public void EveryResourceKeyTheScreensUseIsDefinedInTheKit()
    {
        var defined = Regex.Matches(File.ReadAllText(KitDictionary()), "x:Key=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = HeadFiles("*.xaml")
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"\{(?:Static|Dynamic)Resource\s+([A-Za-z0-9_]+)\}")
                .Select(match => (File: FolioInvariantsTests.Relative(file), Key: match.Groups[1].Value)))
            .Where(use => !defined.Contains(use.Key))
            .Select(use => $"{use.Key}  ({use.File})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "These screens ask for tokens the kit does not define. Nothing fails until the page is "
            + "inflated on a device:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// Nothing reads a token with the dictionary's <b>indexer</b>.
    ///
    /// <para>This is the subtlest consequence of adopting the kit, and it throws at run time rather
    /// than failing to build. <c>Resources["DisplayLabel"]</c> looks only at that dictionary's own
    /// entries; <c>TryGetValue</c> walks what has been merged into it. While the head owned the
    /// palette the indexer worked, so the call sites written that way were correct — and the moment
    /// the kit became the only source they all became exceptions waiting for the screen that uses
    /// them. Two report screens carried nine of them.</para>
    ///
    /// <para><c>PaperResources</c> is the one reader, and it throws with a sentence saying the kit is
    /// not merged rather than returning a default — a view drawn transparent-on-transparent is a bug
    /// that looks like a rendering problem for an afternoon.</para>
    /// </summary>
    [Fact]
    public void NoScreenReadsATokenThroughTheDictionaryIndexer()
    {
        var offenders = HeadFiles("*.cs")
            .Where(file => Code(file).Any(line => Regex.IsMatch(line, @"Resources\[\s*[""a-zA-Z]")))
            .Select(FolioInvariantsTests.Relative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These read a Paper & Ink token with the indexer, which does not see merged dictionaries "
            + "and throws once the kit is the only source. Use PaperResources:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// A file's lines with the comments dropped. The guard must forbid the <i>mechanism</i>, not the
    /// characters: the first version of it failed on App.xaml.cs, where the only match was the doc
    /// comment explaining why the indexer must not be used.
    /// </summary>
    private static IEnumerable<string> Code(string file) =>
        File.ReadAllLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));

    private static string KitDictionary()
    {
        var kit = Path.Combine(FolioInvariantsTests.RepoRoot(), "Storyworthtelling.Maui", "Resources", "Styles", "Paper.xaml");
        Assert.True(File.Exists(kit), $"The shared kit's dictionary is not where this guard expects it: {kit}");
        return kit;
    }

    private static string HeadProject() =>
        File.ReadAllText(FolioInvariantsTests.HeadProjectFile());

    private static IEnumerable<string> HeadFiles(string pattern) =>
        Directory.EnumerateFiles(Path.GetDirectoryName(FolioInvariantsTests.HeadProjectFile())!, pattern, SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"));
}
