using Foreword.Domain.Prayer;

namespace Foreword.Domain.Interfaces;

/// <summary>
/// Writes the prayer page's own line — the fourth of the morning's four Claude calls, and the only
/// one that may see prayer content.
///
/// <para>Four separate calls is what makes D15's isolation structural rather than careful: this one
/// takes a <see cref="PastoralBrief"/> and returns a <see cref="PrayerText"/>; the brief's opening,
/// the calendar's narrator and the checklist's coach take and return strings, and could not be
/// handed either of these if somebody tried.</para>
/// </summary>
public interface IPastoralComposer
{
    Task<PrayerText> Compose(PastoralBrief brief, CancellationToken cancellationToken);
}
