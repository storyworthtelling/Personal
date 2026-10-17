using System.Text.RegularExpressions;
using Constitution.Domain.Scraps;

namespace Constitution.Tests.Scrapbook;

/// <summary>
/// C-3.2.3: each kind of scrap has exactly one thin painter, and the registry knows it. The
/// painters live in the Android head, which this suite does not load, so the guard reads their
/// source: a painter is a class implementing <c>IScrapPainter</c> that declares which kind it draws
/// as <c>Kind =&gt; ScrapKind.X</c>. A new <see cref="ScrapKind"/> with no painter, or two
/// painters claiming one kind, fails here instead of at the first screen that draws it.
/// </summary>
public class ScrapPainterRegistryTests
{
    private sealed record Painter(string File, string Class, string Kind);

    private static readonly Regex Declaration = new(
        @"class\s+(?<name>\w+)[^{;]*?\bIScrapPainter\b",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex KindMember = new(
        @"\bScrapKind\s+Kind\s*=>\s*ScrapKind\.(?<kind>\w+)\s*;",
        RegexOptions.CultureInvariant);

    private static IEnumerable<Painter> PaintersIn(string path, string text)
    {
        foreach (Match declaration in Declaration.Matches(text))
        {
            var kind = KindMember.Match(text, declaration.Index);
            yield return new Painter(path, declaration.Groups["name"].Value,
                kind.Success ? kind.Groups["kind"].Value : "(no Kind member)");
        }
    }

    private static List<Painter> RealPainters() =>
        ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/Scrapbook/", StringComparison.Ordinal))
            .SelectMany(file => PaintersIn(ConstitutionSource.Relative(file), File.ReadAllText(file)))
            .ToList();

    public static IEnumerable<object[]> Kinds() => Enum.GetValues<ScrapKind>().Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryKindHasExactlyOnePainter(ScrapKind kind)
    {
        var claiming = RealPainters().Where(p => p.Kind == kind.ToString()).ToList();

        Assert.True(claiming.Count == 1,
            $"{kind} has {claiming.Count} painters ({string.Join(", ", claiming.Select(p => p.Class))}); it needs exactly one.");
    }

    [Fact]
    public void NoPainterDrawsAKindThatDoesNotExist()
    {
        var known = Enum.GetNames<ScrapKind>();

        Assert.All(RealPainters(), p => Assert.Contains(p.Kind, known));
    }

    [Fact]
    public void TheRegistryListsEveryPainterOnce()
    {
        var registry = ConstitutionSource.Files(".cs")
            .Single(file => Path.GetFileName(file) == "ScrapPainters.cs");
        var text = File.ReadAllText(registry);

        foreach (var painter in RealPainters())
        {
            var listed = Regex.Matches(text, @"new\s+" + painter.Class + @"\s*\(").Count;
            Assert.True(listed == 1, $"{painter.Class} is listed {listed} times in the registry; it should be once.");
        }
    }

    [Fact]
    public void AGuardedKindWithTwoPaintersIsCaught()
    {
        const string one = "public sealed class AClippingPainter : IScrapPainter { public ScrapKind Kind => ScrapKind.MagazineClipping; }";
        const string two = "public sealed class AnotherClippingPainter : IScrapPainter { public ScrapKind Kind => ScrapKind.MagazineClipping; }";

        var found = PaintersIn("a.cs", one).Concat(PaintersIn("b.cs", two)).ToList();

        Assert.Equal(2, found.Count(p => p.Kind == nameof(ScrapKind.MagazineClipping)));
    }

    [Fact]
    public void APainterWithNoKindMemberIsReportedAsSuch()
    {
        var found = PaintersIn("a.cs", "public sealed class Lazy : IScrapPainter { }").ToList();

        Assert.Single(found);
        Assert.Equal("(no Kind member)", found[0].Kind);
    }

    [Fact]
    public void AClassThatIsNotAPainterIsIgnored()
    {
        Assert.Empty(PaintersIn("a.cs", "public sealed class TapePainter { public ScrapKind Kind => ScrapKind.Receipt; }"));
    }
}
