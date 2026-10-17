using System.Text.RegularExpressions;
using Storyworthtelling.Paper;

namespace Constitution.Tests.Design;

/// <summary>
/// C-3.2.1 / PC6: the index card is ONE object in two apps. Book p.11 builds Constitution's meal
/// cards from Index's own stock, head rule, body rule and 22 dp pitch; "the same object in both
/// apps" holds by construction only if both read a single token. These tests pin the values to the
/// mockups' CSS and pin Index to the shared file, so neither app can grow a private copy.
/// </summary>
public class MaterialParityTests
{
    // The mockups' CSS values as numbers, so this guard file holds no quoted colour literal itself
    // (HexLiteralLeakTests scans Constitution/, tests included).
    private static string Rgb(int value) => "#" + value.ToString("X6");

    private static string RepoRoot() => Path.GetDirectoryName(ConstitutionSource.Root())!;

    private static string IndexTokensSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Ledger", "Ledger.Domain", "Design", "MaterialTokens.cs"));

    [Fact]
    public void IndexAndConstitutionShareOneCard()
    {
        // The shared token carries the mockup's values (book p.11: .scrap.index).
        Assert.Equal(Rgb(0xFFFEF8), MaterialTokens.NoteCardHex);
        Assert.Equal(Rgb(0xE4A7A2), MaterialTokens.CardHeadRuleHex);
        Assert.Equal(Rgb(0xD6E3EF), MaterialTokens.CardBodyRuleHex);
        Assert.Equal(22m, MaterialTokens.CardRulePitchDp);

        // Index no longer defines the card: its own token file holds no second copy...
        var indexTokens = IndexTokensSource();
        foreach (var name in new[] { "NoteCardHex", "CardHeadRuleHex", "CardBodyRuleHex", "CardRulePitchDp" })
        {
            Assert.DoesNotMatch(new Regex(@"\bconst\s+\w+\s+" + name + @"\b"), indexTokens);
        }

        // ...and the card views read the shared one, so Index and Constitution draw the same object.
        // Index's own MaterialTokens keeps the other materials, so a bare "MaterialTokens.NoteCardHex"
        // would not even compile there; the reads are found by name, wherever in Ledger.Android they sit.
        var ledgerHead = LedgerHeadFiles();
        Assert.True(ledgerHead.Count > 20, "The guard found too few Ledger.Android files to be reading the real head.");
        var unaliased = ledgerHead
            .SelectMany(file => UnaliasedCardReads(File.ReadAllText(file)).Select(read => Path.GetFileName(file) + ": " + read))
            .ToList();
        Assert.True(unaliased.Count == 0, "Index reads a card token off its own MaterialTokens:\n" + string.Join("\n", unaliased));

        foreach (var view in new[] { "Views/IndexCardView.cs", "Views/ActionCardHaze.cs", "Pages/AddManualAccountPage.xaml.cs" })
        {
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "Ledger", "Ledger.Android", view));
            Assert.Contains("using SharedMaterials = Storyworthtelling.Paper.MaterialTokens;", source);
            Assert.Matches(@"\bSharedMaterials\.(NoteCardHex|CardHeadRuleHex|CardBodyRuleHex|CardRulePitchDp)\b", source);
        }
    }

    private static IReadOnlyList<string> LedgerHeadFiles()
    {
        var head = Path.Combine(RepoRoot(), "Ledger", "Ledger.Android");
        return Directory.EnumerateFiles(head, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(head, "*.xaml", SearchOption.AllDirectories))
            .Where(file => !Path.GetRelativePath(head, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Reads of the four card tokens off a type literally named <c>MaterialTokens</c>, which in Ledger's
    /// head can only be Index's own (or a qualified copy of it). The shared type is reached through the
    /// <c>SharedMaterials</c> alias, a different identifier, or fully qualified under
    /// <c>Storyworthtelling.Paper</c>; neither is reported.
    /// </summary>
    internal static IReadOnlyList<string> UnaliasedCardReads(string source) =>
        Regex.Matches(source, @"(?<prefix>(?:[A-Za-z_]\w*\.)*)MaterialTokens\.(?<name>NoteCardHex|CardHeadRuleHex|CardBodyRuleHex|CardRulePitchDp)\b",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))
            .Where(match => match.Groups["prefix"].Value != "Storyworthtelling.Paper.")
            .Select(match => match.Value)
            .ToList();

    [Fact]
    public void TheUnaliasedReadDetectorFiresOnAnOldStyleLine()
    {
        // Built at run time so this file holds no old-style read of its own for the guard to trip on.
        var bare = "var stock = " + "MaterialTokens." + "NoteCardHex;";
        var qualified = "var rule = Index.Domain.Design." + "MaterialTokens." + "CardHeadRuleHex;";

        Assert.Equal(["MaterialTokens.NoteCardHex"], UnaliasedCardReads(bare));
        Assert.Equal(["Index.Domain.Design.MaterialTokens.CardHeadRuleHex"], UnaliasedCardReads(qualified));
        Assert.Single(UnaliasedCardReads("pitch = " + "MaterialTokens." + "CardRulePitchDp;"));
    }

    [Fact]
    public void AnOldStyleLinePlantedInARealCardViewIsCaught()
    {
        var real = File.ReadAllText(Path.Combine(RepoRoot(), "Ledger", "Ledger.Android", "Views", "IndexCardView.cs"));
        Assert.Empty(UnaliasedCardReads(real));

        var planted = real.Replace("SharedMaterials.NoteCardHex", "MaterialTokens." + "NoteCardHex", StringComparison.Ordinal);

        Assert.NotEqual(real, planted);
        Assert.NotEmpty(UnaliasedCardReads(planted));
    }

    [Fact]
    public void TheUnaliasedReadDetectorLeavesTheSharedReadsAndIndexOwnTokensAlone()
    {
        Assert.Empty(UnaliasedCardReads("var stock = SharedMaterials.NoteCardHex;"));
        Assert.Empty(UnaliasedCardReads("var rule = Storyworthtelling.Paper.MaterialTokens.CardBodyRuleHex;"));
        Assert.Empty(UnaliasedCardReads("var haze = MaterialTokens.HazeAlpha;"));
        Assert.Empty(UnaliasedCardReads("// the note-card stock lives in MaterialTokens' shared copy"));
    }

    [Fact]
    public void ConstitutionDeclaresNoCardTokenOfItsOwn()
    {
        var offenders = ConstitutionSource.Files(".cs")
            .Where(file => !ConstitutionSource.IsGuard(file, "MaterialParityTests.cs"))
            .Where(file => Regex.IsMatch(File.ReadAllText(file),
                @"\b(const|static)\b[^;=]*\b(NoteCardHex|CardHeadRuleHex|CardBodyRuleHex|CardRulePitchDp)\b"))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "Constitution redefines a card token:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheScrapbookMaterialsMatchTheMockupCss()
    {
        // Crinkle ground (.dv-well data-URI SVG): one stroke ink, creases at 7%, ticks 4-9%.
        Assert.Equal(Rgb(0x6B6759), MaterialTokens.CreaseInkHex);
        Assert.Equal(Rgb(0x6B6759), MaterialTokens.TickInkHex);
        Assert.Equal(0.07m, MaterialTokens.CreaseAlpha);
        Assert.Equal(1.0m, MaterialTokens.CreaseStrokeWidth);
        Assert.Equal(0.04m, MaterialTokens.TickAlphaMin);
        Assert.Equal(0.09m, MaterialTokens.TickAlphaMax);
        Assert.Equal(0.7m, MaterialTokens.TickStrokeWidth);

        // .perf: 1.5px dotted rgba(107,103,89,.22)
        Assert.Equal(Rgb(0x6B6759), MaterialTokens.PerforationInkHex);
        Assert.Equal(0.22m, MaterialTokens.PerforationAlpha);
        Assert.Equal(1.5m, MaterialTokens.PerforationWidth);

        // .tape: rgba(244,236,206,.62) with a rgba(138,133,120,.18) edge
        Assert.Equal(Rgb(0xF4ECCE), MaterialTokens.TapeHex);
        Assert.Equal(0.62m, MaterialTokens.TapeAlpha);
        Assert.Equal(Rgb(0x8A8578), MaterialTokens.TapeEdgeHex);
        Assert.Equal(0.18m, MaterialTokens.TapeEdgeAlpha);

        // .scrap.mag
        Assert.Equal(Rgb(0xFDFCF9), MaterialTokens.MagazineStockHex);
        Assert.Equal(Rgb(0xCFC6B2), MaterialTokens.MagazineEdgeHex);
        Assert.Equal(Rgb(0x33322B), MaterialTokens.HalftoneInkHex);
        Assert.Equal(0.07m, MaterialTokens.HalftoneAlpha);
        Assert.Equal(3, MaterialTokens.HalftonePitchDp);
        Assert.Equal(Rgb(0x6B6759), MaterialTokens.MagazineColumnInkHex);

        // .scrap.note: the card stock with 24 px rules in the hairline
        Assert.Equal(MaterialTokens.NoteCardHex, MaterialTokens.NotepadLeafHex);
        Assert.Equal(Rgb(0xE3DCC9), MaterialTokens.NotepadRuleHex);
        Assert.Equal(24, MaterialTokens.NotepadRulePitchDp);

        // .scrap.receipt and .scrap.grid: white stock
        Assert.Equal(Rgb(0xFFFFFF), MaterialTokens.ReceiptHex);
        Assert.Equal(Rgb(0xFFFFFF), MaterialTokens.ChartPaperHex);
    }

    [Fact]
    public void TheChartGridIsTheReferenceMarkAtTenPercent()
    {
        // Not a second blue: the grid is the Reference role's mark (rgba(76,103,135,.10)).
        Assert.Equal(PaperColorTokens.ReferenceMark, MaterialTokens.ChartGridHex);
        Assert.Equal(0.10m, MaterialTokens.ChartGridAlpha);
        Assert.Equal(12, MaterialTokens.ChartGridPitchDp);
    }
}
