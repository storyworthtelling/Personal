using Pulp.Domain.Core;
using Pulp.Domain.People;

namespace Pulp.Domain.FirstRun;

/// <summary>An add or a remove on the draft: the draft afterwards, and the one sentence when the add was refused.</summary>
public sealed record DraftEdit(HouseholdDraft Draft, string? Problem);

/// <summary>
/// The household being set up at first run, before it is written (D142): people added by name with a
/// child flag. Immutable, so a refused add hands back the very draft it was given. It is only saved
/// with at least one adult on it, because a phone belongs to an adult (D97) and "This phone is …"
/// would otherwise have nobody to offer.
/// </summary>
public sealed class HouseholdDraft
{
    public const string NobodyYet = "Add the people who watch, adults and children.";

    public const string NeedsAnAdult = "Add at least one adult; each phone belongs to an adult.";

    private HouseholdDraft(IReadOnlyList<HouseholdViewer> people) => People = people;

    public static HouseholdDraft Empty { get; } = new([]);

    public IReadOnlyList<HouseholdViewer> People { get; }

    /// <summary>Why this draft cannot be saved yet, or null when it can.</summary>
    public string? SaveProblem =>
        People.Count == 0 ? NobodyYet
        : People.All(person => person.IsChild) ? NeedsAnAdult
        : null;

    public DraftEdit Add(string name, bool isChild)
    {
        var taken = People.Select(person => person.Id).ToList();
        var problem = ViewerNaming.Check(name, taken);
        if (problem is not null)
        {
            return new DraftEdit(this, problem);
        }

        var person = new HouseholdViewer(ViewerNaming.IdFor(name, taken), isChild);
        return new DraftEdit(new HouseholdDraft([.. People, person]), null);
    }

    public HouseholdDraft Remove(ViewerId id) =>
        new(People.Where(person => person.Id != id).ToList());
}
