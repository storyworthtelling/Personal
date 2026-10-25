using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;

namespace Constitution.Tests.RecipeRating;

/// <summary>Invented eaters and dishes shaped like book p.9. Every name and dish here is made up.</summary>
internal static class RatingSamples
{
    public static readonly PersonId SamId = new("sam");
    public static readonly PersonId LeeId = new("lee");
    public static readonly PersonId RobinId = new("robin");
    public static readonly PersonId JuniperId = new("juniper");
    public static readonly RecipeId Chicken = new("sheet-pan-chicken");
    public static readonly RecipeId Noodles = new("buttered-noodles");

    public static readonly DateTime Tuesday = new(2026, 11, 17, 0, 0, 0);

    public static RecipeRatingRow Sam(TasteVerdict? last = TasteVerdict.Loved) => new(SamId, "Sam", Chicken, "Sheet-pan chicken", last);

    public static RecipeRatingRow Lee(TasteVerdict? last = TasteVerdict.Fine) => new(LeeId, "Lee", Chicken, "Sheet-pan chicken", last);

    public static RecipeRatingRow Robin(TasteVerdict? last = TasteVerdict.Loved) => new(RobinId, "Robin", Noodles, "Buttered noodles", last);

    public static RecipeRatingRow Juniper(TasteVerdict? last = null) => new(JuniperId, "Juniper", Noodles, "Buttered noodles", last);

    /// <summary>The book's page: Sam Loved, Lee Fine, Robin Loved, and Juniper's first time.</summary>
    public static RecipeRatingModel Book() => new("Lee", Tuesday, [Sam(), Lee(), Robin(), Juniper()]);

    /// <summary>Four people who have all rated their dish before, so no row asks for a first circle.</summary>
    public static RecipeRatingModel AllKnown() => new("Lee", Tuesday, [Sam(), Lee(), Robin(), Juniper(TasteVerdict.Fine)]);

    public static RecipeRatingModel Of(params RecipeRatingRow[] rows) => new("Lee", Tuesday, rows);

    public static InkPageLayout Page(RecipeRatingModel? model = null, int page = 4, int total = 4) =>
        RecipeRatingPage.Build(model ?? Book(), page, total);

    public static IEnumerable<PrintedItem> Items(InkPageLayout page, string prefix) =>
        page.Printed.Where(i => i.Name.StartsWith(prefix, StringComparison.Ordinal));

    public static PrintedItem Item(InkPageLayout page, string name) => page.Printed.Single(i => i.Name == name);
}
