using Pulp.Domain.Settings;

namespace Pulp.Tests.Settings;

/// <summary>
/// D210 in words the owner can attest to: ticking says every adult profile on the service is
/// PIN-locked, and the consequence — children start titles only on ticked services — is stated beside
/// it, so the tick is never read as a preference.
/// </summary>
public class AttestationTextTests
{
    [Fact]
    public void TheStatementNamesTheServiceTheLockAndTheConsequence()
    {
        var statement = AttestationText.Statement("Streamly");

        Assert.Contains("Streamly", statement, StringComparison.Ordinal);
        Assert.Contains("every adult profile", statement, StringComparison.Ordinal);
        Assert.Contains("PIN", statement, StringComparison.Ordinal);
        Assert.Contains("children", statement, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void TheToggleIsTheAttestationItself() =>
        Assert.Equal("Adult profiles are PIN-locked", AttestationText.Toggle);

    [Fact]
    public void AKidsOnlyAppIsToldItNeedsNoTick()
    {
        var line = AttestationText.KidsOnly("Tinytales");

        Assert.Contains("Tinytales", line, StringComparison.Ordinal);
        Assert.Contains("kids-only", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlankNameIsRefused() => Assert.Throws<ArgumentException>(() => AttestationText.Statement(" "));
}
