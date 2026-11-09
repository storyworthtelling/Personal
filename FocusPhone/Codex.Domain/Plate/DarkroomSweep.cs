namespace Codex.Domain.Plate;

/// <summary>A file found in Plate's private Darkroom folder, and when it was last written.</summary>
public sealed record SweptFile(string Name, DateTime LastModified);

/// <summary>
/// Puts back what a crash left behind (error-handling §18). The Darkroom holds the only copy of each photo, and
/// the row naming a capture is written after the picture is: a process killed in between left the picture in the
/// folder where nothing showed it. A picture the room does not list comes back as a photo dated by its file.
/// A picture the owner deleted whose undo window a crash cut short comes back too, which is the right side to err
/// on when the alternative is losing the only copy of a photo.
/// </summary>
public static class DarkroomSweep
{
    private static readonly string[] PictureExtensions = [".jpg", ".jpeg"];

    public static Darkroom Recover(Darkroom room, IEnumerable<SweptFile> files)
    {
        var listed = room.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var recovered = room;
        foreach (var file in files)
        {
            if (!listed.Contains(file.Name)
                && PictureExtensions.Any(extension => file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            {
                recovered = recovered.Add(DarkroomItem.Photo(file.Name, file.LastModified));
            }
        }

        return recovered;
    }
}
