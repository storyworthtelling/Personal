namespace Constitution.Domain.Household;

/// <summary>
/// One person on the household roster as the owner edits it — the only roster type that carries a
/// <see cref="Birthdate"/>. A list, a label or Diagnostics is given a <see cref="RosterEntry"/> instead,
/// which has no such member. The birthdate lives in the record's own field in the encrypted household
/// file and nowhere else.
///
/// <para>Pickup belongs to an adult: a child is never on pickup. The Google address, when there is one,
/// is what the planner shares a calendar with.</para>
///
/// <para><see cref="ToString"/> names the id only. The default would print every member, and a person
/// printed into a log line is a name and a date of birth with it.</para>
/// </summary>
public sealed record RosterPerson
{
    public const int MaxNameLength = 80;

    public RosterPerson(PersonId id, string name, MemberRole role, Birthdate? birthdate, bool onPickup, string? googleAddress)
    {
        Id = id;
        Name = name?.Trim() ?? string.Empty;
        Role = role;
        Birthdate = birthdate;
        OnPickup = onPickup;
        GoogleAddress = string.IsNullOrWhiteSpace(googleAddress) ? null : googleAddress.Trim();
        EnsureValid();
    }

    public PersonId Id { get; init; }

    public string Name { get; init; }

    public MemberRole Role { get; init; }

    public Birthdate? Birthdate { get; init; }

    public bool OnPickup { get; init; }

    public string? GoogleAddress { get; init; }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> naming what is wrong. Run by the constructor and again before
    /// anything is written, because a <c>with</c> expression builds a person without passing through it.
    /// </summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxNameLength)
        {
            throw new ArgumentException("A person on the roster needs a name of 1 to " + MaxNameLength + " characters.", nameof(Name));
        }

        if (Role == MemberRole.Child && OnPickup)
        {
            throw new ArgumentException("A child is never on pickup; pickup is an adult's.", nameof(OnPickup));
        }

        if (GoogleAddress is { } address && !LooksLikeAnAddress(address))
        {
            throw new ArgumentException("A Google address has one @ with something on each side and no spaces.", nameof(GoogleAddress));
        }
    }

    public override string ToString() => nameof(RosterPerson) + "(" + Id.Value + ")";

    private static bool LooksLikeAnAddress(string address)
    {
        var at = address.IndexOf('@');
        return at > 0
            && at < address.Length - 1
            && address.IndexOf('@', at + 1) < 0
            && !address.Any(char.IsWhiteSpace);
    }
}
