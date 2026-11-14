namespace Codex.Domain.Music;

/// <summary>
/// Whether the Playing screen's cover needs decoding again (efficiency audit §5). It used to decode on every
/// one-second tick; the cover depends only on the track, the owner's override, the embedded art and the podcast feed,
/// so it is decoded when one of those changes, or when the view it was shown on has been rebuilt.
/// </summary>
public sealed class ArtworkRefresh
{
    private string? last;

    public static string KeyOf(string? mediaId, string? overrideUri, int embeddedLength, string? feedId) =>
        string.Join('\u001f', mediaId ?? string.Empty, overrideUri ?? string.Empty, embeddedLength, feedId ?? string.Empty);

    /// <summary>True the first time a key is seen since the last different one, or since <see cref="Forget"/>.</summary>
    public bool Due(string key)
    {
        if (key == last)
        {
            return false;
        }
        last = key;
        return true;
    }

    public void Forget() => last = null;
}
