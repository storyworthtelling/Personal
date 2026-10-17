using Constitution.Domain.Scraps;

namespace Constitution.Tests.Scraps;

/// <summary>
/// C-3.2.3 — each thing on its own taped scrap at a slight tilt, never touching another (D47,
/// D48). The numbers asserted here are read off the page-11-to-16 mockup CSS, quoted in
/// <c>ScrapMetrics</c>; the overlap judge is <c>ScrapOracle</c>, a different method from the one
/// the layout uses.
/// </summary>
public class ScrapLayoutTests
{
    private const double Epsilon = 1e-9;

    private static void AssertNothingTouches(IReadOnlyList<PlacedScrap> placed, string context, ref int pairs)
    {
        for (var i = 0; i < placed.Count; i++)
        {
            var body = ScrapOracle.Body(placed[i]);
            for (var j = i + 1; j < placed.Count; j++)
            {
                pairs++;
                Assert.False(ScrapOracle.Touch(body, ScrapOracle.Body(placed[j])),
                    $"{context}: scraps {placed[i].Id} and {placed[j].Id} overlap.");
            }
        }
    }

    [Fact]
    public void NoTwoScrapsOverlap()
    {
        var pairs = 0;
        var layouts = 0;
        foreach (var phoneWidth in ScrapFixtures.PhoneWidths())
        {
            for (var count = 1; count <= 12; count++)
            {
                for (var start = 0; start < ScrapFixtures.AllKinds.Length; start++)
                {
                    var placed = ScrapLayout.Place(ScrapFixtures.Mixed(count, start), ScrapFixtures.WellWidthFor(phoneWidth));
                    Assert.Equal(count, placed.Count);
                    AssertNothingTouches(placed, $"phone {phoneWidth} dp, {count} scraps", ref pairs);
                    layouts++;
                }
            }
        }

        // Non-vacuous: the sweep really did lay out thousands of pages and judge many pairs.
        Assert.Equal(71 * 12 * 5, layouts);
        Assert.True(pairs > 100000, $"Only {pairs} pairs were judged.");
    }

    [Fact]
    public void NoTwoScrapsOverlapAcrossRandomPagesFromAFixedSeed()
    {
        var random = new Random(20261017);
        var pairs = 0;
        for (var round = 0; round < 3000; round++)
        {
            // Fractional widths too: real phones are not whole numbers of dp.
            var phoneWidth = 360 + (random.NextDouble() * 70);
            var scraps = ScrapFixtures.Random(random);
            var placed = ScrapLayout.Place(scraps, ScrapLayout.WellWidthDp(phoneWidth));
            AssertNothingTouches(placed, $"round {round}, phone {phoneWidth:F2} dp", ref pairs);
        }

        Assert.True(pairs > 10000, $"Only {pairs} pairs were judged.");
    }

    [Fact]
    public void TheOverlapJudgeFiresOnAnOverlappingPage()
    {
        // Guards the guard: a hand-built page with two scraps on top of each other must be caught.
        var tape = new TapeAnchor(100, 0, 78, 20, 0);
        var a = new PlacedScrap("a", ScrapKind.IndexCard, 20, 20, 200, 100, 0.5, tape);
        var b = new PlacedScrap("b", ScrapKind.Receipt, 30, 110, 200, 100, -0.5, tape);
        var pairs = 0;

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNothingTouches([a, b], "planted", ref pairs));
        Assert.Equal(1, pairs);
    }

    [Fact]
    public void TiltsStayInRange()
    {
        var seen = new List<double>();
        foreach (var phoneWidth in new[] { 360, 393, 430 })
        {
            for (var count = 1; count <= 12; count++)
            {
                foreach (var scrap in ScrapLayout.Place(ScrapFixtures.Mixed(count, count), ScrapFixtures.WellWidthFor(phoneWidth)))
                {
                    var magnitude = Math.Abs(scrap.TiltDegrees);
                    Assert.InRange(magnitude, ScrapMetrics.MinTiltDegrees - Epsilon, ScrapMetrics.MaxTiltDegrees + Epsilon);
                    seen.Add(scrap.TiltDegrees);
                }
            }
        }

        Assert.True(seen.Count > 100);
    }

    [Fact]
    public void TheTiltRangeIsTheMockupsOwn()
    {
        // Every inline --t in the page-11..16 mockups lies in -1.2deg..1.0deg, and none is nearer
        // zero than 0.4deg. The layout draws magnitudes from 0.4 to 1.2, either way round.
        Assert.Equal(0.4, ScrapMetrics.MinTiltDegrees);
        Assert.Equal(1.2, ScrapMetrics.MaxTiltDegrees);
    }

    [Fact]
    public void TiltsUseTheWholeRangeRatherThanOneAngle()
    {
        var magnitudes = new HashSet<double>();
        for (var i = 0; i < 400; i++)
        {
            magnitudes.Add(Math.Abs(ScrapLayout.TiltFor("scrap-" + i, i)));
        }

        Assert.True(magnitudes.Count >= 7, $"Only {magnitudes.Count} distinct tilt magnitudes.");
        Assert.Contains(magnitudes, m => m < 0.6);
        Assert.Contains(magnitudes, m => m > 1.0);
    }

    [Fact]
    public void NeighboursNeverShareAnAngleAndLeanOppositeWays()
    {
        foreach (var phoneWidth in new[] { 360, 400, 430 })
        {
            for (var start = 0; start < ScrapFixtures.AllKinds.Length; start++)
            {
                var placed = ScrapLayout.Place(ScrapFixtures.Mixed(12, start), ScrapFixtures.WellWidthFor(phoneWidth));
                for (var i = 1; i < placed.Count; i++)
                {
                    Assert.NotEqual(placed[i - 1].TiltDegrees, placed[i].TiltDegrees);
                    Assert.True(Math.Sign(placed[i - 1].TiltDegrees) != Math.Sign(placed[i].TiltDegrees),
                        "Neighbouring scraps should lean opposite ways so the page reads as calm, not as a slope.");
                }
            }
        }
    }

    [Fact]
    public void TheSameScrapAlwaysGetsTheSameTilt()
    {
        var scraps = ScrapFixtures.Mixed(7);
        var first = ScrapLayout.Place(scraps, ScrapFixtures.WellWidthFor(393));
        var again = ScrapLayout.Place(scraps, ScrapFixtures.WellWidthFor(393));
        var narrower = ScrapLayout.Place(scraps, ScrapFixtures.WellWidthFor(360));

        Assert.Equal(first, again);
        Assert.Equal(first.Select(s => s.TiltDegrees), narrower.Select(s => s.TiltDegrees));
        Assert.Equal(ScrapLayout.TiltFor("fixed-id", 3), ScrapLayout.TiltFor("fixed-id", 3));
    }

    [Fact]
    public void EveryScrapIsHeldByTapeInsideThePage()
    {
        // D48: every scrap, no exceptions, and the tape never pokes off the page.
        var checkedScraps = 0;
        foreach (var phoneWidth in ScrapFixtures.PhoneWidths())
        {
            var wellWidth = ScrapFixtures.WellWidthFor(phoneWidth);
            foreach (var scrap in ScrapLayout.Place(ScrapFixtures.Mixed(12, phoneWidth), wellWidth))
            {
                Assert.NotNull(scrap.TapeAnchor);
                Assert.True(scrap.TapeAnchor.WidthDp > 0 && scrap.TapeAnchor.HeightDp > 0);
                foreach (var corner in ScrapOracle.Tape(scrap.TapeAnchor))
                {
                    Assert.InRange(corner.X, 0, wellWidth);
                    Assert.True(corner.Y >= 0, $"{scrap.Id}'s tape starts above the page ({corner.Y}).");
                }

                checkedScraps++;
            }
        }

        Assert.Equal(71 * 12, checkedScraps);
    }

    [Fact]
    public void EachKindIsTapedWhereTheMockupTapesIt()
    {
        var placed = ScrapLayout.Place(
            ScrapFixtures.AllKinds.Select((kind, i) => new Scrap(kind, "kind-" + i, 120)).ToList(),
            ScrapFixtures.WellWidthFor(393));

        foreach (var scrap in placed)
        {
            var (fraction, widthDp, localRotation, top) = scrap.Kind switch
            {
                ScrapKind.MagazineClipping => (0.24, 78.0, 5.0, -11.0),
                ScrapKind.IndexCard => (0.76, 64.0, -2.0, -11.0),
                ScrapKind.NotepadLeaf => (0.50, 78.0, -3.0, -4.0),
                ScrapKind.Receipt => (0.30, 78.0, 3.0, -11.0),
                ScrapKind.ChartScrap => (0.50, 78.0, -3.0, -11.0),
                _ => throw new InvalidOperationException("A new kind needs its tape read off the mockups."),
            };

            Assert.Equal(widthDp, scrap.TapeAnchor.WidthDp);
            Assert.Equal(20, scrap.TapeAnchor.HeightDp);
            Assert.Equal(scrap.TiltDegrees + localRotation, scrap.TapeAnchor.RotationDegrees, 9);

            // Where the tape's centre must be: its place in the unturned box, turned with the box.
            var centreX = scrap.X + (scrap.Width / 2);
            var centreY = scrap.Y + (scrap.Height / 2);
            var dx = (fraction * scrap.Width) - (scrap.Width / 2);
            var dy = top + 10 - (scrap.Height / 2);
            var radians = scrap.TiltDegrees * Math.PI / 180;
            Assert.Equal(centreX + (dx * Math.Cos(radians)) - (dy * Math.Sin(radians)), scrap.TapeAnchor.CenterX, 6);
            Assert.Equal(centreY + (dx * Math.Sin(radians)) + (dy * Math.Cos(radians)), scrap.TapeAnchor.CenterY, 6);
        }
    }

    [Fact]
    public void TapeTouchesItsOwnScrapAndNoOther()
    {
        var holds = 0;
        foreach (var phoneWidth in ScrapFixtures.PhoneWidths())
        {
            for (var start = 0; start < ScrapFixtures.AllKinds.Length; start++)
            {
                var placed = ScrapLayout.Place(ScrapFixtures.Mixed(12, start), ScrapFixtures.WellWidthFor(phoneWidth));
                for (var i = 0; i < placed.Count; i++)
                {
                    var tape = ScrapOracle.Tape(placed[i].TapeAnchor);
                    Assert.True(ScrapOracle.Touch(tape, ScrapOracle.Body(placed[i])),
                        $"{placed[i].Id}'s tape does not reach its own scrap.");
                    holds++;

                    for (var j = 0; j < placed.Count; j++)
                    {
                        if (j != i)
                        {
                            Assert.False(ScrapOracle.Touch(tape, ScrapOracle.Body(placed[j])),
                                $"{placed[i].Id}'s tape touches {placed[j].Id}.");
                            Assert.False(ScrapOracle.Touch(tape, ScrapOracle.Tape(placed[j].TapeAnchor)) && j > i,
                                $"{placed[i].Id}'s tape touches {placed[j].Id}'s tape.");
                        }
                    }
                }
            }
        }

        Assert.True(holds > 4000);
    }

    [Fact]
    public void OneThingPerScrapInTheOrderGiven()
    {
        var scraps = ScrapFixtures.Mixed(9, 2);
        var placed = ScrapLayout.Place(scraps, ScrapFixtures.WellWidthFor(393));

        Assert.Equal(scraps.Select(s => s.Id), placed.Select(p => p.Id));
        Assert.Equal(scraps.Select(s => s.Kind), placed.Select(p => p.Kind));
        Assert.Equal(placed.Select(p => p.Y).OrderBy(y => y), placed.Select(p => p.Y));
    }

    [Fact]
    public void ScrapsSitInTheWellWithTheMockupsPaddingAndMargins()
    {
        // .dv-well padding 28px 18px 0 24px, right:66px; .scrap margin 22px 2px 6px.
        var wellWidth = ScrapLayout.WellWidthDp(393);
        Assert.Equal(393 - 66, wellWidth);

        var placed = ScrapLayout.Place([new Scrap(ScrapKind.IndexCard, "only", 100)], wellWidth);

        Assert.Equal(24 + 2, placed[0].X);
        Assert.Equal(wellWidth - 24 - 18 - 4, placed[0].Width);
        Assert.Equal(22, placed[0].Y);
    }

    [Fact]
    public void ABoxIsItsContentPlusItsPaddingAndBorder()
    {
        // Index card and receipt: padding 14/12 (card 9 on top) and a 1px border all round.
        var card = ScrapLayout.Place([new Scrap(ScrapKind.IndexCard, "c", 100)], 327)[0];
        var receipt = ScrapLayout.Place([new Scrap(ScrapKind.Receipt, "r", 100)], 327)[0];
        // Magazine clipping and chart scrap have no border: padding 16/14 and 10/16.
        var magazine = ScrapLayout.Place([new Scrap(ScrapKind.MagazineClipping, "m", 100)], 327)[0];
        var chart = ScrapLayout.Place([new Scrap(ScrapKind.ChartScrap, "g", 100)], 327)[0];
        // Notepad leaf: padding-top 16, bottom 12, border on three sides only.
        var note = ScrapLayout.Place([new Scrap(ScrapKind.NotepadLeaf, "n", 100)], 327)[0];

        Assert.Equal(100 + 9 + 12 + 2, card.Height);
        Assert.Equal(100 + 14 + 12 + 2, receipt.Height);
        Assert.Equal(100 + 16 + 14, magazine.Height);
        Assert.Equal(100 + 10 + 16, chart.Height);
        Assert.Equal(100 + 16 + 12 + 1, note.Height);
    }

    [Fact]
    public void GapsAreTheCollapsedMarginsOfTheMockup()
    {
        // Adjacent block margins collapse to the larger: 6 below, 22 above -> 22; a chart scrap
        // asks only 18 above, so 6 vs 18 -> 18.
        var placed = ScrapLayout.Place(
        [
            new Scrap(ScrapKind.IndexCard, "a", 80),
            new Scrap(ScrapKind.Receipt, "b", 60),
            new Scrap(ScrapKind.ChartScrap, "c", 90),
        ], 327);

        Assert.Equal(22, placed[1].Y - (placed[0].Y + placed[0].Height));
        Assert.Equal(18, placed[2].Y - (placed[1].Y + placed[1].Height));
    }

    [Fact]
    public void TheStackReportsItsHeightForTheScrollView()
    {
        var placed = ScrapLayout.Place(
        [
            new Scrap(ScrapKind.IndexCard, "a", 80),
            new Scrap(ScrapKind.Receipt, "b", 60),
        ], 327);

        Assert.Equal(placed[1].Y + placed[1].Height + 6, ScrapLayout.StackHeightDp(placed));
        Assert.Equal(0, ScrapLayout.StackHeightDp([]));
    }

    [Fact]
    public void AnEmptyPageHasNoScraps()
    {
        Assert.Empty(ScrapLayout.Place([], 327));
    }

    [Fact]
    public void BadInputIsRefusedWithAReasonRatherThanLaidOutWrongly()
    {
        var duplicate = new[] { new Scrap(ScrapKind.IndexCard, "same", 50), new Scrap(ScrapKind.Receipt, "same", 50) };
        var duplicateError = Assert.Throws<ArgumentException>(() => ScrapLayout.Place(duplicate, 327));
        Assert.Contains("same", duplicateError.Message);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScrapLayout.Place([new Scrap(ScrapKind.IndexCard, "x", -1)], 327));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScrapLayout.Place([new Scrap(ScrapKind.IndexCard, "x", double.NaN)], 327));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScrapLayout.Place([new Scrap(ScrapKind.IndexCard, "x", 50)], 40));
        Assert.Throws<ArgumentException>(() =>
            ScrapLayout.Place([new Scrap(ScrapKind.IndexCard, " ", 50)], 327));
    }

    [Fact]
    public void TheBinderGutterIsTheSuitesOwn()
    {
        // The 66 dp gutter lives in Codex.Ui.Android's BinderTabs (Codex.Domain's BinderTabLayout
        // carries tab heights only), and Domain cannot reference Android code, so the copy here is
        // held equal by reading that source.
        var repository = Directory.GetParent(ConstitutionSource.Root())!.FullName;
        var source = File.ReadAllText(Path.Combine(repository, "FocusPhone", "Codex.Ui.Android", "BinderTabs.cs"));

        Assert.Contains($"public const int WidthDp = {ScrapMetrics.BinderGutterDp};", source);
    }
}
