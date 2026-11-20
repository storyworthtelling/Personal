namespace Storyworthtelling.People;

/// <summary>
/// One person in the household. A child is never on pickup. <see cref="GoogleAddress"/> is the person's own Google account, when they
/// have one (so they can be invited and a calendar shared with them); <see cref="CalendarId"/> is the calendar their own events are on —
/// for an adult usually their primary calendar, for a child a calendar a parent owns.
///
/// <para><see cref="Links"/> names this person in each app that keeps records about them (app → that app's own id), so a record written
/// before the household was shared keeps pointing at the right person.</para>
///
/// <para><see cref="ToString"/> names the id only: the default would print a name and a birthdate into any log line.</para>
/// </summary>
public sealed record HouseholdPerson
{
    public const int MaxNameLength = 80;

    private readonly string name = string.Empty;
    private readonly bool onPickup;
    private readonly bool isChild;
    private readonly string? googleAddress;

    public HouseholdPerson(string id, string name, bool IsChild, DateOnly? Birthdate, bool OnPickup, string? GoogleAddress, string? CalendarId)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A person needs an id.", nameof(id));
        }

        Id = id.Trim().ToLowerInvariant();
        this.isChild = IsChild;
        this.Name = name;
        this.Birthdate = Birthdate;
        this.OnPickup = OnPickup;
        this.GoogleAddress = GoogleAddress;
        this.CalendarId = string.IsNullOrWhiteSpace(CalendarId) ? null : CalendarId.Trim();
    }

    public string Id { get; }

    public string Name
    {
        get => name;
        init
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
            {
                throw new ArgumentException("A person needs a name of 1 to " + MaxNameLength + " characters.", nameof(Name));
            }

            name = trimmed;
        }
    }

    public bool IsChild
    {
        get => isChild;
        init
        {
            isChild = value;
            RefusePickupForAChild();
        }
    }

    public DateOnly? Birthdate { get; init; }

    public bool OnPickup
    {
        get => onPickup;
        init
        {
            onPickup = value;
            RefusePickupForAChild();
        }
    }

    public string? GoogleAddress
    {
        get => googleAddress;
        init
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (trimmed is not null && !LooksLikeAnAddress(trimmed))
            {
                throw new ArgumentException("A Google address has one @ with something on each side and no spaces.", nameof(GoogleAddress));
            }

            googleAddress = trimmed;
        }
    }

    public string? CalendarId { get; init; }

    /// <summary>App name (<see cref="PeopleApps"/>) → that app's own id for this person.</summary>
    public IReadOnlyDictionary<string, string> Links { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool Equals(HouseholdPerson? other) =>
        other is not null
        && Id == other.Id && Name == other.Name && IsChild == other.IsChild && Birthdate == other.Birthdate && OnPickup == other.OnPickup
        && GoogleAddress == other.GoogleAddress && CalendarId == other.CalendarId
        && Links.Count == other.Links.Count && Links.All(pair => other.Links.TryGetValue(pair.Key, out var theirs) && theirs == pair.Value);

    public override int GetHashCode() => HashCode.Combine(Id, Name, IsChild, Birthdate, OnPickup, GoogleAddress, CalendarId);

    public override string ToString() => nameof(HouseholdPerson) + "(" + Id + ")";

    private void RefusePickupForAChild()
    {
        if (isChild && onPickup)
        {
            throw new ArgumentException("A child is never on pickup; pickup is an adult’s.", nameof(OnPickup));
        }
    }

    private static bool LooksLikeAnAddress(string address)
    {
        var at = address.IndexOf('@');
        return at > 0 && at < address.Length - 1 && address.IndexOf('@', at + 1) < 0 && !address.Any(char.IsWhiteSpace);
    }
}

/// <summary>
/// The household's shared rhythm: the family calendar (or that there is none), dinner, the children's bedtime and the household's zone.
/// Every value is null until someone chooses it; an app that needs one takes its own stated stand-in and says so.
/// </summary>
public sealed record HouseholdRhythm
{
    public string? FamilyCalendarId { get; init; }

    /// <summary>True when the household has said it has no family calendar — different from not having set one.</summary>
    public bool NoFamilyCalendar { get; init; }

    public TimeOnly? DinnerStart { get; init; }

    public TimeSpan? DinnerLength { get; init; }

    public TimeOnly? Bedtime { get; init; }

    /// <summary>An IANA zone id.</summary>
    public string? ZoneId { get; init; }

    public static HouseholdRhythm None { get; } = new();
}

/// <summary>A person as an app knew them before the household was shared, for <see cref="HouseholdPeopleEditor.Import"/>.</summary>
public sealed record ImportedPerson(string LocalId, string Name, bool IsChild, DateOnly? Birthdate, bool OnPickup, string? GoogleAddress);
