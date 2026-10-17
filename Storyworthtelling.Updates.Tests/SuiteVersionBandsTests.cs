namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// The band table itself: who holds which numbering space. Band 1 is the one deliberate
/// shared band (the five apps that shipped under the old run_number schemes); every other
/// band belongs to exactly one app, so a new app can never be refused because of another's.
/// </summary>
public class SuiteVersionBandsTests
{
    private const string ConstitutionPackage = "com.storyworthtelling.constitution";

    [Fact]
    public void BandsAreUnique()
    {
        var table = SuiteVersionBands.All;
        Assert.NotEmpty(table);

        var packages = table.GroupBy(a => a.Package).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(packages.Count == 0, $"A package appears twice: {string.Join(", ", packages)}");

        // Band 1 is the single legacy shared band; every other band must hold exactly one app.
        var shared = table.Where(a => a.Band != 1)
            .GroupBy(a => a.Band)
            .Where(g => g.Count() > 1)
            .Select(g => $"band {g.Key}: {string.Join(", ", g.Select(a => a.Package))}")
            .ToList();

        Assert.True(shared.Count == 0, $"Bands shared beyond the legacy band 1:\n{string.Join("\n", shared)}");

        // Non-vacuous: the rule must actually have bands to judge beyond band 1.
        Assert.True(table.Count(a => a.Band != 1) >= 3);
    }

    [Fact]
    public void ConstitutionIsBandTen()
    {
        var constitution = Assert.Single(SuiteVersionBands.All, a => a.Package == ConstitutionPackage);

        Assert.Equal(new AppVersionBand(ConstitutionPackage, 10, 0), constitution);

        var others = SuiteVersionBands.All.Where(a => a.Band == 10 && a.Package != ConstitutionPackage)
            .Select(a => a.Package).ToList();
        Assert.True(others.Count == 0, $"Band 10 is also used by: {string.Join(", ", others)}");
    }
}
