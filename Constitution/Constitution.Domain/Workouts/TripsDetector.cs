using Constitution.Domain.Calendar;

namespace Constitution.Domain.Workouts;

/// <summary>What an entry says about travel: what kind of trip, and which adults it is for.</summary>
internal sealed record TripEntry(TripKind Kind, IReadOnlyList<Adult> Who);

/// <summary>
/// Decides whether a calendar entry is a trip, and whose. One place, so the detector that reads trips and the calendars the
/// planner is shown cannot disagree about what a trip is.
///
/// <para><b>What is a trip.</b> An all-day entry (a trip is whole days; a flight is not a trip) whose title carries a travel
/// word. A holiday word (holiday, vacation, getaway, cruise, camping, a road or family trip) makes it a
/// <see cref="TripKind.FamilyHoliday"/>; a work word (conference, convention, offsite, summit, a business or work trip) makes it
/// <see cref="TripKind.Work"/>, and a holiday word beats it; the bare words "trip" and "travel" make it a work trip when it is one
/// person's and a holiday when it is more than one's. A title that names a hotel is a hotel booking first, so "Holiday Inn" is not a
/// holiday. Every word is matched as a whole word.</para>
///
/// <para><b>Whose.</b> On an adult's own calendar, theirs, and anyone else it names (by title or invited address). On the family
/// calendar, whoever it names, or both adults if it names no one. Availability is not read: Google's all-day events default to
/// free, and a holiday marked free is still a holiday. A declined or cancelled entry is therefore the head's to leave out,
/// not to hand over as free.</para>
/// </summary>
internal static class TripClassifier
{
    private static readonly string[] HolidayWords =
        ["holiday", "vacation", "vacay", "getaway", "cruise", "camping", "road trip", "family trip"];

    private static readonly string[] WorkWords =
        ["conference", "convention", "business trip", "work trip", "business travel", "work travel", "offsite", "off-site", "summit", "client visit"];

    private static readonly string[] GenericWords = ["trip", "travel", "travelling", "traveling"];

    private static readonly string[] HotelWords = ["hotel", "motel", "inn", "resort", "lodge", "suites"];

    private const string Holiday = "holiday";

    /// <summary>True when the title names a hotel, an inn, a resort or the like.</summary>
    public static bool NamesAHotel(string title) => HotelWords.Any(word => WholeWord.Contains(title, word));

    /// <summary>
    /// What the entry says about travel, or null if it is not a trip. <paramref name="owner"/> is the adult whose own calendar
    /// it is on, or null for the family calendar.
    /// </summary>
    public static TripEntry? Classify(CalendarEntry entry, Adult? owner, IReadOnlyList<Adult> adults)
    {
        if (!entry.IsAllDay)
        {
            return null;
        }

        var hotel = NamesAHotel(entry.Title);
        var holiday = HolidayWords.Any(word => !(hotel && word == Holiday) && WholeWord.Contains(entry.Title, word));
        var work = WorkWords.Any(word => WholeWord.Contains(entry.Title, word));
        var generic = GenericWords.Any(word => WholeWord.Contains(entry.Title, word));
        if (!holiday && !work && !generic)
        {
            return null;
        }

        var named = NamedAdults.In(entry, adults);
        var who = owner is not null
            ? adults.Where(adult => adult.Name == owner.Name || named.Any(other => other.Name == adult.Name)).ToList()
            : named.Count > 0 ? named : adults.ToList();
        var kind = holiday ? TripKind.FamilyHoliday
            : work ? TripKind.Work
            : who.Count == 1 ? TripKind.Work : TripKind.FamilyHoliday;
        return new TripEntry(kind, who);
    }
}

/// <summary>
/// Reads the trips out of the calendars (C-5.2.5): for each adult, the all-day entries on their own calendar and the family
/// calendar that are trips for them (see <see cref="TripClassifier"/>), the same trip on two calendars counted once, and the hotel
/// a work trip names, if the calendar names one on a day of the trip. A calendar that could not be read is said, and the trips
/// on the others are still returned.
///
/// <para>A hotel is any entry, timed or all-day and free or busy, whose title names one (hotel, motel, inn, resort, lodge,
/// suites) on a day of the trip; the earliest wins, and its title is the name handed to the lookup, because the calendar's
/// words are all there is to look up. A holiday gets no hotel: nothing needs one.</para>
/// </summary>
public static class TripDetector
{
    public static TripRead Detect(PlanningRules rules, IHouseholdCalendars calendars, DateOnly firstDay, DateOnly lastDay)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(calendars);
        if (lastDay < firstDay)
        {
            throw new ArgumentOutOfRangeException(nameof(lastDay), lastDay, "The last day is before the first day.");
        }

        var zone = rules.Zone;
        var from = ZoneClock.StartOfDay(firstDay, zone);
        var to = ZoneClock.StartOfDay(lastDay.AddDays(1), zone);
        var family = calendars.FamilyCalendar(from, to);
        var unreadable = new List<UnreadableCalendar>();
        if (!family.IsReadable)
        {
            unreadable.Add(new UnreadableCalendar(CalendarSource.Family, family.UnreadableBecause!));
        }

        var owns = rules.Adults.ToDictionary(adult => adult.Name, adult => calendars.OwnCalendar(adult.Name, from, to), StringComparer.OrdinalIgnoreCase);
        if (owns.Values.FirstOrDefault(read => !read.IsReadable) is { } firstUnreadable)
        {
            unreadable.Insert(0, new UnreadableCalendar(CalendarSource.Own, firstUnreadable.UnreadableBecause!));
        }

        var trips = new Dictionary<string, IReadOnlyList<Trip>>(StringComparer.OrdinalIgnoreCase);
        foreach (var adult in rules.Adults)
        {
            var found = TripsFor(adult, rules.Adults.ToDictionary(other => other, other => owns[other.Name].Entries), family.Entries, rules, zone, firstDay, lastDay);
            if (found.Count > 0)
            {
                trips[adult.Name] = found;
            }
        }

        return new TripRead(trips, unreadable);
    }

    private static List<Trip> TripsFor(
        Adult adult,
        IReadOnlyDictionary<Adult, IReadOnlyList<CalendarEntry>> owns,
        IReadOnlyList<CalendarEntry> family,
        PlanningRules rules,
        TimeZoneInfo zone,
        DateOnly firstDay,
        DateOnly lastDay)
    {
        // Another adult's own calendar counts when the entry names this one ("Trip with Lee" on Sam's calendar is Lee's too).
        var own = owns[adult];
        var candidates = owns.SelectMany(pair => pair.Value.Select(entry => (Entry: entry, Class: TripClassifier.Classify(entry, pair.Key, rules.Adults))))
            .Concat(family.Select(entry => (Entry: entry, Class: TripClassifier.Classify(entry, null, rules.Adults))))
            .Where(item => item.Class is not null && item.Class.Who.Any(who => who.Name == adult.Name))
            .Select(item => (Kind: item.Class!.Kind, First: item.Entry.FirstDay, Last: item.Entry.EndDayExclusive.AddDays(-1)))
            .Where(item => item.First <= lastDay && item.Last >= firstDay)
            .Distinct()
            .OrderBy(item => item.First)
            .ThenBy(item => item.Last)
            .ThenBy(item => item.Kind);

        var hotels = own.Concat(family).Where(entry => TripClassifier.NamesAHotel(entry.Title)).ToList();
        return candidates
            .Select(item => new Trip(item.Kind, item.First, item.Last, item.Kind == TripKind.Work ? HotelDuring(hotels, item.First, item.Last, zone) : null))
            .ToList();
    }

    private static HotelStay? HotelDuring(IReadOnlyList<CalendarEntry> hotels, DateOnly first, DateOnly last, TimeZoneInfo zone)
    {
        var during = hotels
            .Select(entry => entry.IsAllDay
                ? (Entry: entry, From: entry.FirstDay, To: entry.EndDayExclusive.AddDays(-1))
                : (Entry: entry, From: ZoneClock.DayOf(entry.Start, zone), To: ZoneClock.DayOf(entry.End, zone)))
            .Where(item => item.From <= last && item.To >= first)
            .OrderBy(item => item.From)
            .ThenBy(item => item.Entry.Title, StringComparer.Ordinal)
            .FirstOrDefault();
        return during.Entry is null ? null : new HotelStay(during.Entry.Title);
    }
}

/// <summary>
/// The calendars as the planner should see them around a trip: a work trip's all-day entry stops taking the whole day (an
/// opaque all-day entry blocks every hour, and the point of a work trip is that the plan goes on; a real meeting on it still
/// blocks its hours), and a holiday's entry takes the day even if it was marked free, so the planner spends its hard days
/// elsewhere. Only entries that <see cref="TripClassifier"/> calls trips are touched, only their availability changes, and the
/// children's calendars are passed through as they are. Unreadable stays unreadable.
///
/// <para>On the family calendar, which every adult reads, a work trip is freed for everyone (the traveller is not at home, and
/// the other adult is not busy because of it); a holiday is forced busy only if it is for every adult, since a holiday for one
/// would otherwise block the other.</para>
///
/// <para>An all-day hotel booking is freed too: a reservation names where someone sleeps, not hours they are unavailable.</para>
/// </summary>
public sealed class TripAwareCalendars : IHouseholdCalendars
{
    private readonly IHouseholdCalendars inner;
    private readonly PlanningRules rules;

    public TripAwareCalendars(IHouseholdCalendars inner, PlanningRules rules)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    public CalendarRead OwnCalendar(string person, DateTimeOffset from, DateTimeOffset to)
    {
        var read = inner.OwnCalendar(person, from, to);
        var adult = rules.FindAdult(person);
        return adult is null ? read : Shown(read, entry => Availability(TripClassifier.Classify(entry, adult, rules.Adults), entry, forEveryone: true));
    }

    public CalendarRead FamilyCalendar(DateTimeOffset from, DateTimeOffset to) =>
        Shown(inner.FamilyCalendar(from, to), entry =>
        {
            var found = TripClassifier.Classify(entry, null, rules.Adults);
            return Availability(found, entry, forEveryone: found is not null && found.Who.Count == rules.Adults.Count);
        });

    public CalendarRead ChildrensCalendars(DateTimeOffset from, DateTimeOffset to) => inner.ChildrensCalendars(from, to);

    private static Availability Availability(TripEntry? found, CalendarEntry entry, bool forEveryone) => found switch
    {
        null => entry.IsAllDay && TripClassifier.NamesAHotel(entry.Title) ? Calendar.Availability.Free : entry.Availability,
        { Kind: TripKind.Work } => Calendar.Availability.Free,
        _ => forEveryone ? Calendar.Availability.Busy : entry.Availability,
    };

    private static CalendarRead Shown(CalendarRead read, Func<CalendarEntry, Availability> availability)
    {
        if (!read.IsReadable)
        {
            return read;
        }

        return CalendarRead.Of(read.Entries.Select(entry =>
        {
            var wanted = availability(entry);
            return wanted == entry.Availability
                ? entry
                : CalendarEntry.AllDay(entry.Title, entry.FirstDay, entry.EndDayExclusive, wanted, entry.Guests);
        }));
    }
}
