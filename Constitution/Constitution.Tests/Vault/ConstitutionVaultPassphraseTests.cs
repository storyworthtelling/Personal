using System.Collections;
using System.Reflection;
using Constitution.Infrastructure.Vault;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Tests.Vault;

/// <summary>
/// The vault passphrase is the one secret not in the vault, so it is the one a leak cannot be undone
/// by restoring. It is an argument to each call and nothing else: never kept, never logged, never in a
/// result or a failure's text, and no secret value reaches a log line or a result either.
///
/// <para>Every passphrase here is built at run time from a fresh Guid, so no literal in the source is
/// a passphrase and no test can pass by matching a constant. The sweep runs every shape of outcome,
/// including the two where a lower layer's own exception message echoes the passphrase back, because
/// "my code never writes it" is only half of "it never appears".</para>
/// </summary>
public class ConstitutionVaultPassphraseTests
{
    private static string NewPassphrase() => "example-" + Guid.NewGuid().ToString("N");

    private static readonly string[] ScenarioNames =
    [
        "healthy",
        "wrong passphrase",
        "corrupt",
        "unreachable, message echoes the passphrase",
        "write fails, message echoes the passphrase",
        "another account owns the vault",
        "account unknown",
        "nothing to publish",
    ];

    public static TheoryData<string> Scenarios
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in ScenarioNames)
            {
                data.Add(name);
            }

            return data;
        }
    }

    private static TestPhone Arrange(string scenario, string passphrase)
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.SeedPersonal(Invented.Personal(), passphrase);
        phone.FillLocalSharedConnections();
        phone.Local.HealthConnectChoicesJson = Invented.HealthChoices;

        switch (scenario)
        {
            case "healthy":
                break;
            case "wrong passphrase":
                cloud.SharedDrive.Content = VaultEnvelope.Write("{}", Invented.OtherPassphrase);
                phone.PersonalDrive.Content = VaultEnvelope.Write("{}", Invented.OtherPassphrase);
                break;
            case "corrupt":
                cloud.SharedDrive.Content = "{ not an envelope";
                phone.PersonalDrive.Content = "{ not an envelope";
                break;
            case "unreachable, message echoes the passphrase":
                cloud.SharedDrive.ReadFailure = new HttpRequestException("could not reach drive with " + passphrase);
                phone.PersonalDrive.ReadFailure = new HttpRequestException("could not reach drive with " + passphrase);
                break;
            case "write fails, message echoes the passphrase":
                cloud.SharedDrive.WriteFailure = new HttpRequestException("rejected the write for " + passphrase);
                phone.PersonalDrive.WriteFailure = new HttpRequestException("rejected the write for " + passphrase);
                break;
            case "another account owns the vault":
                phone.Account = Invented.PartnerAccount;
                break;
            case "account unknown":
                phone.Account = null;
                break;
            case "nothing to publish":
                phone.Local.SleepNumberLogin = string.Empty;
                phone.Local.SleepNumberPassword = string.Empty;
                phone.Local.RemarkableDeviceToken = string.Empty;
                phone.Local.ClaudeApiKey = string.Empty;
                phone.Local.GeminiApiKey = string.Empty;
                phone.Local.HealthConnectChoicesJson = string.Empty;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Not a scenario.");
        }

        return phone;
    }

    /// <summary>Every piece of text a result exposes: its own ToString and each property, with lists read item by item.</summary>
    private static IEnumerable<string> TextsOf(object result)
    {
        yield return result.ToString() ?? string.Empty;
        foreach (var property in result.GetType().GetProperties())
        {
            switch (property.GetValue(result))
            {
                case null:
                    break;
                case string text:
                    yield return text;
                    break;
                case IEnumerable items:
                    foreach (var item in items)
                    {
                        yield return item.ToString() ?? string.Empty;
                    }

                    break;
                case var other:
                    yield return other.ToString() ?? string.Empty;
                    break;
            }
        }
    }

    private static async Task<List<string>> EverythingSaid(TestPhone phone, string passphrase)
    {
        var restore = await phone.Sync.RestoreAsync(passphrase, default);
        var publish = await phone.Sync.PublishAsync(passphrase, Invented.EverythingChanged, default);
        return [.. TextsOf(restore), .. TextsOf(publish), .. phone.Log.Lines];
    }

    // ---- the passphrase never appears ----

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task NoResultOrLogLineEverContainsThePassphrase(string scenario)
    {
        var passphrase = NewPassphrase();
        var phone = Arrange(scenario, passphrase);

        var said = await EverythingSaid(phone, passphrase);

        Assert.NotEmpty(said);
        foreach (var text in said)
        {
            Assert.DoesNotContain(passphrase, text, StringComparison.Ordinal);
        }

        Assert.NotEmpty(phone.Log.Lines);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task NoSecretValueReachesTheLogOrAResult(string scenario)
    {
        var passphrase = NewPassphrase();
        var phone = Arrange(scenario, passphrase);

        var said = await EverythingSaid(phone, passphrase);

        foreach (var text in said)
        {
            foreach (var value in Invented.Everything)
            {
                Assert.DoesNotContain(value, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task AFailureNamesItselfButNeverThePassphraseItsMessageEchoed()
    {
        var passphrase = NewPassphrase();
        var phone = Arrange("unreachable, message echoes the passphrase", passphrase);

        var restore = await phone.Sync.RestoreAsync(passphrase, default);
        var publish = await phone.Sync.PublishAsync(passphrase, Invented.EverythingChanged, default);

        // Still useful: the type and the rest of the message survive.
        Assert.Contains("HttpRequestException", restore.SharedDetail);
        Assert.Contains("could not reach drive", restore.SharedDetail);
        Assert.Contains("HttpRequestException", restore.PersonalDetail);
        Assert.Contains("could not reach drive", publish.SharedDetail);
        Assert.Contains("could not reach drive", publish.PersonalDetail);
        Assert.DoesNotContain(passphrase, restore.SharedDetail, StringComparison.Ordinal);
        Assert.DoesNotContain(passphrase, restore.PersonalDetail, StringComparison.Ordinal);
        Assert.DoesNotContain(passphrase, publish.SharedDetail, StringComparison.Ordinal);
        Assert.DoesNotContain(passphrase, publish.PersonalDetail, StringComparison.Ordinal);

        var writePhone = Arrange("write fails, message echoes the passphrase", passphrase);
        var writeResult = await writePhone.Sync.PublishAsync(passphrase, Invented.EverythingChanged, default);
        Assert.Contains("rejected the write", writeResult.SharedDetail);
        Assert.Contains("rejected the write", writeResult.PersonalDetail);
        Assert.DoesNotContain(passphrase, writeResult.SharedDetail, StringComparison.Ordinal);
        Assert.DoesNotContain(passphrase, writeResult.PersonalDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingEscapesAsAnExceptionInAnyScenario()
    {
        foreach (var scenario in ScenarioNames)
        {
            var passphrase = NewPassphrase();
            var phone = Arrange(scenario, passphrase);

            var restore = await Record.ExceptionAsync(() => phone.Sync.RestoreAsync(passphrase, default));
            var publish = await Record.ExceptionAsync(() => phone.Sync.PublishAsync(passphrase, Invented.EverythingChanged, default));

            Assert.True(restore is null, $"{scenario}: restore threw {restore?.GetType().Name}");
            Assert.True(publish is null, $"{scenario}: publish threw {publish?.GetType().Name}");
        }
    }

    // ---- and it is not kept ----

    [Fact]
    public async Task ThePassphraseIsNotRememberedBetweenCalls()
    {
        var cloud = new TestCloud();
        cloud.SeedShared(Invented.Shared(), Invented.Passphrase);
        var phone = cloud.NewPhone(Invented.OwnerAccount);
        phone.FillLocalSharedConnections();
        Assert.Equal(VaultWriteOutcome.Published, (await phone.Sync.PublishAsync(Invented.Passphrase, Invented.EverythingChanged, default)).Shared);

        // A different passphrase opens nothing: the first call's is not being reused.
        var wrong = await phone.Sync.PublishAsync(Invented.OtherPassphrase, Invented.EverythingChanged, default);
        Assert.Equal(VaultWriteOutcome.RefusedUnreadable, wrong.Shared);

        // And with none given, nothing happens at all, rather than the last one being used.
        var none = await phone.Sync.PublishAsync(string.Empty, Invented.EverythingChanged, default);
        Assert.Equal(VaultWriteOutcome.NoPassphrase, none.Shared);

        var fresh = cloud.NewPhone(Invented.OwnerAccount);
        Assert.Equal(VaultReadOutcome.Loaded, (await fresh.Sync.RestoreAsync(Invented.Passphrase, default)).Shared);
        Assert.Equal(VaultReadOutcome.NoPassphrase, (await fresh.Sync.RestoreAsync(null, default)).Shared);
    }

    [Fact]
    public void TheSyncHoldsOnlyItsCollaboratorsAndNoText()
    {
        // A field of type string would be a place to keep a passphrase; a new field of any kind is a
        // decision, so this lists exactly what the sync is built from.
        var fields = typeof(ConstitutionVaultSync)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(field => field.FieldType)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            typeof(Func<string?>),
            typeof(DrivePersonalVaultStore),
            typeof(IConstitutionConnections),
            typeof(IConstitutionVaultLog),
            typeof(ISecretsVaultStore),
        }.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, fields);
        Assert.DoesNotContain(typeof(string), fields);
    }

    [Fact]
    public void ThePublicSurfaceIsRestoreAndPublishAlone()
    {
        var members = typeof(ConstitutionVaultSync).GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(members, member => member is PropertyInfo or FieldInfo);
        var methods = members.OfType<MethodInfo>().Select(method => method.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["PublishAsync", "RestoreAsync"], methods);
    }
}
