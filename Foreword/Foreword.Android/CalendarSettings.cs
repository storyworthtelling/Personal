using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;

namespace Foreword.Android;

/// <summary>
/// Reads and writes what the three calendar screens edit.
///
/// <para><b>The stored shape does not change.</b> The mapping is still
/// <see cref="CalendarMappingOptions"/> serialised into the same preference the JSON editor wrote,
/// which is also the value the secrets vault carries under a frozen key. Replacing the editor with
/// screens must not orphan a configuration the owner already has, or restore one from the vault
/// into a shape nothing reads — so the screens edit the same document, and the new per-calendar
/// rules are additive lists that an older file simply leaves empty.</para>
///
/// <para>There is <b>no Save</b>. Every change writes immediately, which is §5's inline state
/// change and the rule Score gives for its own settings: leaving by a tab never blurs anything, so
/// a Save button is one that usually does nothing.</para>
/// </summary>
public sealed class CalendarSettings
{
    public CalendarMappingOptions Mapping { get; private set; } = new();

    public WorkScheduleOptions Work { get; private set; } = new();

    public IReadOnlySet<string> SeenCalendarIds { get; private set; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The columns this account set up, in print order. Every screen walks this rather than a list of
    /// people of its own, so a column added on the Columns screen appears everywhere at once.
    /// </summary>
    public ColumnLayout Layout => Mapping.Layout();

    public void Reload()
    {
        // ReadMapping migrates a document an older app wrote, exactly as the delivery side does —
        // the screens and the morning must never see two layouts of one document.
        Mapping = CalendarMappingCodec.ReadMapping(CredentialStore.CalendarMappingJson) ?? new();

        // A schedule never saved starts from the defaults for whoever this account's people are.
        Work = Read<WorkScheduleOptions>(CredentialStore.WorkScheduleJson)
            ?? WorkScheduleOptions.Defaults(Mapping.Layout());
        SeenCalendarIds = Read<List<string>>(CredentialStore.SeenCalendarIds) is { } seen
            ? seen.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    public void SaveMapping() =>
        CredentialStore.CalendarMappingJson = CalendarMappingCodec.Write(Mapping);

    public void SaveWork() =>
        CredentialStore.WorkScheduleJson = CalendarMappingCodec.Write(Work);

    public void RememberSeen(IEnumerable<string> present)
    {
        SeenCalendarIds = NewCalendars.Remember(present);
        CredentialStore.SeenCalendarIds = CalendarMappingCodec.Write(SeenCalendarIds.ToList());
    }

    // ---- the rule for one calendar ----

    /// <summary>What the row says on the right: the column, or the shape of the rule.</summary>
    public string RuleName(string id)
    {
        if (IsSplit(id)) return "Split";
        if (Mapping.EveryoneCalendars.Contains(id)) return "Everyone";
        if (Mapping.NotPrintedCalendars.Contains(id)) return "Not printed";

        return Mapping.CalendarColumns.TryGetValue(id, out var column) ? column.ToString() : string.Empty;
    }

    /// <summary>
    /// The line under the name. For a split calendar it names the words it routes on, so the rule
    /// is visible from the list without opening it.
    /// </summary>
    public string DescribeRule(string id) =>
        IsSplit(id) ? "Split by name · " + string.Join(", ", SplitWords()) : string.Empty;

    public bool IsSplit(string id) =>
        Mapping.SplitCalendars.Contains(id)
        || (!string.IsNullOrEmpty(Mapping.FamilyCalendarId) && id == Mapping.FamilyCalendarId);

    /// <summary>
    /// Sets one calendar's rule, clearing whatever it had. Every rule is exclusive: a calendar
    /// cannot be both split and not printed, and leaving a stale entry behind is how a screen starts
    /// disagreeing with the page it configures.
    /// </summary>
    public void SetRule(string id, PersonColumn? column, bool everyone, bool notPrinted, bool split)
    {
        Mapping.CalendarColumns.Remove(id);
        Mapping.EveryoneCalendars.Remove(id);
        Mapping.NotPrintedCalendars.Remove(id);
        Mapping.SplitCalendars.Remove(id);

        // The old single-calendar shorthand is cleared too when that calendar is re-answered,
        // otherwise it would quietly override the new rule.
        if (Mapping.FamilyCalendarId == id)
        {
            Mapping.FamilyCalendarId = string.Empty;
        }

        if (split) Mapping.SplitCalendars.Add(id);
        else if (everyone) Mapping.EveryoneCalendars.Add(id);
        else if (notPrinted) Mapping.NotPrintedCalendars.Add(id);
        else if (column is { } c) Mapping.CalendarColumns[id] = c;

        SaveMapping();
    }

    // ---- the words a split routes on ----

    /// <summary>
    /// The words that route to one person. The list is the stored one, so an edit to it is an edit
    /// to the settings; a person with none yet gets an empty list to fill.
    /// </summary>
    public List<string> WordsFor(PersonColumn person)
    {
        if (!Mapping.Keywords.TryGetValue(person, out var words))
        {
            words = [];
            Mapping.Keywords[person] = words;
        }

        return words;
    }

    private IEnumerable<string> SplitWords() =>
        Layout.People.SelectMany(WordsFor).Where(w => !string.IsNullOrWhiteSpace(w)).Distinct();

    // ---- the columns themselves ----

    // Each edit saves both documents it can touch: a column's name is also how a work shift refers
    // to it, so renaming or removing one without saving the schedule would leave a shift pointing
    // at nobody. A refused edit (a blank name, one already taken, Watch) saves nothing.

    public bool AddColumn(string name, bool hasAccount) =>
        SaveIf(ColumnEdits.Add(Mapping, name, hasAccount));

    public bool RenameColumn(PersonColumn column, string name) =>
        SaveIf(ColumnEdits.Rename(Mapping, Work, column, name));

    public bool RemoveColumn(PersonColumn column) =>
        SaveIf(ColumnEdits.Remove(Mapping, Work, column));

    public bool MoveColumn(PersonColumn column, int delta) =>
        SaveIf(ColumnEdits.Move(Mapping, column, delta));

    public void SetHasAccount(PersonColumn column, bool hasAccount)
    {
        ColumnEdits.SetHasAccount(Mapping, column, hasAccount);
        SaveMapping();
    }

    private bool SaveIf(bool changed)
    {
        if (changed)
        {
            SaveMapping();
            SaveWork();
        }

        return changed;
    }

    // One codec, shared with the delivery side. Two would eventually disagree about how a column is
    // spelled on the wire, and the screens would quietly stop reading what the app wrote.
    private static T? Read<T>(string json) where T : class => CalendarMappingCodec.Read<T>(json);
}
