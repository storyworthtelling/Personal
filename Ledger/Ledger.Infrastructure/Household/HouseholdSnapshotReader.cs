using System.Text.Json;
using Index.Domain.Household;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Index.Infrastructure.Household;

/// <summary>
/// Her install's side of the household file (D10, D26): reads
/// <c>storyworthtelling-index-household.json</c> over the shared <see cref="IDriveTextFile"/> seam and
/// decrypts it with the vault passphrase, through the exact same <see cref="VaultEnvelope"/>/
/// <see cref="SecretsCrypto"/> path the shared vault itself uses — so a wrong passphrase fails the same
/// way (a caught <see cref="WrongPassphraseException"/> surfaced as
/// <see cref="HouseholdSnapshotStatus.WrongPassphrase"/>), not a second, differently-worded error.
/// </summary>
public sealed class HouseholdSnapshotReader
{
    // Kept on the reader (not the publisher) because "the reading side is the one that has to find the
    // file by this exact name" is the fact that matters; HouseholdSnapshotPublisher.FileName is the
    // same constant, not a second one that could drift from it.
    public const string FileName = "storyworthtelling-index-household.json";

    private readonly IDriveTextFile file;

    public HouseholdSnapshotReader(IDriveTextFile file)
    {
        this.file = file;
    }

    public async Task<HouseholdSnapshotReadResult> ReadAsync(string passphrase, CancellationToken cancellationToken)
    {
        var content = await file.ReadAsync(cancellationToken);
        var (status, plaintext) = VaultEnvelope.Read(content, passphrase);

        if (status != SecretsVaultStatus.Loaded)
        {
            return status switch
            {
                SecretsVaultStatus.Empty => HouseholdSnapshotReadResult.Empty,
                SecretsVaultStatus.WrongPassphrase => HouseholdSnapshotReadResult.WrongPassphrase,
                _ => HouseholdSnapshotReadResult.Corrupt,
            };
        }

        try
        {
            // The version is read BEFORE the body is deserialised, from the raw JSON: a file written by a
            // newer build may have a shape this one would either throw on (then it reads as "damaged", which
            // is false and invites a publisher to replace it) or, worse, deserialise "successfully" into
            // something subtly wrong. Absent means 1 — the files published before there was a version.
            using var document = JsonDocument.Parse(plaintext!);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return HouseholdSnapshotReadResult.Corrupt;
            }

            var version = HouseholdSnapshot.FirstVersion;
            if (TryGetProperty(document.RootElement, nameof(HouseholdSnapshot.Version), out var versionElement)
                && (versionElement.ValueKind != JsonValueKind.Number
                    || !versionElement.TryGetInt32(out version)
                    || version < HouseholdSnapshot.FirstVersion))
            {
                return HouseholdSnapshotReadResult.Corrupt;
            }

            if (version > HouseholdSnapshot.CurrentVersion)
            {
                var publisher = TryGetProperty(document.RootElement, nameof(HouseholdSnapshot.PublishedByAccountEmail), out var email)
                    && email.ValueKind == JsonValueKind.String
                    ? email.GetString()
                    : null;
                return HouseholdSnapshotReadResult.NewerVersion(version, publisher);
            }

            var snapshot = JsonSerializer.Deserialize<HouseholdSnapshot>(plaintext!, VaultEnvelope.SerializerOptions);
            if (snapshot is null || !snapshot.SeriesAreConsistent)
            {
                return HouseholdSnapshotReadResult.Corrupt;
            }
            return HouseholdSnapshotReadResult.Loaded(snapshot);
        }
        catch (JsonException)
        {
            return HouseholdSnapshotReadResult.Corrupt;
        }
    }

    // The same case-insensitive matching VaultEnvelope.SerializerOptions applies to the body.
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
