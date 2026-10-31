namespace Constitution.Domain.Documents;

/// <summary>
/// Tonight's recipe whole: the pages <see cref="RecipeDocument"/> lays out, then the rating page that ends every recipe
/// (book p.9). The recipe is built with exactly one trailing page, which is this one, so "x of N" on every page counts it.
/// </summary>
public static class RecipeWithRating
{
    public static IReadOnlyList<InkPageLayout> Build(RecipeDocumentModel model, RecipeRatingModel rating)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rating);
        if (model.Steps is null)
        {
            throw new ArgumentException($"'{model.Title}' has no timed steps, so it numbers its own pages and has no place for a rating page after them.", nameof(model));
        }

        if (model.TrailingPages != 1)
        {
            throw new ArgumentException($"'{model.Title}' expects {model.TrailingPages} pages after it; the rating page is the one page that follows a recipe.", nameof(model));
        }

        if (!string.Equals(model.Cook.Trim(), rating.Cook?.Trim(), StringComparison.OrdinalIgnoreCase) || model.Day.Date != rating.Day.Date)
        {
            throw new ArgumentException($"The rating page is for {rating.Cook} on {rating.Day:d}, not for {model.Cook}'s '{model.Title}' on {model.Day:d}.", nameof(rating));
        }

        var pages = RecipeDocument.Build(model).ToList();
        pages.Add(RecipeRatingPage.Build(rating, pages.Count + 1, pages.Count + 1));
        return pages;
    }
}
