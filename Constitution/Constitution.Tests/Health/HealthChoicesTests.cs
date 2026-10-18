using Constitution.Domain.Body;
using Constitution.Domain.Health;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.Vault;
using Constitution.Tests.Body;
using Constitution.Tests.Vault;

namespace Constitution.Tests.Health;

/// <summary>C-7.1.2 — which signals this person has asked for and been given, kept in their own vault and nowhere else.</summary>
public class HealthChoicesTests
{
    private static HealthChoices Some() => HealthChoices.Empty
        .With(BodySignal.Weight, SignalChoice.Granted)
        .With(BodySignal.Sleep, SignalChoice.Asked);

    [Fact]
    public async Task GoToThePersonalVault()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(PersonalVaultFor.Blank(), Invented.Passphrase);
        var runner = new ConstitutionVaultRunner(() => Invented.Passphrase, () => true, () => phone.Sync, phone.Log);
        var store = new HealthChoicesStore(phone.Local, runner.PublishAsync, new CapturingBodyLog());

        await store.SaveAsync(Some(), default);

        Assert.Equal(Some().ToJson(), phone.Local.HealthConnectChoicesJson);
        Assert.Equal(Some().ToJson(), phone.ReadPersonal(Invented.Passphrase).HealthConnectChoicesJson);

        // The shared vault, which both people read, never holds a person's choices.
        Assert.DoesNotContain("granted", cloud.SharedPlaintext(Invented.Passphrase), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishesOnlyTheChoicesField()
    {
        var phone = new TestCloud().NewPhone(Invented.OwnerAccount);
        var published = new List<IReadOnlyCollection<VaultField>>();
        var store = new HealthChoicesStore(phone.Local, (fields, _) => { published.Add(fields); return Task.FromResult<ConstitutionVaultRunResult>(null!); }, new CapturingBodyLog());

        await store.SaveAsync(Some(), default);

        Assert.Equal([VaultField.HealthConnectChoices], Assert.Single(published));
    }

    [Fact]
    public async Task UnchangedChoicesPublishNothing()
    {
        var phone = new TestCloud().NewPhone(Invented.OwnerAccount);
        var published = 0;
        var store = new HealthChoicesStore(phone.Local, (_, _) => { published++; return Task.FromResult<ConstitutionVaultRunResult>(null!); }, new CapturingBodyLog());

        await store.SaveAsync(Some(), default);
        await store.SaveAsync(Some(), default);

        Assert.Equal(1, published);
    }

    [Fact]
    public async Task ReconcilingFollowsWhatThePlatformSays()
    {
        var phone = new TestCloud().NewPhone(Invented.OwnerAccount);
        phone.Local.HealthConnectChoicesJson = Some().ToJson();
        var store = new HealthChoicesStore(phone.Local, (_, _) => Task.FromResult<ConstitutionVaultRunResult>(null!), new CapturingBodyLog());
        var source = new FakeHealthSource();
        source.Granted.Add(BodySignal.Steps);

        await store.ReconcileAsync(source, default);

        var now = store.Load();
        Assert.Equal(SignalChoice.Asked, now.Of(BodySignal.Weight)); // was granted, now withdrawn
        Assert.Equal(SignalChoice.Asked, now.Of(BodySignal.Sleep)); // asked, still not given
        Assert.Equal(SignalChoice.Granted, now.Of(BodySignal.Steps)); // given since
        Assert.Equal(SignalChoice.NotAsked, now.Of(BodySignal.Weight.Equals(BodySignal.Weight) ? BodySignal.RestingHeartRate : BodySignal.Weight));
    }

    [Fact]
    public void RoundTripsThroughItsJson()
    {
        var back = HealthChoices.FromJson(Some().ToJson());

        Assert.Equal(SignalChoice.Granted, back.Of(BodySignal.Weight));
        Assert.Equal(SignalChoice.Asked, back.Of(BodySignal.Sleep));
        Assert.Equal(SignalChoice.NotAsked, back.Of(BodySignal.Steps));
        Assert.Equal(Some().ToJson(), back.ToJson());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"signals\":[\"example-health-marker\"]}")]
    [InlineData("{\"signals\":{\"weight\":42,\"steps\":null,\"sleep\":\"somewhere\",\"nonsense\":\"granted\"}}")]
    [InlineData("{\"signals\":")]
    public void GarbledJsonReadsAsEmptyAndNeverThrows(string? json)
    {
        var choices = HealthChoices.FromJson(json);

        Assert.All(BodySignals.All, signal => Assert.Equal(SignalChoice.NotAsked, choices.Of(signal)));
    }

    [Fact]
    public void AnUnknownSignalOrStateIsSkippedNotFatal()
    {
        var choices = HealthChoices.FromJson("{\"signals\":{\"weight\":\"granted\",\"from-the-future\":\"granted\",\"sleep\":\"maybe\"}}");

        Assert.Equal(SignalChoice.Granted, choices.Of(BodySignal.Weight));
        Assert.Equal(SignalChoice.NotAsked, choices.Of(BodySignal.Sleep));
    }

    [Fact]
    public void TheJsonHoldsSignalsAndStatesAndNothingElse()
    {
        var json = Some().ToJson();

        Assert.Equal("{\"version\":1,\"signals\":{\"weight\":\"granted\",\"sleep\":\"asked\"}}", json);
    }

    [Fact]
    public void OnlyHealthConnectSignalsCanBeChosen()
    {
        Assert.Throws<ArgumentException>(() => HealthChoices.Empty.With(BodySignal.Strength, SignalChoice.Granted));
        Assert.Equal(SignalChoice.NotAsked, HealthChoices.Empty.Of(BodySignal.Calories));
    }

    [Fact]
    public void WithdrawingAGrantBecomesAskedAndNeverNotAsked()
    {
        var after = Some().Reconciled(_ => false);

        Assert.Equal(SignalChoice.Asked, after.Of(BodySignal.Weight));
    }
}

internal static class PersonalVaultFor
{
    public static Storyworthtelling.Voice.Secrets.PersonalVault Blank() => Storyworthtelling.Voice.Secrets.PersonalVault.Empty;
}
