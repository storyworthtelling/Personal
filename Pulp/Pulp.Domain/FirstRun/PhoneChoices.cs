using Pulp.Domain.Core;
using Pulp.Domain.People;

namespace Pulp.Domain.FirstRun;

/// <summary>One person "This phone is …" can name: their id and how the page writes them.</summary>
public sealed record PhoneChoice(ViewerId Id, string Name);

/// <summary>
/// Who first run offers for "This phone is …" (D97): the people <see cref="ThisPhone"/> would record,
/// asked of it rather than decided here, so the offer and the record cannot disagree.
/// </summary>
public static class PhoneChoices
{
    /// <param name="nameOf">How a person is written — the household's name record (<c>HouseholdView.NameOf</c>); the id read as a name when none is given.</param>
    public static IReadOnlyList<PhoneChoice> For(HouseholdRoster? roster, Func<ViewerId, string>? nameOf = null) =>
        roster is null
            ? []
            : roster.Viewers
                .Where(person => ThisPhone.CanBeRecorded(roster, person.Id))
                .Select(person => new PhoneChoice(person.Id, (nameOf ?? ViewerNaming.Display)(person.Id)))
                .ToList();
}
