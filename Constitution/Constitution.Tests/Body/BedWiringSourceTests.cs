namespace Constitution.Tests.Body;

/// <summary>
/// Review S1 and S7 on the device wiring, which the suite cannot load and so reads: the filler is built with the
/// standing of the login, and the standing reads the same judged-against-the-replacement health Settings reads.
/// </summary>
public class BedWiringSourceTests
{
    private static string Head(string relative)
    {
        var file = ConstitutionSource.Files(".cs")
            .SingleOrDefault(candidate => ConstitutionSource.Relative(candidate) == "Constitution.Android/" + relative);
        Assert.True(file is not null, $"Constitution.Android/{relative} was not found.");
        return File.ReadAllText(file!);
    }

    [Fact]
    public void TheFillerIsBuiltWithTheStandingOfTheLogin()
    {
        var text = Head("Body/BodyServices.cs");

        Assert.Contains("BedSleepGapFiller.Create(", text, StringComparison.Ordinal);
        Assert.Contains("new BedLoginStanding(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStandingIsTheLoginsHealthJudgedAgainstItsLastSave()
    {
        var text = Head("Body/BedLoginStanding.cs");

        Assert.Contains("ISignInStanding", text, StringComparison.Ordinal);
        Assert.Contains("ConstitutionHealthReport.HealthOf(", text, StringComparison.Ordinal);
        Assert.Contains("CredentialIds.SleepNumber", text, StringComparison.Ordinal);
        Assert.Contains("SavedAt(", text, StringComparison.Ordinal);
        Assert.Contains("ConnectionHealth.Rejected", text, StringComparison.Ordinal);
    }
}
