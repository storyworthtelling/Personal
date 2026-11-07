using System.Text.Json;
using Storyworthtelling.Voice.Secrets;
using Storyworthtelling.Voice.Tests.Support;

namespace Storyworthtelling.Voice.Tests.Secrets;

// Contracts H1, error-handling §3. System.Text.Json drops properties it does not know, so an app built before a
// vault section or field existed loaded the vault, merged its own section and wrote it back without the newer
// ones: an older Flyleaf could erase Pulp's and Constitution's keys from Drive. Every vault record now keeps what
// it does not know, the vault carries a schema number that an older app refuses to write over, and an install
// that has found the vault before does not create a second one when a read comes back with nothing.
public class VaultSchemaTests
{
    private const string Pass = "invented sample passphrase";

    [Fact]
    public async Task ASectionAndAFieldThisBuildDoesNotKnowSurviveAPublish()
    {
        var drive = new VaultPublishSealedTests.OwnedDrive();
        var written = """
            {
              "Claude": { "ApiKey": "sk-kept", "Tier": "future-field" },
              "FutureApp": { "Token": "kept-too" },
              "Schema": 1
            }
            """;
        await drive.WriteAsync(VaultEnvelope.Write(written, Pass), default);

        var result = await new SecretsVaultService(new DriveSecretsVaultStore(drive), () => Pass)
            .PublishAsync(v => v with { Gemini = new GeminiSecrets("g") }, default);

        var after = JsonDocument.Parse(VaultEnvelope.Read(drive.Inner.Content, Pass).Plaintext!).RootElement;
        Assert.Equal(SecretsVaultPublishStatus.Published, result.Status);
        Assert.Equal("kept-too", Property(Property(after, "FutureApp"), "Token").GetString());
        Assert.Equal("future-field", Property(Property(after, "Claude"), "Tier").GetString());
        Assert.Equal("g", Property(Property(after, "Gemini"), "ApiKey").GetString());
    }

    [Fact]
    public async Task APersonalVaultFieldThisBuildDoesNotKnowSurvivesAPublish()
    {
        var drive = new VaultPublishSealedTests.OwnedDrive();
        await drive.WriteAsync(VaultEnvelope.Write("""{ "SleeperSide": "left", "FutureChoice": "kept" }""", Pass), default);

        var published = await new PersonalVaultAccess(new DrivePersonalVaultStore(drive), () => Pass)
            .PublishAsync(v => v with { OwnerColumn = "Sam" }, default);

        var after = JsonDocument.Parse(VaultEnvelope.Read(drive.Inner.Content, Pass).Plaintext!).RootElement;
        Assert.True(published);
        Assert.Equal("kept", Property(after, "FutureChoice").GetString());
    }

    [Fact]
    public async Task AVaultANewerAppWroteIsNotWrittenOver()
    {
        var drive = new VaultPublishSealedTests.OwnedDrive();
        await drive.WriteAsync(VaultEnvelope.Write("""{ "Schema": 99, "Claude": { "ApiKey": "sk-newer" } }""", Pass), default);
        var before = drive.Inner.Content;

        var store = new DriveSecretsVaultStore(drive);
        var loaded = await store.LoadAsync(Pass, default);
        var result = await new SecretsVaultService(store, () => Pass).PublishAsync(v => v with { Gemini = new GeminiSecrets("g") }, default);

        Assert.True(loaded.IsNewerFormat);
        Assert.Equal(SecretsVaultPublishStatus.VaultUnreadable, result.Status);
        Assert.Contains("newer", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, drive.Inner.Content);
    }

    [Fact]
    public void ThisBuildWritesItsSchema()
    {
        var json = JsonSerializer.Serialize(SecretsVault.Empty, VaultEnvelope.SerializerOptions);

        Assert.Equal(SecretsVault.CurrentSchema, JsonDocument.Parse(json).RootElement.GetProperty("Schema").GetInt32());
    }

    [Fact]
    public async Task AnInstallThatHasFoundTheVaultBeforeDoesNotCreateASecondOne()
    {
        // The id this install remembered no longer resolves and a name search finds nothing (a search miss,
        // a scope change): writing would create a second vault the others never see.
        var drive = new FakeDriveHandler();
        var ids = new InMemoryDriveFileIdStore(new Dictionary<string, string> { [DriveSecretsVaultStore.FileName] = "id-gone" });
        var file = new DriveTextFile(_ => Task.FromResult(drive.Service()), DriveSecretsVaultStore.FileName, ids);

        var written = await new SealedDriveFile(file).WriteAsync("{}", Pass, await new SealedDriveFile(file).ReadAsync(Pass, default), SealedWriteOptions.Default, default);

        Assert.Equal(SealedWriteOutcome.Refused, written.Outcome);
        Assert.Empty(drive.Files);
    }

    [Fact]
    public async Task AnInstallThatHasNeverSeenTheVaultMayCreateIt()
    {
        var drive = new FakeDriveHandler();
        var file = new DriveTextFile(_ => Task.FromResult(drive.Service()), DriveSecretsVaultStore.FileName, new InMemoryDriveFileIdStore());

        var written = await new SealedDriveFile(file).WriteAsync("{}", Pass, await new SealedDriveFile(file).ReadAsync(Pass, default), SealedWriteOptions.Default, default);

        Assert.Equal(SealedWriteOutcome.Written, written.Outcome);
        Assert.Single(drive.Files);
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.EnumerateObject().First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
