namespace Storyworthtelling.Voice;

// A single named text file in the owner's Google Drive — the physical home of the shared voice profile.
// Kept a narrow seam so DriveVoiceProfileStore's real logic (serialize, absent-file handling, round-trip)
// is testable without Google, and so the Drive glue that implements it stays thin and swappable.
public interface IDriveTextFile
{
    // The file's contents, or null when it doesn't exist yet.
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    // Creates or overwrites the file with the given contents.
    Task WriteAsync(string content, CancellationToken cancellationToken);
}
