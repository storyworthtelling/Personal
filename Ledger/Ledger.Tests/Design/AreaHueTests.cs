using System.Text.RegularExpressions;
using Index.Domain.Design;
using Xunit;

namespace Index.Tests.Design;

/// <summary>
/// Guard G9 (D120): Index's own five-areas hue-separation rule, extended from
/// <c>FocusPhone/Codex.Tests/Design/AreaHueTests.cs</c> — which does not reach here at all (I9 in
/// INDEX-P11-PLAN.md §1: it enumerates <c>Score.Android</c>/<c>Post.Android</c> by hardcoded name and
/// reads the NATIVE <c>BinderArea Tab =&gt; new(...)</c> shape, not the MAUI kit's
/// <c>record BinderArea(string Label, string GlyphPath, Color Hue, Color Cloth)</c>). This is the
/// first "no per-app exception mechanism exists yet" guard the earlier IconStudio D116 paragraph
/// warned about, written for Index's own MAUI shape.
///
/// <para>Reads <c>LedgerAreas.cs</c> BY SOURCE TEXT rather than by loading the type, the same way
/// <c>KitAdoptionTests</c>/<c>HexLiteralLeakTests</c> do everywhere else in this folder: it lives in
/// <c>Ledger.Android</c>, a <c>net10.0-android</c> project a plain <c>net10.0</c> test project cannot
/// reference at all (the same reason <c>Storyworthtelling.Maui</c> cannot be referenced either).</para>
///
/// <para><b>Scope, per the plan (§1, "not required by this task"):</b> this guard covers the
/// five-areas STRUCTURAL facts — no two share a mark/fill pair, Accounts carries the identity hue,
/// Settings alone carries the neutral grey, and each area's mark/fill are the same hue's two tiers —
/// not the full "no control on the page wears its own tab's role hue" check <c>AreaHueTests</c>
/// enforces for the native heads, which needs the Positive-role wheel from
/// <c>Storyworthtelling.Paper.PaletteSpec</c> and is reasonable future work per the plan, not required
/// here.</para>
/// </summary>
public class AreaHueTests
{
    // D115/D117 — the identity ladder Tools/IconStudio/Tokens.cs commits (IndexMarkHex/IndexFillHex).
    // Duplicated here as a literal comparison value, not a second decision: LedgerAreas.cs is the
    // registered token file for this app's tab hues (HexLiteralLeakTests' allow-list), and this
    // assertion is checking that IT still says what D115/D117 says, the same role a parity test plays
    // elsewhere in the suite.
    private const string ExpectedAccountsMarkHex = "#596C45";
    private const string ExpectedAccountsFillHex = "#B6C3A9";

    // Hue tolerance for "the same hue's two tiers" — a fill tier is lighter/less saturated than its
    // mark, which can drift a few degrees in OKLCH even for a hand-picked pair; this is generous
    // enough to pass every area below while still catching an outright wrong pairing (>30° apart).
    private const double SameHueToleranceDegrees = 20.0;

    [Fact]
    public void FiveAreasAreDeclared_InTheStripsOrder()
    {
        var source = LedgerAreasSource();
        Assert.Contains("Accounts,", source);
        Assert.Contains("Snowball,", source);
        Assert.Contains("Budget,", source);
        Assert.Contains("Savings,", source);
        Assert.Contains("Settings,", source);

        var order = new[] { "Accounts", "Snowball", "Budget", "Savings", "Settings" };
        var positions = order.Select(name => source.IndexOf($"\"{name}\"", StringComparison.Ordinal)).ToList();
        Assert.True(positions.All(p => p >= 0), "One of the five areas is not built as a BinderArea with that label.");
        Assert.Equal(positions, positions.OrderBy(p => p));
    }

    [Fact]
    public void AccountsCarriesTheCommittedIdentityHue()
    {
        var (markHex, fillHex) = ReadPair("Accounts");
        Assert.Equal(ExpectedAccountsMarkHex, markHex, ignoreCase: true);
        Assert.Equal(ExpectedAccountsFillHex, fillHex, ignoreCase: true);
    }

    [Fact]
    public void SettingsAloneReadsTheKitsNeutralGreyTokens_NotALiteralHex()
    {
        var source = LedgerAreasSource();
        var settingsLine = source.Split('\n').Single(line => line.Contains("\"Settings\"", StringComparison.Ordinal)
            && line.Contains("new(", StringComparison.Ordinal));

        Assert.Contains("PaperResources.Colour(\"InkMuted\")", settingsLine, StringComparison.Ordinal);
        Assert.Contains("PaperResources.Colour(\"PaperDeep\")", settingsLine, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Accounts")]
    [InlineData("Snowball")]
    [InlineData("Budget")]
    [InlineData("Savings")]
    public void NoTwoWorkingAreasShareAMarkOrFillHex(string area)
    {
        var areas = new[] { "Accounts", "Snowball", "Budget", "Savings" };
        var (markHex, fillHex) = ReadPair(area);

        foreach (var other in areas.Where(a => a != area))
        {
            var (otherMark, otherFill) = ReadPair(other);
            Assert.False(string.Equals(markHex, otherMark, StringComparison.OrdinalIgnoreCase),
                $"{area} and {other} share a mark hex ({markHex}).");
            Assert.False(string.Equals(fillHex, otherFill, StringComparison.OrdinalIgnoreCase),
                $"{area} and {other} share a fill hex ({fillHex}).");
        }
    }

    [Theory]
    [InlineData("Accounts")]
    [InlineData("Snowball")]
    [InlineData("Budget")]
    [InlineData("Savings")]
    public void EachAreasMarkAndFillAreTheSameHuesTwoTiers(string area)
    {
        var (markHex, fillHex) = ReadPair(area);
        var (_, _, markHue) = Oklch.FromHex(markHex);
        var (_, _, fillHue) = Oklch.FromHex(fillHex);

        var separation = Math.Abs(markHue - fillHue) % 360.0;
        separation = separation > 180.0 ? 360.0 - separation : separation;

        Assert.True(separation <= SameHueToleranceDegrees,
            $"{area}'s mark ({markHex}, {markHue:0.#}°) and fill ({fillHex}, {fillHue:0.#}°) read as "
            + $"different hues ({separation:0.#}° apart) — a mark/fill pair should be one hue's two tiers.");
    }

    // ---- helpers ----

    private static (string MarkHex, string FillHex) ReadPair(string area)
    {
        var source = LedgerAreasSource();
        var markConst = Regex.Match(source, $@"{area}MarkHex\s*=\s*""(#[0-9A-Fa-f]{{6}})""");
        var fillConst = Regex.Match(source, $@"{area}FillHex\s*=\s*""(#[0-9A-Fa-f]{{6}})""");

        Assert.True(markConst.Success, $"No {area}MarkHex constant found in LedgerAreas.cs.");
        Assert.True(fillConst.Success, $"No {area}FillHex constant found in LedgerAreas.cs.");

        return (markConst.Groups[1].Value, fillConst.Groups[1].Value);
    }

    private static string LedgerAreasSource() => File.ReadAllText(LedgerAreasPath());

    private static string LedgerAreasPath()
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Android", "Pages", "LedgerAreas.cs");
        Assert.True(File.Exists(path), $"LedgerAreas.cs not found at {path}.");
        return path;
    }
}
