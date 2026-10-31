namespace Constitution.Domain.Groceries;

/// <summary>
/// One thing the household keeps in the pantry. A <see cref="Standing"/> staple is on the meal sheet's checklist for good (salt,
/// olive oil); a one-off item is stock assumed on hand from an uncooked dinner and is not printed. Either is on hand unless
/// <see cref="NeedMore"/> (the owner ticked it on the paper: a blank staple means no need).
/// </summary>
public sealed record Staple
{
    public const int MaxNameLength = 100;

    public Staple(string name, Aisle aisle, bool needMore, bool standing = true)
    {
        Name = name?.Trim() ?? string.Empty;
        Aisle = aisle;
        NeedMore = needMore;
        Standing = standing;
        EnsureValid();
    }

    public string Name { get; init; }

    public Aisle Aisle { get; init; }

    public bool NeedMore { get; init; }

    public bool Standing { get; init; }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxNameLength)
        {
            throw new ArgumentException("A staple needs a name of 1 to " + MaxNameLength + " characters.", nameof(Name));
        }

        if (!Enum.IsDefined(Aisle))
        {
            throw new ArgumentException("A staple is shelved in one of the known aisles.", nameof(Aisle));
        }
    }
}
