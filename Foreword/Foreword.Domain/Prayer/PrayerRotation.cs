using System.Globalization;

namespace Foreword.Domain.Prayer;

/// <summary>
/// A standing list the owner wants to pray through one at a time — a missionary list, the elders —
/// where one member prints each morning and the list comes round again.
///
/// <para>Deliberately <b>not</b> a held request. A request has a length of time attached and comes
/// off the list when it is answered; a rotation member has neither, and printing "held 6 weeks"
/// beside a name that was simply next would be the page making something up.</para>
///
/// <para>Rotations are the half of the prayer list the owner keeps on the phone. One-off requests
/// are written on the journal page, because the page is where writing belongs; a list that has to
/// be reordered and paused is a thing to edit, and editing belongs on a screen.</para>
/// </summary>
public sealed record PrayerRotation
{
    public required string Id { get; init; }

    /// <summary>What the section is called on the page. Not prayer content — the owner's own label.</summary>
    public required string Name { get; init; }

    public IReadOnlyList<PrayerText> Members { get; init; } = [];

    /// <summary>The morning the rotation started, which is what the pick counts from.</summary>
    public required DateOnly Began { get; init; }

    /// <summary>Putting a list down for a season without losing the list.</summary>
    public bool Paused { get; init; }
}

/// <summary>One morning's member of one rotation, ready to draw.</summary>
public readonly record struct RotationPicked(string Rotation, PrayerText Member, string Position);

/// <summary>
/// Whose turn it is this morning.
///
/// <para><b>Arithmetic on the date, never a stored cursor.</b> A cursor is a second thing to keep in
/// step with the page, and it fails in the one way that matters: a morning that does not deliver
/// still advances it, so a name is skipped and nobody ever finds out. Counting from the start date
/// means a failed morning costs that morning's name and nothing else.</para>
/// </summary>
public static class RotationPick
{
    public static RotationPicked? For(PrayerRotation rotation, DateOnly today)
    {
        if (rotation.Paused || rotation.Members.Count == 0)
        {
            return null;
        }

        var elapsed = today.DayNumber - rotation.Began.DayNumber;
        if (elapsed < 0)
        {
            // A start date in the future has not begun. Nothing, rather than a negative index.
            return null;
        }

        var index = elapsed % rotation.Members.Count;

        return new RotationPicked(
            rotation.Name,
            rotation.Members[index],
            $"{(index + 1).ToString(CultureInfo.InvariantCulture)} of {rotation.Members.Count.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Every rotation the owner keeps, in the order they keep them. Two lists are two things to pray
    /// through; taking only the first would be the second one silently never appearing.
    /// </summary>
    public static IReadOnlyList<RotationPicked> ForAll(
        IReadOnlyList<PrayerRotation> rotations, DateOnly today) =>
        [.. rotations.Select(rotation => For(rotation, today)).OfType<RotationPicked>()];
}
