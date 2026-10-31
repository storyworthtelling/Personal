namespace Constitution.Domain.Calendar;

/// <summary>
/// One of the household's adults, as far as the calendar is concerned.
///
/// <para>The roster that will own people (C-6.1.4) does not exist yet, and neither does a pickup
/// setting, so this carries only what the busy-time model needs and is filled by whoever calls it.
/// <b>Default <see cref="OnPickup"/> to <c>true</c> for anyone not known to be off it</b>: a parent
/// wrongly left off the rota is a child's event nobody was blocked for.</para>
/// </summary>
public sealed record Adult
{
    /// <param name="name">The name as the household uses it; also how it is matched in a title.</param>
    /// <param name="onPickup">Whether this adult is on pickup duty: whether a children's event naming nobody is theirs.</param>
    /// <param name="addresses">Their email addresses; a guest list naming one is a fact about who is going.</param>
    /// <param name="otherNames">What else the family calls them ("Dad"), matched in titles like the name.</param>
    public Adult(string name, bool onPickup, IReadOnlyList<string>? addresses = null, IReadOnlyList<string>? otherNames = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An adult needs a name.", nameof(name));
        }

        Name = name.Trim();
        OnPickup = onPickup;
        Addresses = Clean(addresses);
        OtherNames = Clean(otherNames);
    }

    public string Name { get; }

    public bool OnPickup { get; }

    public IReadOnlyList<string> Addresses { get; }

    public IReadOnlyList<string> OtherNames { get; }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
        (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList();
}
