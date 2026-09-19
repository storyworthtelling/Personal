using System.Text.Json;

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

    private readonly IDriveTextFile file;

    public DriveVoiceProfileStore(IDriveTextFile file)
    {
        this.file = file;
    }

    public async Task<VoiceProfile> LoadAsync(CancellationToken cancellationToken)
    {
        var content = await file.ReadAsync(cancellationToken);
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

    public Task SaveAsync(VoiceProfile profile, CancellationToken cancellationToken) =>
        file.WriteAsync(JsonSerializer.Serialize(profile, SerializerOptions), cancellationToken);

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
