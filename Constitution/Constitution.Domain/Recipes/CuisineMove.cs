using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Storyworthtelling.Household;

namespace Constitution.Domain.Recipes;

/// <summary>The document after a move, and the sentence if the move was refused.</summary>
public sealed record CuisineMoveResult(HouseholdDocument Document, string? Refusal)
{
    public bool Accepted => Refusal is null;
}

/// <summary>
/// Filing one card under another cuisine (C-6.2.4, D64: a scanned card's cuisine is chosen silently, so a
/// misfile has to be easy to fix). The move names the card and both ends, so <see cref="Inverse"/> is the
/// same kind of thing and the screen's undo window (an <c>UndoBar</c>, DESIGN.md §5) is just applying it.
///
/// <para>Applying reads the card <b>as the document holds it now</b> and changes only its cuisine, so
/// a title another install edited meanwhile, or a field a newer build added, is not rolled back by a move
/// or by its undo. The household library's stamp rule still holds: a stamp not newer than the card throws.</para>
/// </summary>
public sealed record CuisineMove(RecipeId Card, Cuisine From, Cuisine To)
{
    public const string CardGoneSentence = "That card is not in the box any more.";
    public const string CardUnreadableSentence = "That card cannot be read on this phone, so it was left where it is.";

    /// <summary>The move that puts the card back.</summary>
    public CuisineMove Inverse => new(Card, To, From);

    public CuisineMoveResult Apply(HouseholdDocument document, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        var record = document.Live(new RecordKey(ConstitutionKinds.Recipe, Card.Value));
        if (record is null)
        {
            return new CuisineMoveResult(document, CardGoneSentence);
        }

        Recipe current;
        try
        {
            current = RecipeMaps.Recipe.Read(new RecordReader(record));
        }
        catch (HouseholdRecordException)
        {
            return new CuisineMoveResult(document, CardUnreadableSentence);
        }

        return new CuisineMoveResult(HouseholdRecords.Put(document, RecipeMaps.Recipe, current with { Cuisine = To }, stamp, writer), null);
    }
}
