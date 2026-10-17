using Codex.Domain.Design;
using Constitution.Domain.Ground;

namespace Constitution.Tests.Ground;

/// <summary>
/// The crinkled ground (C-3.2.2, D43): seeded, deterministic, one tear line, and never in the
/// binder gutter.
/// </summary>
public class CrinkleFieldTests
{
    [Fact]
    public void IsDeterministicForASeed()
    {
        var first = CrinkleField.Generate(7, 260, 260);
        var second = CrinkleField.Generate(7, 260, 260);

        Assert.Equal(first.Creases.Count, second.Creases.Count);
        for (var i = 0; i < first.Creases.Count; i++)
        {
            Assert.Equal(first.Creases[i].Points, second.Creases[i].Points);
            Assert.Equal(first.Creases[i].Intensity, second.Creases[i].Intensity);
        }

        Assert.Equal(first.Ticks, second.Ticks);
        Assert.Equal(first.Perforation, second.Perforation);
    }

    [Fact]
    public void DifferentSeedsDiffer()
    {
        var one = CrinkleField.Generate(1, 260, 260);
        var two = CrinkleField.Generate(2, 260, 260);

        Assert.NotEqual(one.Ticks, two.Ticks);
    }

    [Fact]
    public void ATileHasTheMockupsShape()
    {
        var tile = CrinkleField.Generate(3, 260, 260);

        Assert.Equal(7, tile.Creases.Count);
        Assert.InRange(tile.Ticks.Count, 90, 110);
        Assert.All(tile.Creases, crease => Assert.True(crease.Points.Count >= 3));
        Assert.All(tile.Creases.Select(c => c.Intensity)
            .Concat(tile.Ticks.Select(t => t.Intensity)),
            intensity => Assert.InRange(intensity, CrinkleField.MinIntensity, CrinkleField.MaxIntensity));
    }

    [Theory]
    [InlineData(360)]
    [InlineData(393)]
    [InlineData(430)]
    public void StaysOutOfTheGutter(int phoneWidthDp)
    {
        const int height = 800;
        var wellWidth = phoneWidthDp - CrinkleField.GutterWidthDp;

        for (var seed = 0; seed < 25; seed++)
        {
            var field = CrinkleField.ForPage(seed, phoneWidthDp, height);

            foreach (var point in AllPoints(field))
            {
                Assert.InRange(point.X, 0, wellWidth);
                Assert.InRange(point.Y, 0, height);
            }
        }
    }

    [Fact]
    public void TheGutterIsTheBindersOwnWidth()
    {
        Assert.Equal(BinderTabLayout.PreferredHeightDp, CrinkleField.GutterWidthDp);
    }

    [Fact]
    public void HasExactlyOnePerforation()
    {
        for (var seed = 0; seed < 25; seed++)
        {
            var field = CrinkleField.ForWell(seed, 300, 800);

            Assert.NotNull(field.Perforation);
            Assert.InRange(field.Perforation.YFraction, 0.0, 1.0);
        }
    }

    [Fact]
    public void ALargerWellGetsMoreTicksNotStretchedOnes()
    {
        var small = CrinkleField.ForWell(5, 260, 260);
        var large = CrinkleField.ForWell(5, 520, 520);

        Assert.True(large.Ticks.Count > small.Ticks.Count);
    }

    private static IEnumerable<CrinklePoint> AllPoints(CrinkleField field) =>
        field.Creases.SelectMany(c => c.Points)
            .Concat(field.Ticks.SelectMany(t => new[] { t.A, t.B }));
}
