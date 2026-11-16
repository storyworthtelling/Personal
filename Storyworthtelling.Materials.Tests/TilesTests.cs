namespace Storyworthtelling.Materials.Tests;

/// <summary>
/// The approved materials table (DESIGN.md §2, W8.8) as numbers. Each tile is checked against what the table
/// says it does, not against a copy of the code that makes it.
/// </summary>
public class TilesTests
{
    [Fact]
    public void ThePaperGrainIsOneFleckInSixAtTheGrainAlpha()
    {
        var tile = PaperGrainTile.Build();

        Assert.Equal(Grain.TileSize, tile.Size);
        Assert.All(tile.Values, value => Assert.True(value is 0 or Grain.Alpha));
        var flecks = tile.Values.Count(value => value == Grain.Alpha) / (double)tile.Values.Length;
        Assert.InRange(flecks, 1.0 / Grain.Density - 0.01, 1.0 / Grain.Density + 0.01);
    }

    [Fact]
    public void ThePaperGrainIsTheOneTheAppsAndDocumentsHaveAlwaysDrawn()
    {
        // The first flecks of the seeded tile, as IconStudio's paper_grain.png and every document have carried them
        // since the grain was introduced: moving the generator here must not move the paper.
        var tile = PaperGrainTile.Build();
        var first = Enumerable.Range(0, 400).Where(i => tile.Values[i] != 0).Take(6).ToArray();

        var state = Grain.Seed;
        var expected = new List<int>();
        for (var i = 0; expected.Count < 6; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            if (state % Grain.Density == 0)
            {
                expected.Add(i);
            }
        }

        Assert.Equal(expected, first);
    }

    [Fact]
    public void ATileIsTheSameEveryTime()
    {
        Assert.Equal(PaperGrainTile.Build().Values, PaperGrainTile.Build().Values);
        Assert.Equal(PencilTooth.Build().Values, PencilTooth.Build().Values);
        Assert.Equal(Streaks.Marker(), Streaks.Marker());
        Assert.Equal(Streaks.Highlighter(), Streaks.Highlighter());
    }

    [Fact]
    public void PencilLosesColourInHalfItsPixelsAndDropsOutInATenth()
    {
        var tile = PencilTooth.Build();
        var count = (double)tile.Values.Length;

        var dropped = tile.Values.Count(value => value == 0) / count;
        var partial = tile.Values.Count(value => value > 0 && value < 255) / count;

        Assert.InRange(dropped, 0.08, 0.12);
        Assert.InRange(partial, 0.40, 0.60);
        Assert.All(tile.Values.Where(value => value is > 0 and < 255),
            value => Assert.InRange(value / 255.0, MaterialStrength.PencilKeepsAtLeast - 0.005, MaterialStrength.PencilKeepsAtMost + 0.005));
    }

    [Fact]
    public void PencilFlakesClusterRatherThanScatter()
    {
        var tile = PencilTooth.Build();
        bool Partial(int x, int y) => tile.At(x, y) is > 0 and < 255;

        var all = 0;
        var besideOne = 0;
        var partialBesideOne = 0;
        for (var y = 0; y < tile.Size; y++)
        {
            for (var x = 0; x < tile.Size; x++)
            {
                all += Partial(x, y) ? 1 : 0;
                if (Partial(x + 1, y + 1))
                {
                    besideOne++;
                    partialBesideOne += Partial(x, y) ? 1 : 0;
                }
            }
        }

        var overall = all / (double)(tile.Size * tile.Size);
        var given = partialBesideOne / (double)besideOne;
        Assert.True(given > overall + 0.05, $"a flake beside a flake {given:P0}, any flake {overall:P0}");
    }

    [Fact]
    public void MarkerStreaksRunWithinAFifthOfFullAndNeverJump()
    {
        var rows = Streaks.Marker();

        Assert.All(rows, value => Assert.InRange(value / 255.0, 1 - MaterialStrength.MarkerStreak - 0.005, 1.0));
        for (var i = 1; i < rows.Length; i++)
        {
            Assert.InRange(Math.Abs(rows[i] - rows[i - 1]) / 255.0, 0, MaterialStrength.MarkerStreak);
        }
        Assert.True(rows.Max() - rows.Min() > 20, "a streak that never varies is a flat fill");
    }

    [Fact]
    public void AMarkerThinsOnlyInTheLastTenthOfItsStroke()
    {
        Assert.Equal(1.0, Streaks.MarkerEnd(0.0));
        Assert.Equal(1.0, Streaks.MarkerEnd(0.9));
        Assert.Equal(1 - MaterialStrength.MarkerEndThins, Streaks.MarkerEnd(1.0), 3);
        Assert.InRange(Streaks.MarkerEnd(0.95), 1 - MaterialStrength.MarkerEndThins, 1.0);
    }

    [Fact]
    public void TheHighlighterAveragesTheWashAndPoolsAtItsEnds()
    {
        var rows = Streaks.Highlighter();

        Assert.InRange(rows.Average(value => (double)value), MaterialStrength.WashAlpha - 4, MaterialStrength.WashAlpha + 4);
        Assert.All(rows, value => Assert.InRange(value / 255.0, MaterialStrength.HighlighterLeast - 0.005, MaterialStrength.HighlighterMost + 0.005));

        Assert.Equal(MaterialStrength.HighlighterPool, Streaks.HighlighterPool(0, 200), 3);
        Assert.Equal(MaterialStrength.HighlighterPool, Streaks.HighlighterPool(199, 200), 3);
        Assert.Equal(0.0, Streaks.HighlighterPool(100, 200));
    }

    [Fact]
    public void TheApprovedStrengthsAreTheTable()
    {
        Assert.Equal(0.3, MaterialStrength.PencilKeepsAtLeast);
        Assert.Equal(0.7, MaterialStrength.PencilKeepsAtMost);
        Assert.Equal(0.1, MaterialStrength.PencilDropsOut);
        Assert.Equal(0.2, MaterialStrength.MarkerStreak);
        Assert.Equal(0.12, MaterialStrength.MarkerEdge);
        Assert.Equal(0.25, MaterialStrength.MarkerEndThins);
        Assert.Equal(77, MaterialStrength.WashAlpha);
        Assert.Equal(0.22, MaterialStrength.HighlighterLeast);
        Assert.Equal(0.38, MaterialStrength.HighlighterMost);
        Assert.Equal(0.45, MaterialStrength.HighlighterPool);
        Assert.Equal(0.5, MaterialStrength.PressLift);
    }
}
