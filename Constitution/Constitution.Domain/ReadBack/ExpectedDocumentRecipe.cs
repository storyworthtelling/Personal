using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;

namespace Constitution.Domain.ReadBack;

public sealed partial class ExpectedDocument
{
    /// <summary>
    /// Tonight's recipe as the read-back needs it: only the rating page has anything to read. Every eater's circle group
    /// (uncircled is unchanged) and the "We didn't cook this tonight" tick, from the same model the page was built from, so
    /// the ids the reader asks for are the ids on the page. The other recipe pages carry a notes margin the reader does not
    /// ask about, so they are not pages of this document as far as the read-back goes.
    /// </summary>
    public static ExpectedDocument ForRecipe(RecipeDocumentModel model, RecipeRatingModel rating, string person, DateOnly day, string? subject = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rating);

        var layouts = RecipeWithRating.Build(model, rating);
        var page = new ExpectedPage(layouts.Count, new InkReadout(layouts[^1].Map), RecipeRatingPage.Rules(rating));
        return new ExpectedDocument(DeliveryKind.Recipe, day, subject, person, null, [page]);
    }
}
