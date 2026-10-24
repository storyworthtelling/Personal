using System.Globalization;

namespace Constitution.Domain.Recipes.Screens;

/// <summary>How one person took a dish.</summary>
public enum Reaction
{
    Loved,
    Fine,
    NotAFan,
}

/// <summary>One person's reaction. The name is the household's own first name, as the roster spells it.</summary>
public sealed record CardReaction(string Person, Reaction Reaction);

/// <summary>
/// What the household's cooking has said about a card: when it was last cooked, how many times, since when the
/// count runs ("spring"), and who took it how. Held by whatever source fills the box; the card itself keeps none of it.
/// </summary>
public sealed record CardHistory(DateOnly? LastCooked, int TimesCooked, string? Since, IReadOnlyList<CardReaction> Reactions)
{
    /// <summary>A card nobody has cooked yet.</summary>
    public static CardHistory Never { get; } = new(null, 0, null, []);

    public bool Equals(CardHistory? other) =>
        other is not null
        && LastCooked == other.LastCooked
        && TimesCooked == other.TimesCooked
        && Since == other.Since
        && Reactions.SequenceEqual(other.Reactions);

    public override int GetHashCode() => HashCode.Combine(LastCooked, TimesCooked, Since, Reactions.Count);
}

/// <summary>
/// A picked card's history in the brief's words (C-6.2.2, book p.13): "Last cooked 2 weeks ago · loved by all",
/// "Cooked 6 times since spring", "Sam, Lee, Robin: loved · Juniper: fine", "45 min hands-on · serves 6".
/// Time is passed in. A line with nothing to say is null, so the card leaves it out rather than printing a blank.
/// </summary>
public static class CardHistoryWords
{
    private const string Join = " · ";

    public static string Headline(CardHistory history, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.LastCooked is not { } last)
        {
            return "Not cooked yet";
        }

        var text = "Last cooked " + Ago(today.DayNumber - last.DayNumber);
        var loved = history.Reactions.Where(reaction => reaction.Reaction == Reaction.Loved).Select(reaction => reaction.Person).ToList();
        if (loved.Count == 0)
        {
            return text;
        }

        return text + Join + (loved.Count == history.Reactions.Count ? "loved by all" : "loved by " + string.Join(", ", loved));
    }

    public static string? Counted(CardHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.TimesCooked <= 0)
        {
            return null;
        }

        var text = history.TimesCooked == 1 ? "Cooked once" : "Cooked " + history.TimesCooked.ToString(CultureInfo.InvariantCulture) + " times";
        return string.IsNullOrWhiteSpace(history.Since) ? text : text + " since " + history.Since.Trim();
    }

    public static string? Reactions(CardHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var groups = new[] { Reaction.Loved, Reaction.Fine, Reaction.NotAFan }
            .Select(kind => (kind, names: history.Reactions.Where(reaction => reaction.Reaction == kind).Select(reaction => reaction.Person).ToList()))
            .Where(group => group.names.Count > 0)
            .Select(group => string.Join(", ", group.names) + ": " + Word(group.kind))
            .ToList();
        return groups.Count == 0 ? null : string.Join(Join, groups);
    }

    /// <summary>Hands-on minutes and servings, the two things a card knows about itself.</summary>
    public static string? Facts(Recipe card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var parts = new List<string>();
        if (card.HandsOnMinutes > 0)
        {
            parts.Add(card.HandsOnMinutes.ToString(CultureInfo.InvariantCulture) + " min hands-on");
        }

        if (card.Servings is { } servings)
        {
            parts.Add("serves " + servings.ToString(CultureInfo.InvariantCulture));
        }

        return parts.Count == 0 ? null : string.Join(Join, parts);
    }

    private static string Word(Reaction reaction) => reaction switch
    {
        Reaction.Loved => "loved",
        Reaction.Fine => "fine",
        Reaction.NotAFan => "not a fan",
        _ => throw new ArgumentOutOfRangeException(nameof(reaction), reaction, "No word for this reaction."),
    };

    private static string Ago(int days)
    {
        if (days <= 0)
        {
            return "today";
        }

        if (days == 1)
        {
            return "yesterday";
        }

        if (days < 7)
        {
            return days.ToString(CultureInfo.InvariantCulture) + " days ago";
        }

        if (days < 56)
        {
            var weeks = days / 7;
            return weeks == 1 ? "1 week ago" : weeks.ToString(CultureInfo.InvariantCulture) + " weeks ago";
        }

        if (days < 365)
        {
            return (days / 30).ToString(CultureInfo.InvariantCulture) + " months ago";
        }

        return "over a year ago";
    }
}
