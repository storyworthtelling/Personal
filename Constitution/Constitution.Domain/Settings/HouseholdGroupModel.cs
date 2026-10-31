using System.Text;
using Constitution.Domain.Calendar;
using Constitution.Domain.Household;
using Constitution.Domain.Time;

namespace Constitution.Domain.Settings;

/// <summary>One person in the Household list: name, tag and one muted line. No birthdate member exists.</summary>
public sealed record HouseholdPersonRow(PersonId Id, string Name, string Tag, string? MutedLine, bool Editable);

/// <summary>One adult's pickup switch.</summary>
public sealed record HouseholdPickupRow(string Name, bool OnPickup, bool Editable);

/// <summary>A time setting in mono. <see cref="IsAssumed"/> means nothing was set and the run uses <see cref="Value"/>.</summary>
public sealed record HouseholdTimeRow(string Label, string Helper, string Value, bool IsAssumed);

/// <summary>One calendar choice. <see cref="Id"/> is null while unset.</summary>
public sealed record HouseholdCalendarRow(string Key, string Label, string? Id, string StatusText);

/// <summary>
/// What the Household group says on Settings (book p.17): the people with an adult/child tag and one muted line, the children's
/// bedtime in mono, "Add a person", and the choices the weekly run plans by. Pure, so the screen is a thin painter.
///
/// <para>The people come from the roster (<see cref="RosterView"/>, which has no birthdate); with no roster (<c>null</c>: the household
/// file is not wired on this phone) the group says so and the choices are still editable, read from and written to the planning-setup file.
/// A setting not chosen is shown as assumed with the value the run will use, never as if it were set. On the partner's install the
/// roster is read-only (D74) with the plain reason from <see cref="RosterEditor"/>; the choices are this phone's own.</para>
/// </summary>
public sealed class HouseholdGroupModel
{
    public const string NoHouseholdFileSentence =
        "The household file is not set up on this phone yet, so the people are not listed. The choices below are kept on this phone.";

    private const string ChildTag = "child";
    private const string AdultTag = "adult";
    private const int LongestDinnerMinutes = 240;

    private HouseholdGroupModel()
    {
    }

    public string Header => Summary.Length == 0 ? "Household" : "Household " + Summary;

    /// <summary>The muted half of the closed group's header: "4 people · bedtime 7:30 pm".</summary>
    public string Summary { get; private init; } = string.Empty;

    /// <summary>The sentence shown where the people would be when there is no roster; null otherwise.</summary>
    public string? RosterStatus { get; private init; }

    /// <summary>The line shown where the partner would edit; null for the owner.</summary>
    public string? ReadOnlyNote { get; private init; }

    public IReadOnlyList<HouseholdPersonRow> People { get; private init; } = [];

    public bool CanAddPerson { get; private init; }

    public string AddLabel => "Add a person";

    public IReadOnlyList<HouseholdPickupRow> Pickup { get; private init; } = [];

    public HouseholdTimeRow Bedtime { get; private init; } = new("Children's bedtime", string.Empty, string.Empty, true);

    public HouseholdTimeRow Dinner { get; private init; } = new("Family dinner", string.Empty, string.Empty, true);

    public IReadOnlyList<HouseholdCalendarRow> Calendars { get; private init; } = [];

    public static HouseholdGroupModel Build(
        RosterView? roster, RosterRole role, HouseholdSetupRead setup, IReadOnlyDictionary<PersonId, string>? dislikeLines = null)
    {
        ArgumentNullException.ThrowIfNull(setup);

        var editor = new RosterEditor(role);
        var saved = (setup as HouseholdSetupRead.Configured)?.Setup;

        var people = (roster?.Entries ?? [])
            .Select(entry => new HouseholdPersonRow(
                entry.Id,
                entry.Name,
                entry.Role == MemberRole.Child ? ChildTag : AdultTag,
                dislikeLines is not null && dislikeLines.TryGetValue(entry.Id, out var line) ? line : null,
                editor.CanEdit))
            .ToList();

        var pickup = roster is not null
            ? roster.Adults.Select(entry => new HouseholdPickupRow(entry.Name, entry.OnPickup, editor.CanEdit)).ToList()
            : (saved?.Adults ?? []).Select(adult => new HouseholdPickupRow(adult.Name, adult.OnPickup ?? true, true)).ToList();

        var bedtime = Row("Children's bedtime", "Nothing is planned in the hour before", saved?.Bedtime, BedtimeRoutine.Assumed.Bedtime);
        var dinner = Row("Family dinner", "Nothing else is planned over it", saved?.DinnerStart, FamilyDinner.Assumed.Start);

        var parts = new List<string>();
        if (roster is not null)
        {
            parts.Add(roster.Entries.Count == 1 ? "1 person" : $"{roster.Entries.Count} people");
        }

        parts.Add(bedtime.IsAssumed ? $"bedtime assumed {bedtime.Value}" : $"bedtime {bedtime.Value}");

        return new HouseholdGroupModel
        {
            Summary = string.Join(" · ", parts),
            RosterStatus = roster is null ? NoHouseholdFileSentence : null,
            ReadOnlyNote = roster is null ? null : editor.ReadOnlyNote,
            People = people,
            CanAddPerson = roster is not null && editor.CanEdit,
            Pickup = pickup,
            Bedtime = bedtime,
            Dinner = dinner,
            Calendars = CalendarRows(roster, saved, pickup),
        };
    }

    /// <summary>A plain sentence when the bedtime cannot be used, else null.</summary>
    public static string? CheckBedtime(TimeOnly bedtime) =>
        bedtime.ToTimeSpan() < BedtimeRoutine.UsualLength
            ? "The bedtime has to be an evening time, at least an hour into the day, so the routine before it fits."
            : null;

    /// <summary>A plain sentence when the dinner cannot be used, else null.</summary>
    public static string? CheckDinner(TimeOnly start, TimeSpan length)
    {
        if (length <= TimeSpan.Zero)
        {
            return "Dinner has to take some time.";
        }

        if (length > TimeSpan.FromMinutes(LongestDinnerMinutes))
        {
            return "Dinner can be at most four hours.";
        }

        return start.ToTimeSpan() + length > TimeSpan.FromDays(1) ? "Dinner has to end before midnight." : null;
    }

    /// <summary>What a saved setup still lacks, in a sentence; empty when nothing is missing.</summary>
    public static string Describe(IReadOnlyList<SetupGap> gaps)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        if (gaps.Count == 0)
        {
            return string.Empty;
        }

        var words = gaps.Select(gap => gap switch
        {
            SetupGap.NoAdults => "an adult",
            SetupGap.NoAdultDeliversHere => "which adult this phone delivers for",
            SetupGap.DeliversHereIsNotAnAdult => "an adult to deliver for",
            SetupGap.FamilyCalendarNotSet => "the family calendar",
            SetupGap.ChildrenCalendarsNotSet => "the children's calendar",
            _ => throw new ArgumentOutOfRangeException(nameof(gaps), gap, "That gap has no words yet."),
        });
        return "Still missing: " + string.Join(", ", words) + ".";
    }

    private static HouseholdTimeRow Row(string label, string helper, TimeOnly? chosen, TimeOnly assumed) =>
        new(label, helper, Clock(chosen ?? assumed), chosen is null);

    private static string Clock(TimeOnly time) => TimeFormats.Clock12(new DateTime(2000, 1, 1, time.Hour, time.Minute, 0));

    private static List<HouseholdCalendarRow> CalendarRows(RosterView? roster, PlanningSetup? saved, IReadOnlyList<HouseholdPickupRow> adults)
    {
        const string notSet = "Not set.";
        var rows = new List<HouseholdCalendarRow>();
        foreach (var adult in adults)
        {
            var id = saved?.CalendarOf(adult.Name);
            rows.Add(new HouseholdCalendarRow("adult:" + adult.Name, adult.Name, id, id ?? notSet));
        }

        var family = saved?.FamilyCalendarId;
        rows.Add(new HouseholdCalendarRow("family", "Family", family,
            family ?? (saved?.NoFamilyCalendar == true ? "None." : notSet)));

        var hasChildren = roster is null || roster.Children.Count > 0;
        if (hasChildren)
        {
            var children = saved?.ChildrenCalendarIds ?? [];
            var joined = children.Count == 0 ? null : string.Join(", ", children);
            rows.Add(new HouseholdCalendarRow("children", "Children", joined, joined ?? notSet));
        }

        return rows;
    }
}

/// <summary>What was wrong with a person sheet.</summary>
public enum PersonProblem
{
    NameMissing,
    NameTooLong,
    ChildOnPickup,
    AddressShape,
    NotOnTheRoster,
}

/// <summary>
/// A person as the add/edit sheet holds them before saving. A null <see cref="Id"/> is a new person. <b>It has no birthdate</b>: the sheet's
/// birthdate is handed to <see cref="HouseholdPersonSheet.Check"/> as the roster's own value type, so no list-shaped type can hold one.
/// </summary>
public sealed record HouseholdPersonDraft(PersonId? Id, string Name, MemberRole Role, bool OnPickup, string? GoogleAddress);

/// <summary>The sheet's check: the person to save, or the problems, or the roster's refusal.</summary>
public sealed record HouseholdPersonCheck(RosterPerson? Person, IReadOnlyList<PersonProblem> Problems, RosterRefusal? Refusal)
{
    public bool CanSave => Person is not null;
}

/// <summary>Checks the add/edit person sheet with the roster's own rules, so the screen can say what is wrong before saving.</summary>
public static class HouseholdPersonSheet
{
    public static HouseholdPersonCheck Check(
        RosterEditor editor, RosterView roster, HouseholdPersonDraft draft, Birthdate? birthdate = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(draft);

        if (!editor.CanEdit)
        {
            return new HouseholdPersonCheck(null, [],
                new RosterRefusal(RosterRefusalReason.PartnerInstallIsReadOnly, RosterEditor.PartnerReadOnlySentence));
        }

        var name = draft.Name?.Trim() ?? string.Empty;
        var problems = new List<PersonProblem>();
        if (name.Length == 0)
        {
            problems.Add(PersonProblem.NameMissing);
        }
        else if (name.Length > RosterPerson.MaxNameLength)
        {
            problems.Add(PersonProblem.NameTooLong);
        }

        if (draft.Role == MemberRole.Child && draft.OnPickup)
        {
            problems.Add(PersonProblem.ChildOnPickup);
        }

        if (problems.Count > 0)
        {
            return new HouseholdPersonCheck(null, problems, null);
        }

        try
        {
            var person = new RosterPerson(
                draft.Id ?? NewId(name, roster),
                name,
                draft.Role,
                birthdate,
                draft.OnPickup,
                draft.GoogleAddress);
            return new HouseholdPersonCheck(person, [], null);
        }
        catch (ArgumentException failure) when (failure.ParamName == nameof(RosterPerson.GoogleAddress))
        {
            // The roster's own rule said the address is the wrong shape; the sheet says so in its own words.
            return new HouseholdPersonCheck(null, [PersonProblem.AddressShape], null);
        }
    }

    private static PersonId NewId(string name, RosterView roster)
    {
        var slug = new StringBuilder();
        foreach (var letter in name.ToLowerInvariant())
        {
            if (letter is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(letter);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var stem = slug.ToString().Trim('-');
        if (stem.Length == 0)
        {
            stem = "person";
        }

        stem = stem[..Math.Min(stem.Length, PersonId.MaxLength - 6)].TrimEnd('-');
        var taken = roster.Entries.Select(entry => entry.Id.Value).ToHashSet(StringComparer.Ordinal);
        var candidate = stem;
        for (var copy = 2; taken.Contains(candidate); copy++)
        {
            candidate = $"{stem}-{copy}";
        }

        return new PersonId(candidate);
    }
}
