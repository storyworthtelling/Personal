using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Diagnostics.Tests;

/// <summary>
/// Audit 08 §6–8, §28: every vault restore and publish was swallowed, so after a factory reset a mistyped passphrase
/// looked exactly like "nothing to restore". One line per outcome, a warning for anything that did not land, and never
/// a value or an exception message (a message can quote a path or a key name).
/// </summary>
public class VaultTrailTests
{
    [Theory]
    [InlineData("Loaded", false)]
    [InlineData("NoPassphrase", false)]
    [InlineData("Empty", true)]
    [InlineData("WrongPassphrase", true)]
    [InlineData("Corrupt", true)]
    [InlineData("Unreachable", true)]
    public void ARestoreThatDidNotOpenIsAProblem(string status, bool problem)
    {
        var (isProblem, line) = VaultTrail.Restored(VaultTrail.Shared, status);

        Assert.Equal(problem, isProblem);
        Assert.Equal($"VAULT shared restore: {status}", line);
    }

    [Theory]
    [InlineData("Published", false)]
    [InlineData("ReadOnly", false)]
    [InlineData("NoPassphrase", false)]
    [InlineData("VaultUnreadable", true)]
    [InlineData("WriteFailed", true)]
    public void APublishThatDidNotLandIsAProblem(string status, bool problem)
    {
        var (isProblem, line) = VaultTrail.Published(VaultTrail.Personal, status);

        Assert.Equal(problem, isProblem);
        Assert.Equal($"VAULT personal publish: {status}", line);
    }

    [Fact]
    public void AFailureNamesItsTypesNeverItsMessage()
    {
        var failure = new InvalidOperationException("token ya29.example-secret", new HttpRequestException("403 for /drive/v3"));

        var (problem, line) = VaultTrail.Restored(VaultTrail.Shared, "Unreachable", failure);

        Assert.True(problem);
        Assert.Equal("VAULT shared restore: Unreachable (System.InvalidOperationException < System.Net.Http.HttpRequestException)", line);
    }

    [Fact]
    public void AFailedPublishCarriesItsOutcomeWordBesideTheTypes()
    {
        var failure = new IOException("The file couldn’t be written: token ya29.example-secret",
            new HttpRequestException("403 for /drive/v3/files/example"));

        var (problem, line) = VaultTrail.Published(VaultTrail.Shared, "WriteFailed", failure, "Failed");

        Assert.True(problem);
        Assert.Equal("VAULT shared publish: WriteFailed [Failed] (System.IO.IOException < System.Net.Http.HttpRequestException)", line);
        Assert.DoesNotContain("ya29", line);
        Assert.DoesNotContain("403", line);
    }

    [Theory]
    [InlineData("Drive said no: 403 for /drive/v3")]
    [InlineData("two words")]
    [InlineData("")]
    public void ADetailThatIsNotOneWordIsLeftOut(string detail)
    {
        var (_, line) = VaultTrail.Published(VaultTrail.Personal, "WriteFailed", detail: detail);

        Assert.Equal("VAULT personal publish: WriteFailed", line);
    }

    [Fact]
    public void ARestoreCanCarryADetailWordToo()
    {
        var (_, line) = VaultTrail.Restored(VaultTrail.Shared, "Corrupt", detail: "NewerFormat");

        Assert.Equal("VAULT shared restore: Corrupt [NewerFormat]", line);
    }
}
