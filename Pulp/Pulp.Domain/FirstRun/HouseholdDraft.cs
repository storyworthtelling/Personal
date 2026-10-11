using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;
using Pulp.Domain.Settings;

namespace Pulp.Domain.FirstRun;

/// <summary>One person typed into the draft: the name as cleaned, the id it was given among the draft, and the child flag.</summary>
public sealed record DraftPerson(ViewerId Id, string Name, bool IsChild);

/// <summary>An add or a remove on the draft: the draft afterwards, and the one sentence when the add was refused.</summary>
public sealed record DraftEdit(HouseholdDraft Draft, string? Problem);

/// <summary>
/// What saving the draft writes: the viewers, a name record for each, the rules record of each child
/// (a child with none cannot be built for — the hand-off planner refuses the whole household), and who
/// was left out as already there.
/// </summary>
public sealed record DraftCommit(
    IReadOnlyList<HouseholdViewer> Viewers, IReadOnlyList<ViewerNameEntry> Names, IReadOnlyList<ChildRules> ChildRules, IReadOnlyList<string> AlreadyThere);

/// <summary>The household document after the draft is written, and what was written.</summary>
public sealed record DraftWritten(HouseholdDocument Document, DraftCommit Commit);

/// <summary>
/// The pure half of writing the first-run people: read the roster as it stands in the document, commit
/// the draft against it, and put every viewer, name and child's rules into the document in one go —
/// the caller saves the result as a single edit, so no child is ever on the roster without rules.
/// </summary>
public static class HouseholdDraftWrite
{
    public static DraftWritten Apply(HouseholdDocument document, HouseholdDraft draft, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(draft);
        var view = HouseholdView.Of(document);
        var viewers = view.Roster?.Viewers ?? [];
        var commit = draft.Commit(viewers.Select(viewer => viewer.Id), viewers.ToDictionary(viewer => viewer.Id, viewer => view.NameOf(viewer.Id)));
        foreach (var viewer in commit.Viewers)
        {
            document = HouseholdRecords.Put(document, HouseholdMaps.Viewer, viewer, stamp, writer);
        }

        foreach (var name in commit.Names)
        {
            document = HouseholdRecords.Put(document, HouseholdMaps.ViewerName, name, stamp, writer);
        }

        foreach (var rules in commit.ChildRules)
        {
            document = HouseholdRecords.Put(document, HouseholdMaps.ChildRules, rules, stamp, writer);
        }

        return new DraftWritten(document, commit);
    }
}

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

    private HouseholdDraft(IReadOnlyList<DraftPerson> people) => People = people;

    public static HouseholdDraft Empty { get; } = new([]);

    public IReadOnlyList<DraftPerson> People { get; }

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

        var person = new DraftPerson(ViewerNaming.Mint(name, taken), ViewerNaming.Clean(name)!, isChild);
        return new DraftEdit(new HouseholdDraft([.. People, person]), null);
    }

    public HouseholdDraft Remove(ViewerId id) =>
        new(People.Where(person => person.Id != id).ToList());

    /// <summary>
    /// What to write now. The draft's ids were minted from the people typed so far; Drive may have adopted
    /// a roster since (the sync runs while the owner types), so each id is minted again against
    /// <paramref name="rosterNow"/> — the whole roster as it stands — and the people already added in this
    /// pass. Writing a draft id that is now someone else's would merge a new person into their records. A
    /// person whose name is already there (by the id read as a name, or by <paramref name="namesNow"/>) is
    /// left out and reported, not added twice.
    /// </summary>
    public DraftCommit Commit(IEnumerable<ViewerId> rosterNow, IReadOnlyDictionary<ViewerId, string>? namesNow = null)
    {
        ArgumentNullException.ThrowIfNull(rosterNow);
        var taken = rosterNow.ToList();
        var known = taken.Select(id => namesNow is not null && namesNow.TryGetValue(id, out var name) ? name : ViewerNaming.Display(id)).ToList();
        var viewers = new List<HouseholdViewer>();
        var names = new List<ViewerNameEntry>();
        var rules = new List<ChildRules>();
        var already = new List<string>();
        foreach (var person in People)
        {
            if (known.Contains(person.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                already.Add(person.Name);
                continue;
            }

            var id = ViewerNaming.Mint(person.Name, taken);
            taken.Add(id);
            viewers.Add(new HouseholdViewer(id, person.IsChild));
            names.Add(new ViewerNameEntry(id, person.Name));
            if (person.IsChild)
            {
                rules.Add(ChildRuleEdits.NewChild(id));
            }
        }

        return new DraftCommit(viewers, names, rules, already);
    }
}

/// <summary>
/// Keeps the draft across Android rebuilding the screen (a rotation, the process reclaimed while the
/// account picker was up): one line a person, the child flag then the name. Anything it cannot read is
/// an empty draft, never a half one; the ids are minted again from the names on the way back in.
/// </summary>
public static class HouseholdDraftCodec
{
    private const char Child = 'c';
    private const char Adult = 'a';

    public static string Encode(HouseholdDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return string.Join('\n', draft.People.Select(person => (person.IsChild ? Child : Adult) + person.Name));
    }

    public static HouseholdDraft Decode(string? text)
    {
        var draft = HouseholdDraft.Empty;
        foreach (var line in (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 2 || line[0] is not (Child or Adult))
            {
                return HouseholdDraft.Empty;
            }

            var edit = draft.Add(line[1..], line[0] == Child);
            if (edit.Problem is not null)
            {
                return HouseholdDraft.Empty;
            }

            draft = edit.Draft;
        }

        return draft;
    }
}
