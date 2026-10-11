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

    public static IReadOnlyList<WatchedSitting> Resolve(
        SessionMeaning meaning, ServiceCatalogue catalogue, IEnumerable<WatchedSitting> sittings)
    {
        ArgumentNullException.ThrowIfNull(meaning);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(sittings);

        if (meaning.Kind is not (SessionEventKind.Ended or SessionEventKind.StoppedShort)
            || catalogue.ByPackage(meaning.Package) is not { } service)
        {
            return [];
        }

        var handOffs = sittings
            .Where(sitting => sitting.Sitting.Evidence == WatchSource.HandOff && sitting.Sitting.Service == service.Id)
            .ToList();
        if (handOffs.Count == 0)
        {
            return [];
        }

        var latest = handOffs.Max(sitting => sitting.Sitting.At);
        var since = meaning.Observation.At - latest;
        if (since < TimeSpan.Zero || since > HandOffWindow)
        {
            return [];
        }

        var observation = meaning.Observation;
        return handOffs
            .Where(sitting => sitting.Sitting.At == latest)
            .DistinctBy(sitting => sitting.Viewer)
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
}
