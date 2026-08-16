namespace Storyworthtelling.Voice;

/// <summary>
/// A cheap, synchronous cache of "well-known Drive file name → its stable Drive file id", so a file the
/// owner MOVES in Drive is still found by id rather than only by a name search (thread #13). Seeded from —
/// and persisted back to — the encrypted vault so the mapping survives the FocusPhone cutover's factory
/// reset. Optional on <see cref="DriveTextFile"/>: with no store wired, resolution falls back to the name
/// search exactly as before, so nothing regresses before the wiring lands in an app.
/// </summary>
public interface IDriveFileIdStore
{
    /// <summary>The last-known Drive id for this well-known file name, or null if none captured yet.</summary>
    string? Get(string fileName);

    /// <summary>Records the resolved Drive id for this file name (capture / re-capture after a move).</summary>
    void Set(string fileName, string fileId);
}
