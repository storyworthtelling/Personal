using Constitution.Infrastructure.Vault;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Tests.Vault;

/// <summary>Review S7: the bed side is one person's own fact, restored from and published to the personal vault only.</summary>
public class SleeperSideVaultTests
{
    private static PersonalVault WithSide(string side) => Invented.Personal() with { SleeperSide = side };

    private static Task<ConstitutionRestoreResult> Restore(TestPhone phone) => phone.Sync.RestoreAsync(Invented.Passphrase, default);

    private static Task<ConstitutionPublishResult> Publish(TestPhone phone, params VaultField[] changed) =>
        phone.Sync.PublishAsync(Invented.Passphrase, changed, default);

    [Fact]
    public async Task ARestoreFillsAMissingSide()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(WithSide("right"), Invented.Passphrase);

        var result = await Restore(phone);

        Assert.Equal("right", phone.Local.SleeperSide);
        Assert.Contains(VaultField.SleeperSide, result.Filled);
    }

    [Fact]
    public async Task ARestoreNeverReplacesAChosenSide()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(WithSide("left"), Invented.Passphrase);
        phone.Local.SleeperSide = "right";

        var result = await Restore(phone);

        Assert.Equal("right", phone.Local.SleeperSide);
        Assert.DoesNotContain(VaultField.SleeperSide, result.Filled);
    }

    [Fact]
    public async Task ChoosingNoSideIsNotUndoneByARestore()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(WithSide("left"), Invented.Passphrase);
        phone.Local.SleeperSide = "none";

        await Restore(phone);

        Assert.Equal("none", phone.Local.SleeperSide);
    }

    [Fact]
    public async Task APersonalVaultWithNoSideFillsNothing()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);

        var result = await Restore(phone);

        Assert.Equal(string.Empty, phone.Local.SleeperSide);
        Assert.DoesNotContain(VaultField.SleeperSide, result.Filled);
    }

    [Fact]
    public async Task AChosenSideIsPublishedToThePersonalVaultOnly()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);
        phone.Local.SleeperSide = "left";
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        var result = await Publish(phone, VaultField.SleeperSide);

        Assert.Equal(VaultWriteOutcome.Published, result.Personal);
        var personal = phone.ReadPersonal(Invented.Passphrase);
        Assert.Equal("left", personal.SleeperSide);
        Assert.Equal(Invented.WorkSchedule, personal.WorkScheduleJson);
        Assert.Equal("example-refresh", personal.TidalAccount.RefreshToken);
        Assert.DoesNotContain("SleeperSide", cloud.SharedPlaintext(Invented.Passphrase), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASideAloneIsWorthPublishing()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);
        phone.Local.SleeperSide = "right";

        var result = await Publish(phone, VaultField.SleeperSide);

        Assert.Equal(VaultWriteOutcome.Published, result.Personal);
        Assert.Equal("right", phone.ReadPersonal(Invented.Passphrase).SleeperSide);
    }

    [Fact]
    public async Task ChoosingNoSideIsPublishedAsNone()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(WithSide("left"), Invented.Passphrase);
        phone.Local.SleeperSide = "none";

        await Publish(phone, VaultField.SleeperSide);

        Assert.Equal("none", phone.ReadPersonal(Invented.Passphrase).SleeperSide);
    }

    [Fact]
    public async Task AnUnchangedSideLeavesTheVaultAlone()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(WithSide("left"), Invented.Passphrase);
        phone.Local.SleeperSide = "right";

        var result = await Publish(phone, VaultField.HealthConnectChoices);

        Assert.Equal(VaultWriteOutcome.NothingToPublish, result.Personal);
        Assert.Equal(0, phone.PersonalDrive.Writes);
        Assert.Equal("left", phone.ReadPersonal(Invented.Passphrase).SleeperSide);
    }

    [Fact]
    public async Task ASideNeverChosenNeverBlanksTheVault()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(WithSide("left"), Invented.Passphrase);
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        await Publish(phone, VaultField.HealthConnectChoices);

        Assert.Equal("left", phone.ReadPersonal(Invented.Passphrase).SleeperSide);
    }

    [Fact]
    public async Task ASideMissingFromTheVaultIsFilledFromThisInstall()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);
        phone.Local.SleeperSide = "left";

        await Publish(phone);

        Assert.Equal("left", phone.ReadPersonal(Invented.Passphrase).SleeperSide);
    }
}
