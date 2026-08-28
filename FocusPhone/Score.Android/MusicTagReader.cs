using Codex.Domain.Music;
using Codex.Infrastructure.Music;

namespace Score;

/// <summary>
/// Reads embedded tags from an audio stream via TagLibSharp and maps them to a
/// domain <see cref="Track"/> (the pure <see cref="TagMapping"/> does the field
/// logic). Embedded cover art is written to an app-private cache file and its path
/// stored on the track. TagLibSharp seeks as needed, so a seekable stream over
/// Drive avoids downloading whole files.
/// </summary>
public sealed class MusicTagReader
{
    /// <summary>
    /// Which fields this reader knows how to read. Bumped whenever it starts reading
    /// one it did not before — tempo, at version 2 — so a cache filled by an older
    /// reader is re-read once instead of carrying an empty column until every file
    /// happens to be edited.
    /// </summary>
    public const int Version = 2;

    private readonly string artCacheDirectory;

    public MusicTagReader(string artCacheDirectory) => this.artCacheDirectory = artCacheDirectory;

    public Track Read(string fileId, string name, Stream stream)
    {
        using var file = TagLib.File.Create(new StreamFileAbstraction(name, stream));
        var tag = file.Tag;
        return TagMapping.ToTrack(
            fileId,
            tag.Title,
            tag.Performers,
            tag.Album,
            tag.AlbumArtists,
            tag.Track,
            tag.Disc,
            tag.Genres,
            file.Properties?.Duration ?? TimeSpan.Zero,
            SaveArtwork(fileId, tag),
            tag.BeatsPerMinute);
    }

    private string? SaveArtwork(string fileId, TagLib.Tag tag)
    {
        var picture = tag.Pictures?.FirstOrDefault();
        if (picture?.Data?.Data is not { Length: > 0 } bytes)
        {
            return null;
        }

        Directory.CreateDirectory(artCacheDirectory);
        var path = Path.Combine(artCacheDirectory, SafeName(fileId) + ".art");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string SafeName(string fileId) =>
        string.Concat(fileId.Select(character => char.IsLetterOrDigit(character) ? character : '_'));

    private sealed class StreamFileAbstraction(string name, Stream stream) : TagLib.File.IFileAbstraction
    {
        public string Name { get; } = name;

        public Stream ReadStream { get; } = stream;

        public Stream WriteStream => throw new NotSupportedException();

        public void CloseStream(Stream stream) => stream.Dispose();
    }
}
