using Constitution.Domain.Household;
using Constitution.Infrastructure.Household;
using Constitution.Infrastructure.Vault;
using Storyworthtelling.Household;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Tests.HouseholdHead;

/// <summary>A Drive file held in memory: what was written, how many times it was read and written, and a failure to order.</summary>
internal sealed class HeadDrive : IDriveTextFile
{
    public string? Content { get; set; }

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public Exception? Failure { get; set; }

    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Failure is null ? Task.FromResult(Content) : Task.FromException<string?>(Failure);
    }

    public Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Writes++;
        Content = content;
        return Task.CompletedTask;
    }
}

internal sealed class HeadLog : IConstitutionVaultLog
{
    public List<string> Lines { get; } = [];

    public void Outcome(string phrase) => Lines.Add(phrase);
}

/// <summary>One invented phone: its own folder, its clock, its role and the shared Drive file it meets the other phone in.</summary>
internal sealed class HeadRig : IDisposable
{
    public const string Passphrase = "invented-head-passphrase";
    public const string OtherKey = "invented-other-key";

    public const string FileName = "example-constitution-household.json";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "head-rig-" + Guid.NewGuid().ToString("N"));

    public HeadRig(HeadDrive? drive = null, HouseholdFileRole role = HouseholdFileRole.Owner)
    {
        Drive = drive ?? new HeadDrive();
        Role = role;
        Now = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.FromHours(-4));
        Local = new LocalHouseholdStore(folder, "household.json");
        Shared = new DriveHouseholdStore(Drive, FileName, () => HeldPassphrase, () => HouseholdSync.MayCreateFile(Role));
        Install = new FileHouseholdInstallStore(folder);
        Service = new HouseholdFileService(
            Local,
            Shared,
            Install,
            new HouseholdFileSeams(() => HeldPassphrase.Length > 0, () => HasAccount, () => Role, () => Now, () => TimeZoneInfo.Utc),
            Log);
        Box = new HouseholdRecipeBox(Service);
        Keeper = new HouseholdRecipeKeeper(Service);
    }

    public HeadDrive Drive { get; }

    public HeadLog Log { get; } = new();

    public HouseholdFileRole Role { get; set; }

    public string HeldPassphrase { get; set; } = Passphrase;

    public bool HasAccount { get; set; } = true;

    public DateTimeOffset Now { get; set; }

    public LocalHouseholdStore Local { get; }

    public DriveHouseholdStore Shared { get; }

    public FileHouseholdInstallStore Install { get; }

    public HouseholdFileService Service { get; }

    public HouseholdRecipeBox Box { get; }

    public HouseholdRecipeKeeper Keeper { get; }

    public string SealedFile(HouseholdDocument document, string? passphrase = null) =>
        VaultEnvelope.Write(HouseholdDocumentCodec.Serialize(document), passphrase ?? Passphrase);

    public HouseholdDocument Opened() =>
        HouseholdDocumentCodec.Deserialize(VaultEnvelope.Read(Drive.Content, Passphrase).Plaintext!);

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
