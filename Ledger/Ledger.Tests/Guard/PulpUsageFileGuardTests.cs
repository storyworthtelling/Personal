using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard G16, the file's half (Pulp D320): Index's reader against Pulp's writer, by content, so a rename of the
/// file or a field on one side fails here instead of her install quietly showing no line. And the install rule:
/// the usage read is the household's own passphrase-protected file and nothing of the owner's — no Plaid, no vault section.
/// </summary>
public class PulpUsageFileGuardTests
{
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    [Fact]
    public void BothSidesNameTheSameFileAndTheSameFields()
    {
        var pulp = Read("Pulp", "Pulp.Domain", "Usage", "IndexUsageFile.cs");
        var index = Read("Ledger", "Ledger.Domain", "Usage", "PulpUsageFile.cs");
        var source = Read("Ledger", "Ledger.Infrastructure", "Usage", "DrivePulpUsageSource.cs");

        Assert.Contains("FileName = \"storyworthtelling-pulp-usage.json\"", pulp, StringComparison.Ordinal);
        Assert.Contains("FileName = \"storyworthtelling-pulp-usage.json\"", source, StringComparison.Ordinal);
        foreach (var field in new[] { "version", "entries", "service", "month", "count", "lastOpened", "merchants" })
        {
            Assert.Contains($"\"{field}\"", pulp, StringComparison.Ordinal);
            Assert.Contains($"\"{field}\"", index, StringComparison.Ordinal);
        }

        Assert.Contains("CurrentVersion = 1", pulp, StringComparison.Ordinal);
        Assert.Contains("CurrentVersion = 1", index, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUsageReadTouchesNeitherPlaidNorTheOwnersVaultSection()
    {
        var source = Read("Ledger", "Ledger.Infrastructure", "Usage", "DrivePulpUsageSource.cs");

        foreach (var forbidden in new[] { "Plaid", "SecretsVaultService", "ISecretsVaultStore", "DriveSecretsVaultStore", "VaultSync", "Institution" })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheUsageFileGetsItsOwnDriveFileNotTheBareSharedOne()
    {
        var program = Read("Ledger", "Ledger.Android", "MauiProgram.cs");

        Assert.Contains("DrivePulpUsageSource.FileName", program, StringComparison.Ordinal);
    }
}
