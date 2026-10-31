using System.Text.RegularExpressions;

namespace Constitution.Tests.MealPlacement;

/// <summary>
/// The meal placement core is pure and clean: it reads no clock, swallows nothing, holds no colour or type-size literal, and
/// names no model. Read by content, like the other guards.
/// </summary>
public class MealPlacementSourceTests
{
    private static IReadOnlyList<string> Sources() =>
        Directory.GetFiles(Path.Combine(ConstitutionSource.Root(), "Constitution.Domain", "Placement"), "Meal*.cs");

    [Fact]
    public void TheGuardReadsTheRealFiles()
    {
        Assert.NotEmpty(Sources());
    }

    [Fact]
    public void NoClockIsRead()
    {
        Assert.All(Sources(), file =>
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("DateTime.Now", text, StringComparison.Ordinal);
            Assert.DoesNotContain("UtcNow", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TimeProvider", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NothingIsSwallowed()
    {
        Assert.All(Sources(), file => Assert.DoesNotMatch(new Regex(@"\bcatch\b"), File.ReadAllText(file)));
    }

    [Fact]
    public void NoColourOrModelIsNamed()
    {
        Assert.All(Sources(), file =>
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(new Regex(@"#[0-9A-Fa-f]{6}\b|0x[0-9A-Fa-f]{6,8}\b"), text);
            Assert.DoesNotContain("claude-", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void NoIdentifierHoldsAnUnderscore()
    {
        Assert.All(Sources(), file => Assert.DoesNotMatch(new Regex(@"\b[A-Za-z0-9]+_[A-Za-z0-9_]+\b"), File.ReadAllText(file)));
    }
}
