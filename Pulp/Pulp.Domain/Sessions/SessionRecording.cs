using System.Collections.Concurrent;
using System.Text;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Services;

namespace Pulp.Domain.Sessions;

/// <summary>
/// Decides where a relayed session's evidence goes (G18): the app's package names a service, and the
/// service names the title or episode the household last handed off to it, because the app's own
/// title string is whatever it chose to publish and cannot be matched to the catalogue. The result is
/// the session sittings to write, one per viewer who shared that hand-off — a child's evidence goes to
/// that child and an adult is never given it (D23) — and nothing else: it builds
/// <see cref="WatchSource.Session"/> sittings and leaves <c>Watched.Decide</c> the only judge.
///
/// <para>Only a closed session is recorded. A progress tick would write a household record every
/// time the app reports, and a changed title is a title the household did not hand off. A session
/// that predates the hand-off, or comes after <see cref="HandOffWindow"/>, is not that hand-off's.</para>
/// </summary>
public static class SessionRecording
{
    /// <summary>How long after a hand-off a closed session still counts as that hand-off's sitting.</summary>
    public static readonly TimeSpan HandOffWindow = TimeSpan.FromHours(12);

    /// <summary>Hand-off sittings this close together (viewers sharing one tap) are one hand-off.</summary>
    public static readonly TimeSpan HandOffTolerance = TimeSpan.FromSeconds(5);

    /// <param name="titleName">The display name of a title id when known; used to compare the relayed title with the hand-off's.</param>
    /// <param name="titleChangedAt">When a title change was last seen for this package; a hand-off made before it is spent.</param>
    public static IReadOnlyList<WatchedSitting> Resolve(
        SessionMeaning meaning,
        ServiceCatalogue catalogue,
        IEnumerable<WatchedSitting> sittings,
        Func<TitleId, string?>? titleName = null,
        DateTimeOffset? titleChangedAt = null)
    {
        ArgumentNullException.ThrowIfNull(meaning);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(sittings);

        if (meaning.Kind is not (SessionEventKind.Ended or SessionEventKind.StoppedShort)
            || catalogue.ByPackage(meaning.Package) is not { } service)
        {
            return [];
        }

        var all = sittings.ToList();
        var handOffs = all
            .Where(sitting => sitting.Sitting.Evidence == WatchSource.HandOff && sitting.Sitting.Service == service.Id)
            .ToList();
        if (handOffs.Count == 0)
        {
            return [];
        }

        var latest = handOffs.Max(sitting => sitting.Sitting.At);
        var group = handOffs.Where(sitting => latest - sitting.Sitting.At <= HandOffTolerance).ToList();
        var start = group.Min(sitting => sitting.Sitting.At);

        var since = meaning.Observation.At - start;
        if (since < TimeSpan.Zero || since > HandOffWindow)
        {
            return [];
        }

        if (titleChangedAt is { } changed && start < changed)
        {
            return [];
        }

        var observation = meaning.Observation;
        return group
            .DistinctBy(sitting => sitting.Viewer)
            .Where(sitting => !EndedAlready(sitting, all, start) && TitleAgrees(meaning.Title, sitting.Sitting.Title, titleName))
            .Select(sitting => new WatchedSitting(
                sitting.Viewer,
                new WatchEvent(
                    sitting.Sitting.Title,
                    sitting.Sitting.Kind,
                    observation.At,
                    service.Id,
                    sitting.Sitting.Episode,
                    WatchSource.Session,
                    observation.Position,
                    observation.Duration,
                    observation.Ended)))
            .ToList();
    }

    // The same viewer's session sitting on the same title and episode already reached its end after this hand-off.
    private static bool EndedAlready(WatchedSitting handOff, List<WatchedSitting> all, DateTimeOffset start) =>
        all.Any(other => other.Viewer == handOff.Viewer
            && other.Sitting.Evidence == WatchSource.Session
            && other.Sitting.Ended
            && other.Sitting.Title == handOff.Sitting.Title
            && other.Sitting.Episode == handOff.Sitting.Episode
            && other.Sitting.At >= start);

    // Blank on either side (or no name lookup) means nothing to compare, and the window alone decides.
    private static bool TitleAgrees(string? relayed, TitleId handedOff, Func<TitleId, string?>? titleName)
    {
        var known = titleName?.Invoke(handedOff);
        var left = Normalise(relayed);
        var right = Normalise(known);
        return left.Length == 0 || right.Length == 0
            || left.Contains(right, StringComparison.Ordinal)
            || right.Contains(left, StringComparison.Ordinal);
    }

    private static string Normalise(string? text)
    {
        var builder = new StringBuilder();
        foreach (var character in text ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }
}

/// <summary>
/// When the app on each package was last seen moving to another title. In-process only (the receiver's own
/// previous-message memory is in-process too, so nothing finer survives a death): a death forgets it, and
/// the title comparison and the ended-episode rule in <see cref="SessionRecording"/> carry on from there.
/// </summary>
public sealed class TitleChangeMemory
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> seen = new(StringComparer.Ordinal);

    public void Seen(string package, DateTimeOffset at) =>
        seen.AddOrUpdate(package, at, (key, existing) => existing >= at ? existing : at);

    public DateTimeOffset? LastSeen(string package) => seen.TryGetValue(package, out var at) ? at : null;
}
