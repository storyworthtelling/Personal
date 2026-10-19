using System.Text.Json;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// A flag in a file: pen ink has come through the cloud's PDF at least once on this install. It is the one thing that
/// turns "no ink anywhere" from a missing round trip (R3) into the owner's silence, so it is only ever set by a read
/// that saw ink. A file that cannot be read says "not seen", which is the safe direction: one more morning of caution,
/// not a skip recorded on faith.
/// </summary>
public sealed class FileInkProof : IInkProof
{
    private static readonly object FileLock = new();

    private readonly string path;

    public FileInkProof(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public bool PenMarksSeen
    {
        get
        {
            lock (FileLock)
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    return document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty("penMarksSeen", out var seen)
                        && seen.ValueKind == JsonValueKind.True;
                }
                catch (JsonException)
                {
                    // A damaged flag proves nothing; the next record writes a good one.
                    return false;
                }
            }
        }
    }

    public void RecordPenMarksSeen()
    {
        lock (FileLock)
        {
            AtomicFile.WriteAllText(path, "{\"penMarksSeen\":true}");
        }
    }
}
