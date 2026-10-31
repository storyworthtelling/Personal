using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Health;
using Constitution.Infrastructure.Vault;
using Constitution.Tests.Vault;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Tests.Health;

/// <summary>
/// Review S3: a grant is the platform's, given to one install. Choices restored onto a fresh install must not
/// read as "allowed once and taken away", which raises a false Problem notice about Health Connect.
/// </summary>
public class RestoredChoicesTests
{
    private static HealthChoices Granted() => HealthChoices.Empty
        .With(BodySignal.Weight, SignalChoice.Granted)
        .With(BodySignal.Sleep, SignalChoice.Asked);

    [Fact]
    public void AGrantBecomesAnAskWhenRestored()
    {
        var restored = HealthChoices.FromJson(HealthChoices.AsRestored(Granted().ToJson()));

        Assert.Equal(SignalChoice.Asked, restored.Of(BodySignal.Weight));
        Assert.Equal(SignalChoice.Asked, restored.Of(BodySignal.Sleep));
        Assert.Equal(SignalChoice.NotAsked, restored.Of(BodySignal.Steps));
    }

    [Fact]
    public void AnythingElseInTheJsonIsLeftAsItWas()
    {
        const string withExtras = "{\"version\":1,\"signals\":{\"weight\":\"granted\",\"unknown-signal\":\"granted\"},\"note\":\"kept\"}";

        var restored = HealthChoices.AsRestored(withExtras);

        Assert.Contains("\"unknown-signal\":\"asked\"", restored, StringComparison.Ordinal);
        Assert.Contains("\"note\":\"kept\"", restored, StringComparison.Ordinal);
        Assert.DoesNotContain("granted", restored, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"signals\":[\"granted\"]}")]
    [InlineData("[1,2]")]
    public void ADocumentOfAnotherShapeIsHandedBackUntouched(string json)
    {
        Assert.Equal(json, HealthChoices.AsRestored(json));
    }

    [Fact]
    public async Task RestoreDemotesGrantedToAsked()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(PersonalVault.Empty with { HealthConnectChoicesJson = Granted().ToJson() }, Invented.Passphrase);

        var result = await phone.Sync.RestoreAsync(Invented.Passphrase, default);

        Assert.Contains(VaultField.HealthConnectChoices, result.Filled);
        var local = HealthChoices.FromJson(phone.Local.HealthConnectChoicesJson);
        Assert.Equal(SignalChoice.Asked, local.Of(BodySignal.Weight));
        Assert.DoesNotContain("granted", phone.Local.HealthConnectChoicesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARestoredChoiceIsDeclinedNotRevoked()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(PersonalVault.Empty with { HealthConnectChoicesJson = Granted().ToJson() }, Invented.Passphrase);
        await phone.Sync.RestoreAsync(Invented.Passphrase, default);
        var choices = HealthChoices.FromJson(phone.Local.HealthConnectChoicesJson);

        Assert.Empty(PermissionAsk.RevokedSignals(choices, _ => false));
        Assert.Equal(AskKind.Declined, PermissionAsk.For(BodySignal.Weight, choices.Of(BodySignal.Weight), grantedNow: false).Kind);
    }

    [Fact]
    public async Task AGrantOnThisInstallThenLostIsStillRevoked()
    {
        var cloud = new TestCloud();
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(PersonalVault.Empty with { HealthConnectChoicesJson = Granted().ToJson() }, Invented.Passphrase);
        await phone.Sync.RestoreAsync(Invented.Passphrase, default);

        // The owner allows weight on this phone: the open-time reconcile records the grant.
        var granted = HealthChoices.FromJson(phone.Local.HealthConnectChoicesJson).Reconciled(signal => signal == BodySignal.Weight);
        Assert.Equal(SignalChoice.Granted, granted.Of(BodySignal.Weight));

        // Later Health Connect takes it away.
        var revoked = PermissionAsk.RevokedSignals(granted, _ => false);

        Assert.Equal([BodySignal.Weight], revoked);
        Assert.Equal(AskKind.Revoked, PermissionAsk.For(BodySignal.Weight, granted.Of(BodySignal.Weight), grantedNow: false).Kind);
    }
}
