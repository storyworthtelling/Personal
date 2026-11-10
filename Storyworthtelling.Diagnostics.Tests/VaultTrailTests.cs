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
}
