using System.Globalization;
using System.Text;

namespace Constitution.Domain.Recipes;

/// <summary>One pile in the box: a cuisine and its cards, in title order.</summary>
public sealed record Pile(Cuisine Cuisine, IReadOnlyList<Recipe> Cards)
{
    public int Count => Cards.Count;
}

/// <summary>
/// The recipe box as the Meals tab shows it (C-6.2.1, book p.13): one small pile per cuisine that has
/// cards, each under a label strip, with a search that narrows the piles in place (D59). A pure model
/// of the cards it was given — no view, no storage, no clock.
///
/// <para><b>Pile order</b> is the bigger piles first, ties by label, and the catch-all
/// <see cref="Cuisine.Other"/> last however big it is. Cards in a pile read by title, ignoring case and
/// accents. Neither depends on the order the cards were given in, so two installs show one box.</para>
///
/// <para><b>Searching narrows in place.</b> The result keeps the piles' order from the whole box rather
/// than re-ranking them by what matched, so a pile does not jump about as the owner types.</para>
/// </summary>
public sealed class RecipeBox
{
    private readonly IReadOnlyList<Pile> piles;

    public RecipeBox(IEnumerable<Recipe> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var all = cards.ToList();
        var twin = all.GroupBy(card => card.Id).FirstOrDefault(group => group.Count() > 1);
        if (twin is not null)
        {
            throw new ArgumentException("Two cards share the id '" + twin.Key.Value + "'.", nameof(cards));
        }

        Count = all.Count;
        piles = all
            .GroupBy(card => card.Cuisine)
            .Select(group => new Pile(group.Key, group.OrderBy(card => SearchText.Fold(card.Title), StringComparer.Ordinal).ThenBy(card => card.Id.Value, StringComparer.Ordinal).ToList()))
            .OrderBy(pile => pile.Cuisine == Cuisine.Other ? 1 : 0)
            .ThenByDescending(pile => pile.Count)
            .ThenBy(pile => Cuisines.Label(pile.Cuisine), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>How many cards the box holds.</summary>
    public int Count { get; }

    public IReadOnlyList<Pile> Piles => piles;

    /// <summary>
    /// The piles narrowed to the cards matching every word of <paramref name="query"/> in the title or an
    /// ingredient name — case and accents ignored, a part of a word enough. Quantities, units and steps are
    /// not searched. A blank query is the whole box; piles left empty are dropped, the rest keep their order.
    /// </summary>
    public IReadOnlyList<Pile> Search(string? query)
    {
        var words = SearchText.Words(query);
        if (words.Count == 0)
        {
            return piles;
        }

        var narrowed = new List<Pile>();
        foreach (var pile in piles)
        {
            var matching = pile.Cards.Where(card => Matches(card, words)).ToList();
            if (matching.Count > 0)
            {
                narrowed.Add(new Pile(pile.Cuisine, matching));
            }
        }

        return narrowed;
    }

    /// <summary>
    /// The edit that files <paramref name="card"/> under <paramref name="to"/> (C-6.2.4), or null when it is
    /// already there. The edit comes with its inverse, <see cref="CuisineMove.Inverse"/>: undo-first is the
    /// screen's, so the model hands back both and the screen decides the window.
    /// </summary>
    /// <exception cref="ArgumentException">The card is not in this box.</exception>
    public CuisineMove? Move(Recipe card, Cuisine to)
    {
        ArgumentNullException.ThrowIfNull(card);
        var held = piles.SelectMany(pile => pile.Cards).FirstOrDefault(candidate => candidate.Id == card.Id)
            ?? throw new ArgumentException("The card '" + card.Id.Value + "' is not in this box, so it cannot be moved.", nameof(card));
        return held.Cuisine == to ? null : new CuisineMove(held.Id, held.Cuisine, to);
    }

    private static bool Matches(Recipe card, IReadOnlyList<string> words)
    {
        var haystack = SearchText.Fold(card.Title) + "\n" + string.Join('\n', card.Ingredients.Select(ingredient => SearchText.Fold(ingredient.Name)));
        return words.All(word => haystack.Contains(word, StringComparison.Ordinal));
    }
}

/// <summary>Case- and accent-insensitive text for ordering and searching: decomposed, marks dropped, lower-cased.</summary>
internal static class SearchText
{
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    public static IReadOnlyList<string> Words(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : Fold(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
