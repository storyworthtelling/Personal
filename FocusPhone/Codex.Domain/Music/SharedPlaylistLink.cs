using System.Text.RegularExpressions;

namespace Codex.Domain.Music;

/// <summary>
/// The front door of a playlist import: a share arrives as free text, not as a
/// tidy identifier. The share sheet wraps the link in a sentence and a footer, a
/// localized client puts its market in the path, and the app-to-app form has no
/// scheme at all — so the id has to be found in the text rather than parsed from it.
///
/// Spotify-shaped because that is what gets shared; a second service adds a pattern
/// here rather than a second front door.
/// </summary>
public static partial class SharedPlaylistLink
{
    /// <summary>
    /// The playlist id inside this shared text, or null if there is not one.
    ///
    /// Deliberately narrow: an album or a track link returns null rather than being
    /// guessed at, because importing the wrong thing is worse than saying the share
    /// was not a playlist.
    /// </summary>
    public static string? Parse(string? sharedText)
    {
        if (string.IsNullOrWhiteSpace(sharedText))
        {
            return null;
        }

        var match = PlaylistId().Match(sharedText);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"(?:open\.spotify\.com/(?:intl-[A-Za-z-]+/)?playlist/|spotify:playlist:)([A-Za-z0-9]+)")]
    private static partial Regex PlaylistId();
}
