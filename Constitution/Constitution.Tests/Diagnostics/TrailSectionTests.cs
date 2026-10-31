using Constitution.Domain.Diagnostics;

namespace Constitution.Tests.Diagnostics;

/// <summary>Review S8: the export carries the last of each trail, filtered, under its own heading.</summary>
public class TrailSectionTests
{
    private const string Stamp = "[Oct 18, 9:05 am] ";

    private static DiagnosticsSnapshot Snapshot(params TrailSection[] trails) =>
        new(AppVersion.Parse("1.0.57"), InstallRole.Owner, new DiagnosticsCounts(0, 0, 0, []), [], [], [], trails);

    private static TrailSection Section(TrailKind kind, params string[] lines) =>
        new(kind, lines.Select(line => TrailLine.From(Stamp + "INFO Body — " + line) ?? throw new InvalidOperationException("test line withheld")));

    [Fact]
    public void TheExportCarriesEachTrailUnderItsHeading()
    {
        var export = DiagnosticsRenderer.Render(Snapshot(
            Section(TrailKind.Body, "body read steps: ok, 2 readings", "sleepiq sign-in: 200"),
            Section(TrailKind.Vault, "vault restore skipped: no passphrase")));

        Assert.Contains("\nTrails\n", export);
        Assert.Contains("  Body trail\n", export);
        Assert.Contains("  Vault trail\n", export);
        Assert.Contains("    " + Stamp + "INFO Body — body read steps: ok, 2 readings\n", export);
        Assert.Contains("sleepiq sign-in: 200", export);
        Assert.True(
            export.IndexOf("Body trail", StringComparison.Ordinal) < export.IndexOf("Vault trail", StringComparison.Ordinal),
            "The Body trail comes before the vault's.");
    }

    [Fact]
    public void ATrailWithNothingInItSaysSo()
    {
        var export = DiagnosticsRenderer.Render(Snapshot(new TrailSection(TrailKind.Body, [])));

        Assert.Contains("  Body trail\n    Nothing recorded yet\n", export);
    }

    [Fact]
    public void NoTrailsAtAllStillRendersTheHeadingOnce()
    {
        var export = DiagnosticsRenderer.Render(Snapshot());

        Assert.Contains("\nTrails\n  Nothing recorded yet\n", export);
        Assert.Equal(1, export.Split("\nTrails\n").Length - 1);
    }

    [Fact]
    public void ATrailMayAppearOnce()
    {
        Assert.Throws<ArgumentException>(() => Snapshot(Section(TrailKind.Body, "body install day recorded"), Section(TrailKind.Body, "body install day recorded")));
    }

    [Fact]
    public void ASectionHoldsAtMostTheLastForty()
    {
        var lines = Enumerable.Range(0, TrailLines.MostRecent + 1)
            .Select(n => TrailLine.From($"{Stamp}INFO Body — body read steps: ok, {n} readings")!);

        Assert.Throws<ArgumentException>(() => new TrailSection(TrailKind.Body, lines));
    }

    [Fact]
    public void EveryKindHasAHeadingPhrase()
    {
        foreach (var kind in Enum.GetValues<TrailKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(DiagnosticsPhrases.Of(kind)));
            Assert.Empty(PrivacyScan.Find(DiagnosticsPhrases.Of(kind)));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticsPhrases.Of((TrailKind)99));
    }
}
