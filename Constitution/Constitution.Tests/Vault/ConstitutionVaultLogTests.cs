using Constitution.Infrastructure.Vault;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Tests.Vault;

/// <summary>
/// The sync says what happened, in short outcome phrases, through an injected log: how many fields a
/// restore filled, whether a publish landed, and why one was refused. A phrase never carries a value, a
/// passphrase or an exception's text; the detail lives on the result for the screen that needs it.
/// </summary>
public class ConstitutionVaultLogTests
{
    private static TestPhone Fresh(out TestCloud cloud)
    {
        cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);
        return phone;
    }

    [Fact]
    public async Task ARestoreSaysHowManyFieldsItFilled()
    {
        var phone = Fresh(out _);

        await phone.Sync.RestoreAsync(Invented.Passphrase, default);

        var line = Assert.Single(phone.Log.Lines);
        Assert.Contains("vault restore", line);
        Assert.Contains("restored 7 fields", line);
        Assert.Contains("shared loaded", line);
        Assert.Contains("personal loaded", line);
    }

    [Fact]
    public async Task ARestoreThatFillsNothingSaysSoAndWhy()
    {
        var phone = Fresh(out var cloud);
        cloud.SharedDrive.Content = null;
        phone.PersonalDrive.Content = VaultEnvelope.Write("{}", Invented.OtherPassphrase);

        await phone.Sync.RestoreAsync(Invented.Passphrase, default);

        var line = Assert.Single(phone.Log.Lines);
        Assert.Contains("restored 0 fields", line);
        Assert.Contains("shared empty", line);
        Assert.Contains("personal wrong passphrase", line);
    }

    [Fact]
    public async Task ASingleFieldIsNotPluralised()
    {
        var phone = Fresh(out _);
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        await phone.Sync.RestoreAsync(Invented.Passphrase, default);

        Assert.Contains("restored 1 field (", Assert.Single(phone.Log.Lines));
    }

    [Fact]
    public async Task ACallWithoutAPassphraseSaysItWasSkipped()
    {
        var phone = Fresh(out _);

        await phone.Sync.RestoreAsync(string.Empty, default);
        await phone.Sync.PublishAsync(null, default);

        Assert.Equal(2, phone.Log.Lines.Count);
        Assert.All(phone.Log.Lines, line => Assert.Contains("skipped: no passphrase", line));
    }

    [Fact]
    public async Task APublishThatLandedSaysSo()
    {
        var phone = Fresh(out _);
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        await phone.Sync.PublishAsync(Invented.Passphrase, default);

        var line = Assert.Single(phone.Log.Lines);
        Assert.Contains("vault publish", line);
        Assert.Contains("shared published", line);
        Assert.Contains("personal published", line);
    }

    [Fact]
    public async Task ARefusedPublishSaysSoAndGivesTheReason()
    {
        var phone = Fresh(out var cloud);
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;
        cloud.SharedDrive.Content = VaultEnvelope.Write("{}", Invented.OtherPassphrase);
        phone.PersonalDrive.Content = "{ not an envelope";

        await phone.Sync.PublishAsync(Invented.Passphrase, default);

        var line = Assert.Single(phone.Log.Lines);
        Assert.Contains("shared refused: unreadable vault", line);
        Assert.Contains("personal refused: unreadable vault", line);
    }

    [Fact]
    public async Task AnotherAccountsPublishSaysItIsNotTheOwner()
    {
        var phone = Fresh(out _);
        phone.Account = Invented.PartnerAccount;
        phone.FillLocalSharedConnections();

        await phone.Sync.PublishAsync(Invented.Passphrase, default);

        Assert.Contains("shared refused: not the owner", Assert.Single(phone.Log.Lines));
    }

    [Fact]
    public async Task AFailureIsNamedInTheLogButItsTextIsNot()
    {
        var phone = Fresh(out var cloud);
        cloud.SharedDrive.ReadFailure = new HttpRequestException("no network at all");
        phone.PersonalDrive.ReadFailure = new HttpRequestException("no network at all");
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        await phone.Sync.RestoreAsync(Invented.Passphrase, default);
        await phone.Sync.PublishAsync(Invented.Passphrase, default);

        Assert.Equal(2, phone.Log.Lines.Count);
        Assert.Contains("shared unreachable", phone.Log.Lines[0]);
        Assert.Contains("shared failed", phone.Log.Lines[1]);
        Assert.All(phone.Log.Lines, line => Assert.DoesNotContain("no network at all", line));
    }

    [Fact]
    public async Task EveryLineIsOneShortPhrase()
    {
        var phone = Fresh(out _);
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        await phone.Sync.RestoreAsync(Invented.Passphrase, default);
        await phone.Sync.PublishAsync(Invented.Passphrase, default);

        Assert.All(phone.Log.Lines, line =>
        {
            Assert.InRange(line.Length, 1, 120);
            Assert.DoesNotContain('\n', line);
        });
    }
}
