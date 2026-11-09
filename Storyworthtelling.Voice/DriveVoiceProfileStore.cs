using System.Text.Json;
using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice;

/// <summary>
/// The production voice-profile store: one JSON file in the owner's Google Drive, read by every
/// com.storyworthtelling app and written by whichever app ingested a document. All the Drive
/// mechanics live behind <see cref="IDriveTextFile"/>; this class only owns the JSON, and treats an
/// absent, blank or garbled file as "no voice learned yet" so a first run — or a corrupt file —
/// degrades quietly to neutral copy rather than throwing into the UI.
///
/// <para><b>And it tells a stale profile apart from no profile.</b> The refit renamed what is
/// written here, and there is already a file on the owner's Drive in the old shape. Read naively
/// that file comes back empty twice over — no <c>register</c> to read, and bare-string
/// <c>examples</c> where objects are now expected, which throws into the catch below — so every app
/// in the suite would quietly stop sounding like the owner with nothing anywhere saying why. An old
/// document is loaded as <see cref="VoiceProfile.NeedsRelearn"/> instead: still neutral copy for
/// now, but a state the app can name and the next ingestion can act on.</para>
/// </summary>
public sealed class DriveVoiceProfileStore : IVoiceProfileStore
{
    /// <summary>The shared file's name. One voice, one file, referenced by name across every app.</summary>
    public const string FileName = "storyworthtelling-voice.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// The sealed profile (privacy report §5: it carries verbatim passages of the owner's manuscripts). Every app
    /// reads it with the vault passphrase; the plaintext <see cref="FileName"/> is read until the sealed one has
    /// been written, then trashed.
    /// </summary>
    public const string SealedFileName = "storyworthtelling-voice-sealed.json";

    private readonly IDriveTextFile file;
    private readonly SealedDriveFile? sealedFile;
    private readonly Func<string?>? passphrase;

    /// <summary>The plaintext profile alone: what tests of the JSON and the old shape use.</summary>
    public DriveVoiceProfileStore(IDriveTextFile file)
    {
        this.file = file;
    }

    private DriveVoiceProfileStore(IDriveTextFile legacyFile, IDriveTextFile sealedFile, Func<string?> passphrase)
    {
        file = legacyFile;
        this.sealedFile = new SealedDriveFile(sealedFile);
        this.passphrase = passphrase;
    }

    /// <summary>
    /// The production store: the sealed profile under the vault passphrase, with the plaintext one read until it
    /// has been moved across. <paramref name="fileFor"/> builds the Drive file for a name.
    /// </summary>
    public static DriveVoiceProfileStore Sealed(Func<string, IDriveTextFile> fileFor, Func<string?> passphrase) =>
        new(fileFor(FileName), fileFor(SealedFileName), passphrase);

    public async Task<VoiceProfile> LoadAsync(CancellationToken cancellationToken)
    {
        var key = passphrase?.Invoke();
        if (sealedFile is not null && !string.IsNullOrEmpty(key))
        {
            var read = await sealedFile.ReadAsync(key, cancellationToken);
            read.ThrowIfUnreachable();
            if (read.Status == SealedReadStatus.Loaded)
                return Parse(read.Plaintext);
            if (read.Status != SealedReadStatus.Absent)
                return VoiceProfile.Empty;
        }

        return Parse(await file.ReadAsync(cancellationToken));
    }

    public async Task SaveAsync(VoiceProfile profile, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(profile, SerializerOptions);
        if (sealedFile is null)
        {
            await file.WriteAsync(json, cancellationToken);
            return;
        }

        var key = passphrase!();
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("The voice profile was not written: it is sealed under the vault passphrase, and this app has none.");

        var read = await sealedFile.ReadAsync(key, cancellationToken);
        read.ThrowIfUnreachable();
        var written = await sealedFile.WriteAsync(json, key, read, SealedWriteOptions.Default, cancellationToken);
        written.ThrowIfFailed();
        if (!written.Succeeded)
            throw new IOException("The voice profile was not written: " + written.Detail);

        // The sealed copy is confirmed, so the plaintext one (with its passages) goes.
        if (file is IDriveFileTrash trash && await file.ReadAsync(cancellationToken) is not null)
            await trash.TrashAsync(cancellationToken);
    }

    private static VoiceProfile Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return VoiceProfile.Empty;

        try
        {
            if (IsOldShape(content))
                return VoiceProfile.Empty with { NeedsRelearn = true };

            var profile = JsonSerializer.Deserialize<VoiceProfile>(content, SerializerOptions);
            return profile is null ? VoiceProfile.Empty : Normalize(profile);
        }
        catch (JsonException)
        {
            // A hand-mangled or partially-written file shouldn't strand every app on an exception;
            // behave as if no voice were saved and let the next successful refine overwrite it.
            return VoiceProfile.Empty;
        }
    }

    /// <summary>
    /// A document written before the refit, recognised by the two fields that only it has. Checked
    /// against the document rather than against a deserialised record, because the old shape's
    /// examples do not survive deserialisation at all — the parse throws first, and a caught
    /// exception cannot tell "written by an older version" from "corrupt".
    /// </summary>
    private static bool IsOldShape(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return false;

        return Names(document).Any(name =>
            name.Equals("summary", StringComparison.OrdinalIgnoreCase)
            || name.Equals("signatureMoves", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Names(JsonDocument document) =>
        document.RootElement.EnumerateObject().Select(property => property.Name);

    /// <summary>
    /// A profile written before a list field existed deserializes that field as null; replace any
    /// null list with an empty one so consumers never have to null-check.
    /// </summary>
    private static VoiceProfile Normalize(VoiceProfile profile) => profile with
    {
        Traits = Safe(profile.Traits),
        Examples = profile.Examples ?? [],
        Avoid = Safe(profile.Avoid),
        Cadence = Safe(profile.Cadence),
        Punctuation = Safe(profile.Punctuation),
    };

    private static IReadOnlyList<string> Safe(IReadOnlyList<string>? list) => list ?? [];
}
