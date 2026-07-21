using FocusPhone.Domain.Music;

namespace FocusPhone.Infrastructure.Music;

/// <summary>
/// Maps the raw tag fields a tagging library exposes into a domain <see cref="Track"/>.
/// Pure and unit-tested; keeps TagLibSharp out of the tested path (the reader adapts
/// TagLibSharp's fields to these arguments). The Unknown-* fallbacks live on
/// <see cref="Track"/>, so blank/missing values are passed through as empty.
/// </summary>
public static class TagMapping
{
    public static Track ToTrack(
        string fileId,
        string? title,
        IReadOnlyList<string>? performers,
        string? album,
        IReadOnlyList<string>? albumArtists,
        uint trackNumber,
        uint discNumber,
        IReadOnlyList<string>? genres,
        TimeSpan duration,
        string? artRef = null) =>
        new(
            fileId,
            title ?? string.Empty,
            First(performers),
            album ?? string.Empty,
            First(albumArtists),
            (int)trackNumber,
            discNumber == 0 ? 1 : (int)discNumber,
            First(genres),
            duration,
            artRef);

    private static string First(IReadOnlyList<string>? values) =>
        values is { Count: > 0 } ? values[0] : string.Empty;
}
