using Constitution.Domain.Body;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Health;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.Vault;
using Constitution.Tests.Body;

namespace Constitution.Tests.Vault;

/// <summary>
/// A publish that failed on one side said only "personal failed", so the trail could not tell a dropped connection
/// from a refused write; and the health-choices line beside it said "published", because it read the run's status,
/// which is Published whenever a publish ran at all. The failed side's line now names the exception's type (never
/// its message, which comes from the Drive client and the HTTP stack), and the health-choices line says how the
/// personal vault, where the choices live, went. Every value here is invented.
/// </summary>
public class PublishFailureLineTests
{
    private const string Planted = "example-planted-message";

    private static TestPhone PhoneWhosePersonalWriteFails()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), Invented.Passphrase);
        phone.PersonalDrive.WriteFailure = new IOException(Planted);
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = "{\"signals\":[\"example-newer-choice\"]}";
        return phone;
    }

    [Fact]
    public async Task TheFailedSideNamesTheExceptionsTypeAndNotItsMessage()
    {
        var phone = PhoneWhosePersonalWriteFails();

        var result = await phone.Sync.PublishAsync(Invented.Passphrase, Invented.EverythingChanged, default);

        Assert.Equal(VaultWriteOutcome.Failed, result.Personal);
        var line = Assert.Single(phone.Log.Lines, l => l.StartsWith("vault publish:", StringComparison.Ordinal));
        Assert.EndsWith("personal failed (VaultWriteException, failed)", line, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, line, StringComparison.Ordinal);
        Assert.DoesNotContain(Invented.Passphrase, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedReadOfThePersonalVaultNamesItsType()
    {
        var phone = PhoneWhosePersonalWriteFails();
        phone.PersonalDrive.ReadFailure = new HttpRequestException(Planted);

        await phone.Sync.PublishAsync(Invented.Passphrase, Invented.EverythingChanged, default);

        var line = Assert.Single(phone.Log.Lines, l => l.StartsWith("vault publish:", StringComparison.Ordinal));
        Assert.EndsWith("personal failed (HttpRequestException)", line, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHealthChoicesLineSaysHowThePersonalVaultWent()
    {
        var phone = PhoneWhosePersonalWriteFails();
        var runner = new ConstitutionVaultRunner(() => Invented.Passphrase, () => true, () => phone.Sync, phone.Log);
        var body = new CapturingBodyLog();
        var store = new HealthChoicesStore(phone.Local, runner.PublishAsync, body);

        await store.SaveAsync(HealthChoices.Empty.With(BodySignal.Weight, SignalChoice.Granted), default);

        var line = Assert.Single(body.Lines);
        Assert.Equal("health choices saved; vault publish personal failed (VaultWriteException, failed)", line);
        Assert.DoesNotContain(Planted, line, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNewLinesPassTheExportFilter()
    {
        foreach (var line in new[]
        {
            "[Oct 18, 9:05 am] INFO Vault — vault publish: shared published, personal failed (VaultWriteException, failed)",
            "[Oct 18, 9:05 am] INFO Body — health choices saved; vault publish personal failed (VaultWriteException, failed)",
            "[Oct 18, 9:05 am] INFO Body — health choices saved; vault publish personal published",
        })
        {
            Assert.Equal(line, TrailLines.Filter(line));
        }
    }
}
