using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Diagnostics.Tests;

/// <summary>
/// Audit 08 §31: the same kind of line had a different source in each app ("vault" in Pulp, "Vault" elsewhere, a contract
/// under its app's name), so a fault could not be grouped across apps. One vocabulary; a source is one word the reader
/// can split on.
/// </summary>
public class LogSourcesTests
{
    [Fact]
    public void TheSharedWordsAreFixed()
    {
        Assert.Equal("Vault", LogSources.Vault);
        Assert.Equal("Drive", LogSources.Drive);
        Assert.Equal("Worker", LogSources.Worker);
        Assert.Equal("Permission", LogSources.Permission);
        Assert.Equal("Crash", LogSources.Crash);
        Assert.Equal("Exit", LogSources.Exit);
    }

    [Fact]
    public void AServiceAndAContractAreNamedWithinTheirKind()
    {
        Assert.Equal("Http:plaid", LogSources.Http("plaid"));
        Assert.Equal("Contract:usage", LogSources.Contract("usage"));
    }

    [Fact]
    public void ASourceIsOneWord() =>
        Assert.Equal("Contract:hidden-apps", LogSources.Contract("hidden apps"));
}
