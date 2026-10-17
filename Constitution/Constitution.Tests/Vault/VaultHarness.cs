using System.Text.Json;
using Constitution.Infrastructure.Vault;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Tests.Vault;

// The fakes the vault-sync tests stand on. Nothing here is Drive, a phone or a real credential: the
// vault stores are the real ones, run over an in-memory file, so the real envelope, the real crypto and
// the real SecretsVaultService (and with it VaultOwnership) are exercised and only the network is
// replaced. Every value is invented.

/// <summary>A Drive file that lives in memory, counts what is done to it and can be told to fail.</summary>
internal sealed class InMemoryDrive : IDriveTextFile
{
    public string? Content { get; set; }

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public Exception? ReadFailure { get; set; }

    public Exception? WriteFailure { get; set; }

    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reads++;
        return ReadFailure is null ? Task.FromResult(Content) : Task.FromException<string?>(ReadFailure);
    }

    public Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WriteFailure is not null)
        {
            return Task.FromException(WriteFailure);
        }

        Content = content;
        Writes++;
        return Task.CompletedTask;
    }
}

/// <summary>This install's local connections.</summary>
internal sealed class FakeConnections : IConstitutionConnections
{
    public string SleepNumberLogin { get; set; } = string.Empty;

    public string SleepNumberPassword { get; set; } = string.Empty;

    public string RemarkableDeviceToken { get; set; } = string.Empty;

    public string ClaudeApiKey { get; set; } = string.Empty;

    public string GeminiApiKey { get; set; } = string.Empty;

    public string HealthConnectChoicesJson { get; set; } = string.Empty;

    public string WorkScheduleJson { get; set; } = string.Empty;
}

internal sealed class CapturingLog : IConstitutionVaultLog
{
    public List<string> Lines { get; } = [];

    public void Outcome(string phrase) => Lines.Add(phrase);
}

/// <summary>The invented values every test uses. None resembles a real one.</summary>
internal static class Invented
{
    public const string Passphrase = "example-passphrase-one";
    public const string OtherPassphrase = "example-passphrase-two";

    public const string OwnerAccount = "owner@example.com";
    public const string PartnerAccount = "partner@example.com";

    public const string SleepLogin = "sleeper@example.com";
    public const string SleepWord = "example-bed-word";
    public const string RemarkableToken = "example-device-token";
    public const string ClaudeKey = "example-claude-key";
    public const string GeminiKey = "example-gemini-key";

    public const string HealthMarker = "example-health-marker";
    public const string WorkMarker = "example-work-marker";
    public const string HealthChoices = "{\"signals\":[\"" + HealthMarker + "\"]}";
    public const string WorkSchedule = "{\"blocks\":[\"" + WorkMarker + "\"]}";

    /// <summary>What a freshly set-up install has just changed: every field it can edit itself.</summary>
    public static readonly VaultField[] EverythingChanged =
    [
        VaultField.SleepNumberLogin, VaultField.SleepNumberPassword, VaultField.RemarkableDeviceToken,
        VaultField.HealthConnectChoices,
    ];

    /// <summary>Every secret-bearing invented value, for the guards that search output for any of them.</summary>
    public static readonly string[] Everything =
    [
        SleepLogin, SleepWord, RemarkableToken, ClaudeKey, GeminiKey, HealthMarker, WorkMarker,
    ];

    /// <summary>A shared vault with every section filled, including ones Constitution has no business touching.</summary>
    public static SecretsVault Shared(string? owner = OwnerAccount) => new()
    {
        OwnerAccount = owner ?? string.Empty,
        Constitution = new ConstitutionSecrets(SleepLogin, SleepWord),
        Remarkable = new RemarkableSecrets(RemarkableToken),
        Claude = new ClaudeSecrets(ClaudeKey),
        Gemini = new GeminiSecrets(GeminiKey),
        Nyt = new NytSecrets("example-nyt-cookie", "example-nyt-key"),
        Foreword = new ForewordSecrets("example-foreword-key", "{\"example\":\"mapping\"}"),
        Pulp = new PulpSecrets("example-stream", "example-omdb", "example-youtube"),
        Plaid = new PlaidSecrets("example-plaid-client", "example-plaid-word") { Items = [new PlaidItem("item-1", "example-access", "Example Bank")] },
        Gmail = new GmailSecrets("mail@example.com", "example-mail-word", "example-bridge"),
        Maps = new MapsSecrets("example-maps-key"),
        Music = new MusicSecrets("example-folder"),
        Tidal = new TidalSecrets("example-tidal-client", "example-tidal-word"),
        GitHubUpdateToken = "example-github-token",
        DriveFileIds = new Dictionary<string, string> { ["storyworthtelling-secrets.json"] = "example-file-id" },
    };

    /// <summary>A person's own vault with every field filled, including ones Foreword and Score write.</summary>
    public static PersonalVault Personal(string health = HealthChoices, string work = WorkSchedule) => new()
    {
        TidalAccount = new TidalAccount("listener-1", "example-refresh"),
        OwnerColumn = "Sam",
        OwnerPhoto = "example-photo",
        CalendarMappingJson = "{\"example\":\"calendar\"}",
        WorkScheduleJson = work,
        HealthConnectChoicesJson = health,
    };

    /// <summary>The shared vault with Constitution's four sections blanked: what must be identical before and after a publish.</summary>
    public static string WithoutTheSectionsConstitutionWrites(SecretsVault vault) =>
        JsonSerializer.Serialize(
            vault with
            {
                Constitution = new ConstitutionSecrets(),
                Remarkable = new RemarkableSecrets(),
                Claude = new ClaudeSecrets(),
                Gemini = new GeminiSecrets(),
            },
            VaultEnvelope.SerializerOptions);

    /// <summary>The personal vault with the one field Constitution writes blanked.</summary>
    public static string WithoutHealthChoices(PersonalVault vault) =>
        JsonSerializer.Serialize(vault with { HealthConnectChoicesJson = string.Empty }, VaultEnvelope.SerializerOptions);
}

/// <summary>The household's shared Drive file, and the helpers that put a vault into it and read one back.</summary>
internal sealed class TestCloud
{
    public InMemoryDrive SharedDrive { get; } = new();

    /// <summary>
    /// A phone bound to this household's shared Drive. Pass a person's existing Drive to model that
    /// same person's phone after a factory reset; leave it out for a different person.
    /// </summary>
    public TestPhone NewPhone(string? account, InMemoryDrive? personalDrive = null) => new(this, account, personalDrive);

    public void SeedShared(SecretsVault vault, string passphrase) =>
        SharedDrive.Content = VaultEnvelope.Write(JsonSerializer.Serialize(vault, VaultEnvelope.SerializerOptions), passphrase);

    public string SharedPlaintext(string passphrase) => TestCloud.Open(SharedDrive, passphrase);

    public SecretsVault ReadShared(string passphrase) =>
        JsonSerializer.Deserialize<SecretsVault>(SharedPlaintext(passphrase), VaultEnvelope.SerializerOptions)!;

    internal static string Open(InMemoryDrive drive, string passphrase)
    {
        var (status, plaintext) = VaultEnvelope.Read(drive.Content, passphrase);
        Assert.Equal(SecretsVaultStatus.Loaded, status);
        return plaintext!;
    }
}

/// <summary>
/// One install: its own local connections and its own person's Drive, over the household's shared one.
/// </summary>
internal sealed class TestPhone
{
    public TestPhone(TestCloud cloud, string? account, InMemoryDrive? personalDrive = null)
    {
        Account = account;
        PersonalDrive = personalDrive ?? new InMemoryDrive();
        Sync = new ConstitutionVaultSync(
            new DriveSecretsVaultStore(cloud.SharedDrive),
            new DrivePersonalVaultStore(PersonalDrive),
            () => Account,
            Local,
            Log);
    }

    /// <summary>The Google account this install is bound to; null until it signs in.</summary>
    public string? Account { get; set; }

    /// <summary>This person's own Drive, which no other install ever reads.</summary>
    public InMemoryDrive PersonalDrive { get; }

    public FakeConnections Local { get; } = new();

    public CapturingLog Log { get; } = new();

    public ConstitutionVaultSync Sync { get; }

    public void SeedPersonal(PersonalVault vault, string passphrase) =>
        PersonalDrive.Content = VaultEnvelope.Write(JsonSerializer.Serialize(vault, VaultEnvelope.SerializerOptions), passphrase);

    public string PersonalPlaintext(string passphrase) => TestCloud.Open(PersonalDrive, passphrase);

    public PersonalVault ReadPersonal(string passphrase) =>
        JsonSerializer.Deserialize<PersonalVault>(PersonalPlaintext(passphrase), VaultEnvelope.SerializerOptions)!;

    /// <summary>Fills every local connection with the invented values, as a fully set-up install would hold.</summary>
    public void FillLocalSharedConnections()
    {
        Local.SleepNumberLogin = Invented.SleepLogin;
        Local.SleepNumberPassword = Invented.SleepWord;
        Local.RemarkableDeviceToken = Invented.RemarkableToken;
        Local.ClaudeApiKey = Invented.ClaudeKey;
        Local.GeminiApiKey = Invented.GeminiKey;
    }
}
