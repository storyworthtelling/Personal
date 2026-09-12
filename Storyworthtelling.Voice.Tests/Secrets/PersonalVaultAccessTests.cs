using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// The per-person vault's read-and-write rules, tested for the first time.
///
/// <para>They were written in an Android UI library, where nothing could reach them — which is why
/// the rule that matters most here had never been exercised: <b>it refuses to write a vault it
/// could not read.</b> A mistyped passphrase must not replace a good file with a nearly-empty one,
/// and this file has no second copy anywhere.</para>
/// </summary>
public class PersonalVaultAccessTests
{
    private const string Passphrase = "the one thing not in the vault";

    private sealed class InMemoryDrive : IDriveTextFile
    {
        public string? Content { get; set; }

        public int Writes { get; private set; }

        public bool Throws { get; set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken) =>
            Throws
                ? Task.FromException<string?>(new HttpRequestException("no scope"))
                : Task.FromResult(Content);

        public Task WriteAsync(string content, CancellationToken cancellationToken)
        {
            Content = content;
            Writes++;
            return Task.CompletedTask;
        }
    }

    private static (PersonalVaultAccess Access, InMemoryDrive Drive) Build(
        string? passphrase = Passphrase)
    {
        var drive = new InMemoryDrive();
        return (new PersonalVaultAccess(new DrivePersonalVaultStore(drive), () => passphrase), drive);
    }

    [Fact]
    public async Task WhatItPublishesItLoadsBack()
    {
        var (access, _) = Build();

        Assert.True(await access.PublishAsync(
            vault => vault with { TidalAccount = new TidalAccount("me", "token") },
            CancellationToken.None));

        var loaded = await access.LoadAsync(CancellationToken.None);

        Assert.Equal(PersonalVaultStatus.Loaded, loaded.Status);
        Assert.Equal("token", loaded.Vault.TidalAccount.RefreshToken);
    }

    /// <summary>
    /// A merge, not a replace. The vault holds more than one thing now, and a publish that wrote
    /// only what its caller knew about would quietly drop whatever the other caller had put there.
    /// </summary>
    [Fact]
    public async Task PublishingOneThingKeepsTheRest()
    {
        var (access, _) = Build();

        await access.PublishAsync(
            vault => vault with { TidalAccount = new TidalAccount("me", "token") },
            CancellationToken.None);

        await access.PublishAsync(
            vault => vault with { OwnerColumn = "Jordan" }, CancellationToken.None);

        var loaded = await access.LoadAsync(CancellationToken.None);

        Assert.Equal("token", loaded.Vault.TidalAccount.RefreshToken);
        Assert.Equal("Jordan", loaded.Vault.OwnerColumn);
    }

    /// <summary>
    /// The rule that had never been tested. A wrong passphrase reads as a wrong passphrase and the
    /// publish is refused — the file on Drive is untouched, because there is no second copy of it.
    /// </summary>
    [Fact]
    public async Task ItRefusesToWriteAVaultItCouldNotRead()
    {
        var (owner, drive) = Build();
        await owner.PublishAsync(
            vault => vault with { OwnerColumn = "Rowan" }, CancellationToken.None);

        var onDrive = drive.Content;
        var writes = drive.Writes;

        var wrong = new PersonalVaultAccess(
            new DrivePersonalVaultStore(drive), () => "not the passphrase");

        Assert.False(await wrong.PublishAsync(
            vault => vault with { OwnerColumn = "Jordan" }, CancellationToken.None));

        Assert.Equal(onDrive, drive.Content);
        Assert.Equal(writes, drive.Writes);
    }

    [Fact]
    public async Task AWrongPassphraseSaysSoRatherThanReturningAnEmptyVault()
    {
        var (owner, drive) = Build();
        await owner.PublishAsync(
            vault => vault with { OwnerColumn = "Rowan" }, CancellationToken.None);

        var wrong = new PersonalVaultAccess(
            new DrivePersonalVaultStore(drive), () => "not the passphrase");

        Assert.Equal(
            PersonalVaultStatus.WrongPassphrase,
            (await wrong.LoadAsync(CancellationToken.None)).Status);
    }

    /// <summary>
    /// No passphrase is not a failure — it is a phone whose passphrase a factory reset wiped. It
    /// reads as empty and writes nothing, and the head carries on from its local settings.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WithoutAPassphraseNothingIsReadAndNothingIsWritten(string? passphrase)
    {
        var (access, drive) = Build(passphrase);

        Assert.Equal(
            PersonalVaultStatus.Empty,
            (await access.LoadAsync(CancellationToken.None)).Status);

        Assert.False(await access.PublishAsync(
            vault => vault with { OwnerColumn = "Jordan" }, CancellationToken.None));

        Assert.Null(drive.Content);
        Assert.Equal(0, drive.Writes);
    }

    /// <summary>
    /// No Drive scope and no network are the same answer: empty, and the head keeps its local copy.
    /// An exception out of here would take a launch down over a backup.
    /// </summary>
    [Fact]
    public async Task ADriveThatWillNotAnswerReadsAsEmptyRatherThanThrowing()
    {
        var (access, drive) = Build();
        drive.Throws = true;

        Assert.Equal(
            PersonalVaultStatus.Empty,
            (await access.LoadAsync(CancellationToken.None)).Status);

        Assert.False(await access.PublishAsync(
            vault => vault with { OwnerColumn = "Jordan" }, CancellationToken.None));
    }

    /// <summary>
    /// A first run has no file, which must still be writable or nothing is ever backed up.
    /// </summary>
    [Fact]
    public async Task AnAbsentFileIsEmptyAndCanBeWrittenTo()
    {
        var (access, drive) = Build();

        Assert.Equal(
            PersonalVaultStatus.Empty,
            (await access.LoadAsync(CancellationToken.None)).Status);

        Assert.True(await access.PublishAsync(
            vault => vault with { OwnerColumn = "Jordan" }, CancellationToken.None));

        Assert.NotNull(drive.Content);
    }

    /// <summary>
    /// And what sits on Drive is ciphertext. This file carries a sign-in token and, now, a photo of
    /// somebody's face.
    /// </summary>
    [Fact]
    public async Task WhatSitsOnDriveIsCiphertext()
    {
        var (access, drive) = Build();

        await access.PublishAsync(
            vault => vault with { TidalAccount = new TidalAccount("me", "the-refresh-token") },
            CancellationToken.None);

        Assert.NotNull(drive.Content);
        Assert.DoesNotContain("the-refresh-token", drive.Content!, StringComparison.Ordinal);
    }
}
