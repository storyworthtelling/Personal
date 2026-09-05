using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

public class DrivePersonalVaultStoreTests
{
    private sealed class InMemoryTextFile : IDriveTextFile
    {
        public string? Content { get; set; }
        public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Content);
        public Task WriteAsync(string content, CancellationToken cancellationToken)
        {
            Content = content;
            return Task.CompletedTask;
        }
    }

    private const string Pass = "vault-passphrase";

    private static PersonalVault Signed(string user, string token) =>
        PersonalVault.Empty with { TidalAccount = new TidalAccount(user, token) };

    [Fact]
    public async Task RoundTripsThroughTheEnvelope()
    {
        var file = new InMemoryTextFile();
        var store = new DrivePersonalVaultStore(file);

        await store.SaveAsync(Signed("user-1", "refresh-ghi"), Pass, default);
        var loaded = await store.LoadAsync(Pass, default);

        Assert.Equal(PersonalVaultStatus.Loaded, loaded.Status);
        Assert.Equal("user-1", loaded.Vault.TidalAccount.UserId);
        Assert.Equal("refresh-ghi", loaded.Vault.TidalAccount.RefreshToken);
    }

    // Same guarantee the shared vault gives: what reaches Drive is opaque. A personal
    // vault holds someone's subscription token, so a plaintext write would be worse
    // here than in the shared file, not better.
    [Fact]
    public async Task WhatReachesDriveIsEncrypted()
    {
        var file = new InMemoryTextFile();

        await new DrivePersonalVaultStore(file).SaveAsync(Signed("user-1", "refresh-ghi"), Pass, default);

        Assert.NotNull(file.Content);
        Assert.DoesNotContain("refresh-ghi", file.Content);
        Assert.DoesNotContain("user-1", file.Content);
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_WhenNoFileYet()
    {
        Assert.Equal(
            PersonalVaultStatus.Empty,
            (await new DrivePersonalVaultStore(new InMemoryTextFile { Content = null }).LoadAsync(Pass, default)).Status);

        Assert.Equal(
            PersonalVaultStatus.Empty,
            (await new DrivePersonalVaultStore(new InMemoryTextFile { Content = "   " }).LoadAsync(Pass, default)).Status);
    }

    [Fact]
    public async Task LoadAsync_ReturnsCorrupt_WhenNotAnEnvelope()
    {
        Assert.Equal(
            PersonalVaultStatus.Corrupt,
            (await new DrivePersonalVaultStore(new InMemoryTextFile { Content = "not json at all" }).LoadAsync(Pass, default)).Status);
    }

    // The two vaults share a passphrase, so a wrong one has to be told apart from a
    // corrupt file here exactly as it is there — otherwise a mistyped passphrase reads
    // as "your sign-in is gone" and somebody pairs their phone again for no reason.
    [Fact]
    public async Task LoadAsync_ReturnsWrongPassphrase_WhenThePassphraseIsWrong()
    {
        var file = new InMemoryTextFile();
        await new DrivePersonalVaultStore(file).SaveAsync(Signed("user-1", "refresh-ghi"), Pass, default);

        var loaded = await new DrivePersonalVaultStore(file).LoadAsync("not-the-passphrase", default);

        Assert.Equal(PersonalVaultStatus.WrongPassphrase, loaded.Status);
    }

    // One file per person, in that person's own Drive. The name is the same in both
    // Drives on purpose — it is found by name, and two people's files never meet.
    [Fact]
    public void TheFileNameIsDistinctFromTheSharedVault()
    {
        Assert.NotEqual(DriveSecretsVaultStore.FileName, DrivePersonalVaultStore.FileName);
    }
}
