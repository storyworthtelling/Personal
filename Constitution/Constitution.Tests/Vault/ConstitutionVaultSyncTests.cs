using System.Net;
using System.Text.Json;
using Constitution.Infrastructure.Vault;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Tests.Vault;

/// <summary>
/// C-8.2.2 — every connection restored on launch and saved on change, so a reset is a passphrase away.
///
/// <para>The vault stores, the envelope, the crypto and <see cref="SecretsVaultService"/> (and with it
/// <see cref="VaultOwnership"/>) are the real ones, run over an in-memory Drive file; only the network
/// is faked, and every value is invented. Three rules carry the story: restore fills only what is
/// missing; nothing is ever written over a vault that could not be read; and what is one person's
/// stays in that person's own vault.</para>
/// </summary>
public class ConstitutionVaultSyncTests
{
    private static async Task<ConstitutionRestoreResult> Restore(TestPhone phone, string? passphrase = Invented.Passphrase) =>
        await phone.Sync.RestoreAsync(passphrase, default);

    // ---- restore ----

    [Fact]
    public async Task FillsOnlyWhatIsMissing()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);

        // This install already holds a Claude key and Health Connect choices of its own. Everything
        // else is missing, and the vaults hold something different for the two it has.
        phone.Local.ClaudeApiKey = "example-local-claude";
        phone.Local.HealthConnectChoicesJson = "{\"signals\":[\"example-local-choice\"]}";

        var result = await Restore(phone);

        // What was missing came back, each from the vault it belongs to.
        Assert.Equal(Invented.SleepLogin, phone.Local.SleepNumberLogin);
        Assert.Equal(Invented.SleepWord, phone.Local.SleepNumberPassword);
        Assert.Equal(Invented.RemarkableToken, phone.Local.RemarkableDeviceToken);
        Assert.Equal(Invented.GeminiKey, phone.Local.GeminiApiKey);
        Assert.Equal(Invented.WorkSchedule, phone.Local.WorkScheduleJson);

        // What was there is untouched, even though a vault holds a different value for it.
        Assert.Equal("example-local-claude", phone.Local.ClaudeApiKey);
        Assert.Equal("{\"signals\":[\"example-local-choice\"]}", phone.Local.HealthConnectChoicesJson);

        Assert.Equal(
            [
                VaultField.SleepNumberLogin,
                VaultField.SleepNumberPassword,
                VaultField.RemarkableDeviceToken,
                VaultField.GeminiApiKey,
                VaultField.WorkSchedule,
            ],
            result.Filled);
        Assert.Equal(VaultReadOutcome.Loaded, result.Shared);
        Assert.Equal(VaultReadOutcome.Loaded, result.Personal);
    }

    [Fact]
    public async Task AFreshInstallGetsEveryConnectionBack()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);

        var result = await Restore(phone);

        Assert.Equal(Invented.SleepLogin, phone.Local.SleepNumberLogin);
        Assert.Equal(Invented.SleepWord, phone.Local.SleepNumberPassword);
        Assert.Equal(Invented.RemarkableToken, phone.Local.RemarkableDeviceToken);
        Assert.Equal(Invented.ClaudeKey, phone.Local.ClaudeApiKey);
        Assert.Equal(Invented.GeminiKey, phone.Local.GeminiApiKey);
        Assert.Equal(Invented.HealthChoices, phone.Local.HealthConnectChoicesJson);
        Assert.Equal(Invented.WorkSchedule, phone.Local.WorkScheduleJson);
        Assert.Equal(7, result.Filled.Count);
    }

    [Fact]
    public async Task ABlankInTheVaultFillsNothing()
    {
        // A vault written by another app only: Constitution's section and the personal choices are blank.
        var cloud = new TestCloud();
        cloud.SeedShared(SecretsVault.Empty with { OwnerAccount = Invented.OwnerAccount }, Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(PersonalVault.Empty, Invented.Passphrase);

        var result = await Restore(phone);

        Assert.Empty(result.Filled);
        Assert.Equal(string.Empty, phone.Local.SleepNumberLogin);
        Assert.Equal(string.Empty, phone.Local.ClaudeApiKey);
        Assert.Equal(string.Empty, phone.Local.HealthConnectChoicesJson);
        Assert.Equal(VaultReadOutcome.Loaded, result.Shared);
        Assert.Equal(VaultReadOutcome.Loaded, result.Personal);
    }

    [Fact]
    public async Task RestoreWritesNothingAnywhere()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);
        var sharedBefore = cloud.SharedDrive.Content;
        var personalBefore = phone.PersonalDrive.Content;

        await Restore(phone);

        Assert.Equal(0, cloud.SharedDrive.Writes);
        Assert.Equal(0, phone.PersonalDrive.Writes);
        Assert.Equal(sharedBefore, cloud.SharedDrive.Content);
        Assert.Equal(personalBefore, phone.PersonalDrive.Content);
    }

    [Fact]
    public async Task TheOtherAdultRestoresTheHouseholdsConnectionsToo()
    {
        // Reading the shared vault is for everyone; only writing it belongs to the owner.
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(owner: Invented.OwnerAccount), Invented.Passphrase);
        var partner = cloud.NewPhone(Invented.PartnerAccount);

        var result = await Restore(partner);

        Assert.Equal(Invented.SleepLogin, partner.Local.SleepNumberLogin);
        Assert.Equal(Invented.ClaudeKey, partner.Local.ClaudeApiKey);
        Assert.Equal(Invented.RemarkableToken, partner.Local.RemarkableDeviceToken);
        Assert.Equal(VaultReadOutcome.Loaded, result.Shared);
    }

    // ---- a vault that cannot be read fills nothing, and says which way it failed ----

    private static void Break(InMemoryDrive drive, VaultReadOutcome how)
    {
        switch (how)
        {
            case VaultReadOutcome.WrongPassphrase:
                // A real vault, locked with some other passphrase.
                drive.Content = VaultEnvelope.Write("{}", Invented.OtherPassphrase);
                break;
            case VaultReadOutcome.Corrupt:
                drive.Content = "{ this is not an envelope";
                break;
            case VaultReadOutcome.Empty:
                drive.Content = null;
                break;
            case VaultReadOutcome.Unreachable:
                drive.ReadFailure = new HttpRequestException("no network");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(how), how, "Not a way to break a vault.");
        }
    }

    [Theory]
    [InlineData(VaultReadOutcome.WrongPassphrase)]
    [InlineData(VaultReadOutcome.Corrupt)]
    [InlineData(VaultReadOutcome.Empty)]
    [InlineData(VaultReadOutcome.Unreachable)]
    public async Task ASharedVaultItCannotReadFillsNothingFromIt(VaultReadOutcome how)
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        Break(cloud.SharedDrive, how);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);

        var result = await Restore(phone);

        Assert.Equal(how, result.Shared);
        Assert.Equal(VaultReadOutcome.Loaded, result.Personal);
        Assert.Equal(string.Empty, phone.Local.SleepNumberLogin);
        Assert.Equal(string.Empty, phone.Local.ClaudeApiKey);
        Assert.Equal(string.Empty, phone.Local.RemarkableDeviceToken);

        // The personal vault is a separate file and still restores.
        Assert.Equal(Invented.HealthChoices, phone.Local.HealthConnectChoicesJson);
        Assert.Equal([VaultField.HealthConnectChoices, VaultField.WorkSchedule], result.Filled);
    }

    [Theory]
    [InlineData(VaultReadOutcome.WrongPassphrase)]
    [InlineData(VaultReadOutcome.Corrupt)]
    [InlineData(VaultReadOutcome.Empty)]
    [InlineData(VaultReadOutcome.Unreachable)]
    public async Task APersonalVaultItCannotReadFillsNothingFromIt(VaultReadOutcome how)
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);
        Break(phone.PersonalDrive, how);

        var result = await Restore(phone);

        Assert.Equal(how, result.Personal);
        Assert.Equal(VaultReadOutcome.Loaded, result.Shared);
        Assert.Equal(string.Empty, phone.Local.HealthConnectChoicesJson);
        Assert.Equal(string.Empty, phone.Local.WorkScheduleJson);

        // The shared vault is a separate file and still restores.
        Assert.Equal(Invented.SleepLogin, phone.Local.SleepNumberLogin);
        Assert.Equal(5, result.Filled.Count);
    }

    [Fact]
    public async Task OnlyAVaultThatExistsAndWillNotOpenAsksForAttention()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);

        // A first run (nothing on Drive yet) and an offline launch are ordinary; a wrong passphrase is not.
        var firstRun = cloud.NewPhone(Invented.OwnerAccount);
        firstRun.PersonalDrive.Content = null;
        Assert.False((await Restore(firstRun)).NeedsAttention);

        var offline = cloud.NewPhone(Invented.OwnerAccount);
        offline.PersonalDrive.ReadFailure = new HttpRequestException("no network");
        Assert.False((await Restore(offline)).NeedsAttention);

        var mistyped = cloud.NewPhone(Invented.OwnerAccount);
        Assert.True((await Restore(mistyped, Invented.OtherPassphrase)).NeedsAttention);
    }

    // ---- no passphrase, no business ----

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task WithoutAPassphraseNothingIsReadOrFilled(string? passphrase)
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);

        var result = await Restore(phone, passphrase);

        Assert.Equal(VaultReadOutcome.NoPassphrase, result.Shared);
        Assert.Equal(VaultReadOutcome.NoPassphrase, result.Personal);
        Assert.Empty(result.Filled);
        Assert.Equal(0, cloud.SharedDrive.Reads);
        Assert.Equal(0, phone.PersonalDrive.Reads);
        Assert.Equal(string.Empty, phone.Local.SleepNumberLogin);
    }

    [Fact]
    public async Task CancellationIsTheCallerLeavingNotAFailure()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => phone.Sync.RestoreAsync(Invented.Passphrase, gone.Token));

        Assert.Equal(string.Empty, phone.Local.SleepNumberLogin);
    }

    // ---- publish ----

    // Unless a test says what changed, every field this install can change counts as changed: the
    // state of a phone whose owner has just set everything up.
    private static async Task<ConstitutionPublishResult> Publish(
        TestPhone phone, string? passphrase = Invented.Passphrase, IReadOnlyCollection<VaultField>? changed = null) =>
        await phone.Sync.PublishAsync(passphrase, changed ?? Invented.EverythingChanged, default);

    [Fact]
    public async Task TheOwnerPublishesWhatChangedAndOnlyFillsTheReusedSections()
    {
        // The vault holds older values; this install holds the current ones and has just changed the
        // Sleep Number login and paired the tablet. Claude and Gemini are Folio's to rotate: the vault
        // already holds them, so what this install holds is left out of it.
        var cloud = new TestCloud();
        cloud.SeedShared(
            Invented.Shared() with
            {
                Constitution = new ConstitutionSecrets("old@example.com", "example-old-word"),
                Remarkable = new RemarkableSecrets("example-old-token"),
                Claude = new ClaudeSecrets("example-old-claude"),
            },
            Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.FillLocalSharedConnections();

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.Published, result.Shared);
        var after = cloud.ReadShared(Invented.Passphrase);
        Assert.Equal(Invented.SleepLogin, after.Constitution.SleepNumberLogin);
        Assert.Equal(Invented.SleepWord, after.Constitution.SleepNumberPassword);
        Assert.Equal(Invented.RemarkableToken, after.Remarkable.DeviceToken);
        Assert.Equal("example-old-claude", after.Claude.ApiKey);
        Assert.Equal(Invented.GeminiKey, after.Gemini.ApiKey);
        Assert.Equal(Invented.OwnerAccount, after.OwnerAccount);
    }

    [Fact]
    public async Task OnlyConstitutionsOwnFieldsMove()
    {
        // Every other app's section, the file-id map and the owner come out byte-for-byte as they went in.
        // That includes Nyt: the New York Times is not used (D96), so the sync has no business there.
        var cloud = new TestCloud();
        var before = Invented.Shared();
        cloud.SeedShared(before, Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.FillLocalSharedConnections();
        phone.Local.SleepNumberLogin = "newer@example.com";

        await Publish(phone);

        var after = cloud.ReadShared(Invented.Passphrase);
        Assert.Equal(
            Invented.WithoutTheSectionsConstitutionWrites(before),
            Invented.WithoutTheSectionsConstitutionWrites(after));
        Assert.Equal("example-nyt-cookie", after.Nyt.SessionCookie);
        Assert.Equal("example-nyt-key", after.Nyt.ApiKey);
        Assert.Equal("newer@example.com", after.Constitution.SleepNumberLogin);
    }

    [Fact]
    public async Task StaleLocalClaudeKeyNeverOverwritesARotatedVaultKey()
    {
        // Folio rotated the shared Claude key in the vault. This install still holds the old one, and it
        // has just saved a Sleep Number login: that publish must not carry the old key back up.
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared() with { Claude = new ClaudeSecrets("example-rotated-claude") }, Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.FillLocalSharedConnections();
        phone.Local.ClaudeApiKey = "example-stale-claude";

        await Publish(phone, changed: [VaultField.SleepNumberLogin, VaultField.SleepNumberPassword]);
        Assert.Equal("example-rotated-claude", cloud.ReadShared(Invented.Passphrase).Claude.ApiKey);

        // Not even when the caller says the key changed: Constitution has no screen for it and never owns it.
        await Publish(phone, changed: [VaultField.ClaudeApiKey, VaultField.GeminiApiKey]);
        Assert.Equal("example-rotated-claude", cloud.ReadShared(Invented.Passphrase).Claude.ApiKey);
    }

    [Fact]
    public async Task UnchangedFieldsAreNeverPublished()
    {
        // The install holds a different value for every shared field; it says only that the login changed.
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.Local.SleepNumberLogin = "newer@example.com";
        phone.Local.SleepNumberPassword = "example-newer-word";
        phone.Local.RemarkableDeviceToken = "example-newer-token";
        phone.Local.ClaudeApiKey = "example-newer-claude";
        phone.Local.GeminiApiKey = "example-newer-gemini";

        await Publish(phone, changed: [VaultField.SleepNumberLogin]);

        var after = cloud.ReadShared(Invented.Passphrase);
        Assert.Equal("newer@example.com", after.Constitution.SleepNumberLogin);
        Assert.Equal(Invented.SleepWord, after.Constitution.SleepNumberPassword);
        Assert.Equal(Invented.RemarkableToken, after.Remarkable.DeviceToken);
        Assert.Equal(Invented.ClaudeKey, after.Claude.ApiKey);
        Assert.Equal(Invented.GeminiKey, after.Gemini.ApiKey);
    }

    [Fact]
    public async Task TheTabletTokenIsPublishedOnlyWhenJustPairedOrMissingFromTheVault()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.Local.RemarkableDeviceToken = "example-repaired-token";

        // Held, but not just paired: the vault's token stands.
        await Publish(phone, changed: []);
        Assert.Equal(Invented.RemarkableToken, cloud.ReadShared(Invented.Passphrase).Remarkable.DeviceToken);

        // Just paired: the new token replaces it.
        await Publish(phone, changed: [VaultField.RemarkableDeviceToken]);
        Assert.Equal("example-repaired-token", cloud.ReadShared(Invented.Passphrase).Remarkable.DeviceToken);
    }

    [Fact]
    public async Task AValueTheVaultLacksIsFilledEvenWhenNothingChanged()
    {
        // A login saved before any passphrase existed is held locally and was never backed up. Adding the
        // passphrase later (nothing changed since) must still carry it up, and into empty slots only.
        var cloud = new TestCloud();
        cloud.SeedShared(
            Invented.Shared() with
            {
                Constitution = new ConstitutionSecrets(),
                Remarkable = new RemarkableSecrets(),
                Claude = new ClaudeSecrets(),
            },
            Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.FillLocalSharedConnections();
        phone.Local.GeminiApiKey = "example-newer-gemini";

        await Publish(phone, changed: []);

        var after = cloud.ReadShared(Invented.Passphrase);
        Assert.Equal(Invented.SleepLogin, after.Constitution.SleepNumberLogin);
        Assert.Equal(Invented.SleepWord, after.Constitution.SleepNumberPassword);
        Assert.Equal(Invented.RemarkableToken, after.Remarkable.DeviceToken);
        Assert.Equal(Invented.ClaudeKey, after.Claude.ApiKey);
        Assert.Equal(Invented.GeminiKey, after.Gemini.ApiKey);
    }

    [Fact]
    public async Task HealthConnectChoicesArePublishedOnlyWhenChangedOrMissing()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(health: "{\"signals\":[\"example-older-choice\"]}"), Invented.Passphrase);
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        var untouched = await Publish(phone, changed: []);
        Assert.Equal("{\"signals\":[\"example-older-choice\"]}", phone.ReadPersonal(Invented.Passphrase).HealthConnectChoicesJson);
        Assert.Equal(VaultWriteOutcome.NothingToPublish, untouched.Personal);
        Assert.Equal(0, phone.PersonalDrive.Writes);

        var changedNow = await Publish(phone, changed: [VaultField.HealthConnectChoices]);
        Assert.Equal(Invented.HealthChoices, phone.ReadPersonal(Invented.Passphrase).HealthConnectChoicesJson);
        Assert.Equal(VaultWriteOutcome.Published, changedNow.Personal);
    }

    [Fact]
    public async Task AnEmptyLocalValueNeverBlanksTheVault()
    {
        // A reset phone that has not restored yet holds nothing. Saving a setting on it must not wipe
        // the household's keys from the vault; only what this install actually holds is published.
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.Local.SleepNumberLogin = "newer@example.com";

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.Published, result.Shared);
        var after = cloud.ReadShared(Invented.Passphrase);
        Assert.Equal("newer@example.com", after.Constitution.SleepNumberLogin);
        Assert.Equal(Invented.SleepWord, after.Constitution.SleepNumberPassword);
        Assert.Equal(Invented.GeminiKey, after.Gemini.ApiKey);
        Assert.Equal(Invented.RemarkableToken, after.Remarkable.DeviceToken);
        Assert.Equal(Invented.ClaudeKey, after.Claude.ApiKey);
    }

    [Fact]
    public async Task NothingToSayWritesNothingAndClaimsNothing()
    {
        // An install with no connections has nothing to publish. Writing anyway would make it the
        // owner of an unowned vault by accident, with an empty write.
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.PartnerAccount);

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.NothingToPublish, result.Shared);
        Assert.Equal(VaultWriteOutcome.NothingToPublish, result.Personal);
        Assert.False(result.NeedsAttention);
        Assert.Equal(0, cloud.SharedDrive.Reads + cloud.SharedDrive.Writes);
        Assert.Equal(0, phone.PersonalDrive.Reads + phone.PersonalDrive.Writes);
        Assert.Null(cloud.SharedDrive.Content);
    }

    [Fact]
    public async Task ThePublisherOfAnUnownedVaultBecomesItsOwner()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.FillLocalSharedConnections();

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.Published, result.Shared);
        Assert.Equal(Invented.OwnerAccount, cloud.SharedOwner);
    }

    // ---- never over a vault it could not read ----

    [Theory]
    [InlineData(VaultReadOutcome.WrongPassphrase)]
    [InlineData(VaultReadOutcome.Corrupt)]
    public async Task NeverPublishesOverAnUnreadVault(VaultReadOutcome how)
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        Break(cloud.SharedDrive, how);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);
        Break(phone.PersonalDrive, how);
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = "{\"signals\":[\"example-newer-choice\"]}";
        var sharedBefore = cloud.SharedDrive.Content;
        var personalBefore = phone.PersonalDrive.Content;

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.RefusedUnreadable, result.Shared);
        Assert.Equal(VaultWriteOutcome.RefusedUnreadable, result.Personal);
        Assert.True(result.NeedsAttention);
        Assert.Equal(0, cloud.SharedDrive.Writes);
        Assert.Equal(0, phone.PersonalDrive.Writes);
        Assert.Equal(sharedBefore, cloud.SharedDrive.Content);
        Assert.Equal(personalBefore, phone.PersonalDrive.Content);
    }

    [Fact]
    public async Task AnUnreadablePersonalVaultDoesNotStopTheSharedOne()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        Break(phone.PersonalDrive, VaultReadOutcome.WrongPassphrase);
        phone.Local.SleepNumberLogin = "newer@example.com";
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.Published, result.Shared);
        Assert.Equal(VaultWriteOutcome.RefusedUnreadable, result.Personal);
        Assert.Equal("newer@example.com", cloud.ReadShared(Invented.Passphrase).Constitution.SleepNumberLogin);
    }

    [Fact]
    public async Task AnUnreadableSharedVaultDoesNotStopThePersonalOne()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        Break(cloud.SharedDrive, VaultReadOutcome.Corrupt);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.Local.ClaudeApiKey = "example-newer-claude";
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.RefusedUnreadable, result.Shared);
        Assert.Equal(VaultWriteOutcome.Published, result.Personal);
        Assert.Equal(Invented.HealthChoices, phone.ReadPersonal(Invented.Passphrase).HealthConnectChoicesJson);
    }

    [Fact]
    public async Task ADriveThatCannotBeReachedIsAResultNotAnException()
    {
        var cloud = new TestCloud();
        cloud.SharedDrive.ReadFailure = new HttpRequestException("no network");
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.PersonalDrive.ReadFailure = new HttpRequestException("no network");
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.Failed, result.Shared);
        Assert.Equal(VaultWriteOutcome.Failed, result.Personal);
        Assert.True(result.NeedsAttention);
        Assert.Contains("HttpRequestException", result.SharedDetail);
        Assert.Contains("no network", result.SharedDetail);
        Assert.Contains("HttpRequestException", result.PersonalDetail);
        Assert.Equal(0, cloud.SharedDrive.Writes);
        Assert.Equal(0, phone.PersonalDrive.Writes);
    }

    [Fact]
    public async Task AWriteThatFailsIsAResultNotAnException()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        cloud.SharedDrive.WriteFailure = new HttpRequestException("insufficient scope");
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.PersonalDrive.WriteFailure = new HttpRequestException("insufficient scope");
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.Failed, result.Shared);
        Assert.Equal(VaultWriteOutcome.Failed, result.Personal);
        Assert.True(result.NeedsAttention);
        Assert.Contains("insufficient scope", result.SharedDetail);
        Assert.Contains("insufficient scope", result.PersonalDetail);
    }

    // ---- only the owning account publishes shared sections ----

    [Fact]
    public async Task AnotherAccountNeverWritesTheSharedVault()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(owner: Invented.OwnerAccount), Invented.Passphrase);
        var before = cloud.SharedDrive.Content;
        var partner = cloud.NewPhone(Invented.PartnerAccount);
        partner.FillLocalSharedConnections();
        partner.Local.ClaudeApiKey = "example-partner-claude";
        partner.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        var result = await Publish(partner);

        Assert.Equal(VaultWriteOutcome.RefusedNotOwner, result.Shared);
        Assert.Equal(0, cloud.SharedDrive.Writes);
        Assert.Equal(before, cloud.SharedDrive.Content);
        Assert.Equal(Invented.ClaudeKey, cloud.ReadShared(Invented.Passphrase).Claude.ApiKey);

        // Her own vault is hers to write; and not being the owner is the design, not a fault.
        Assert.Equal(VaultWriteOutcome.Published, result.Personal);
        Assert.False(result.NeedsAttention);
    }

    [Fact]
    public async Task OwnershipIsComparedWithoutRegardToCase()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(owner: Invented.OwnerAccount), Invented.Passphrase);
        var phone = cloud.NewPhone("Owner@Example.com");
        phone.Local.SleepNumberLogin = "newer@example.com";

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.Published, result.Shared);
        Assert.Equal("newer@example.com", cloud.ReadShared(Invented.Passphrase).Constitution.SleepNumberLogin);
    }

    [Fact]
    public async Task AnInstallThatDoesNotKnowItsAccountDoesNotWriteTheSharedVault()
    {
        // Ownership is the whole rule, so an install that cannot say who it is cannot show it owns the
        // vault. It does not even read the shared file; its own vault is unaffected.
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(account: null);
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.RefusedUnknownAccount, result.Shared);
        Assert.Equal(0, cloud.SharedDrive.Reads + cloud.SharedDrive.Writes);
        Assert.Equal(VaultWriteOutcome.Published, result.Personal);
    }

    // ---- personal facts stay personal ----

    [Fact]
    public async Task PersonalFactsStayPersonal()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;
        phone.Local.WorkScheduleJson = Invented.WorkSchedule;

        await Publish(phone);

        // The serialized shared vault carries neither the values nor a field that could hold them.
        var shared = cloud.SharedPlaintext(Invented.Passphrase);
        Assert.DoesNotContain(Invented.HealthMarker, shared, StringComparison.Ordinal);
        Assert.DoesNotContain(Invented.WorkMarker, shared, StringComparison.Ordinal);
        Assert.DoesNotContain("HealthConnect", shared, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkSchedule", shared, StringComparison.Ordinal);

        // The choices went where they belong: this person's own vault.
        Assert.Equal(Invented.HealthChoices, phone.ReadPersonal(Invented.Passphrase).HealthConnectChoicesJson);
    }

    [Fact]
    public async Task PersonalFactsStayPersonalEvenWhenTheSharedVaultIsNotOurs()
    {
        // The partner cannot write the shared vault at all, and her choices still back up, to her own.
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(owner: Invented.OwnerAccount), Invented.Passphrase);
        var partner = cloud.NewPhone(Invented.PartnerAccount);
        partner.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        await Publish(partner);

        Assert.DoesNotContain(Invented.HealthMarker, cloud.SharedPlaintext(Invented.Passphrase), StringComparison.Ordinal);
        Assert.Equal(Invented.HealthChoices, partner.ReadPersonal(Invented.Passphrase).HealthConnectChoicesJson);
    }

    [Fact]
    public async Task TheWorkScheduleIsReadHereAndNeverWrittenBack()
    {
        // Foreword owns the work schedule. A copy restored here can go stale; publishing it back would
        // overwrite Foreword's newer one with an old one.
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        const string forewordsNewerSchedule = "{\"blocks\":[\"example-newer-work\"]}";
        phone.SeedPersonal(Invented.Personal(work: forewordsNewerSchedule), Invented.Passphrase);
        phone.Local.WorkScheduleJson = Invented.WorkSchedule;
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        await Publish(phone);

        Assert.Equal(forewordsNewerSchedule, phone.ReadPersonal(Invented.Passphrase).WorkScheduleJson);
    }

    [Fact]
    public async Task PublishingLeavesTheRestOfThePersonalVaultAlone()
    {
        // Score keeps a sign-in here and Foreword keeps its calendar choices; neither is ours to move.
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        var before = Invented.Personal(health: "{\"signals\":[\"example-older-choice\"]}");
        phone.SeedPersonal(before, Invented.Passphrase);
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        await Publish(phone);

        var after = phone.ReadPersonal(Invented.Passphrase);
        Assert.Equal(Invented.HealthChoices, after.HealthConnectChoicesJson);
        Assert.Equal(Invented.WithoutHealthChoices(before), Invented.WithoutHealthChoices(after));
    }

    [Fact]
    public async Task APersonalVaultThatDoesNotExistYetIsCreated()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        var result = await Publish(phone);

        Assert.Equal(VaultWriteOutcome.Published, result.Personal);
        Assert.Equal(Invented.HealthChoices, phone.ReadPersonal(Invented.Passphrase).HealthConnectChoicesJson);
    }

    // ---- a second install ----

    [Fact]
    public async Task ASecondInstallRestoresFromItsOwnPersonalVaultOnly()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(owner: Invented.OwnerAccount), Invented.Passphrase);

        // The owner's phone publishes her choices to her Drive.
        var owner = cloud.NewPhone(Invented.OwnerAccount);
        owner.Local.HealthConnectChoicesJson = Invented.HealthChoices;
        await Publish(owner);

        // The partner has her own Drive with her own, different choices.
        const string partnersChoices = "{\"signals\":[\"example-partner-choice\"]}";
        var partner = cloud.NewPhone(Invented.PartnerAccount);
        partner.SeedPersonal(PersonalVault.Empty with { HealthConnectChoicesJson = partnersChoices }, Invented.Passphrase);

        await Restore(partner);

        Assert.Equal(partnersChoices, partner.Local.HealthConnectChoicesJson);
        Assert.DoesNotContain(Invented.HealthMarker, partner.Local.HealthConnectChoicesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondInstallWithNoChoicesOfItsOwnIsNeverHandedTheFirstOnes()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(owner: Invented.OwnerAccount), Invented.Passphrase);
        var owner = cloud.NewPhone(Invented.OwnerAccount);
        owner.Local.HealthConnectChoicesJson = Invented.HealthChoices;
        owner.Local.WorkScheduleJson = Invented.WorkSchedule;
        owner.FillLocalSharedConnections();
        await Publish(owner);

        var partner = cloud.NewPhone(Invented.PartnerAccount);

        var result = await Restore(partner);

        // The household's connections come back; her choices and her schedule are hers, and empty.
        Assert.Equal(Invented.SleepLogin, partner.Local.SleepNumberLogin);
        Assert.Equal(string.Empty, partner.Local.HealthConnectChoicesJson);
        Assert.Equal(string.Empty, partner.Local.WorkScheduleJson);
        Assert.Equal(VaultReadOutcome.Empty, result.Personal);
    }

    [Fact]
    public async Task AResetIsAPassphraseAway()
    {
        // The owner sets everything up and backs it up; Foreword has already put the work schedule in
        // her personal vault. Then the phone is wiped: a new install, her same Drive, nothing local.
        var cloud = new TestCloud();
        var before = cloud.NewPhone(Invented.OwnerAccount);
        before.SeedPersonal(PersonalVault.Empty with { WorkScheduleJson = Invented.WorkSchedule }, Invented.Passphrase);
        before.FillLocalSharedConnections();
        before.Local.HealthConnectChoicesJson = Invented.HealthChoices;
        Assert.True((await Publish(before)).Shared == VaultWriteOutcome.Published);

        var afterReset = cloud.NewPhone(Invented.OwnerAccount, before.PersonalDrive);
        var result = await Restore(afterReset);

        Assert.Equal(Invented.SleepLogin, afterReset.Local.SleepNumberLogin);
        Assert.Equal(Invented.SleepWord, afterReset.Local.SleepNumberPassword);
        Assert.Equal(Invented.RemarkableToken, afterReset.Local.RemarkableDeviceToken);
        Assert.Equal(Invented.ClaudeKey, afterReset.Local.ClaudeApiKey);
        Assert.Equal(Invented.GeminiKey, afterReset.Local.GeminiApiKey);
        Assert.Equal(Invented.HealthChoices, afterReset.Local.HealthConnectChoicesJson);
        Assert.Equal(Invented.WorkSchedule, afterReset.Local.WorkScheduleJson);
        Assert.Equal(7, result.Filled.Count);
    }

    // ---- no passphrase, no business ----

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task WithoutAPassphraseNothingIsPublished(string? passphrase)
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        var result = await Publish(phone, passphrase);

        Assert.Equal(VaultWriteOutcome.NoPassphrase, result.Shared);
        Assert.Equal(VaultWriteOutcome.NoPassphrase, result.Personal);
        Assert.False(result.NeedsAttention);
        Assert.Equal(0, cloud.SharedDrive.Reads + cloud.SharedDrive.Writes);
        Assert.Equal(0, phone.PersonalDrive.Reads + phone.PersonalDrive.Writes);
    }

    [Fact]
    public async Task CancellingAPublishIsTheCallerLeavingNotAFailure()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.FillLocalSharedConnections();
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => phone.Sync.PublishAsync(Invented.Passphrase, Invented.EverythingChanged, gone.Token));

        Assert.Equal(0, cloud.SharedDrive.Writes);
    }
}
