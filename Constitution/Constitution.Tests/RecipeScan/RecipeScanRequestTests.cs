using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// The owner's scan (C-6.5.1) as D99 reshapes it: the request names Plate's recipe activity and carries nothing, and what comes
/// back across however many pages is words that are forgotten when the draft is.
/// </summary>
public class RecipeScanRequestTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void AimsAtPlateExplicitly()
    {
        var launcher = Read("Constitution.Android/MealsTab/ScanRecipeLauncher.cs");

        Assert.Equal(RecipeReadContract.PlatePackage, ScanRequest.Package);
        Assert.Equal(RecipeReadContract.PlatePackage + ".RecipeReadActivity", ScanRequest.Component);
        Assert.Contains("SetClassName(ScanRequest.Package, ScanRequest.Component)", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("SetPackage", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void PagesAreTransient()
    {
        var payload = new RecipeReadPayload(
            "Pear porridge",
            Enumerable.Range(0, 40).Select(n => n + " pears").ToList(),
            Enumerable.Range(0, 30).Select(n => "Step " + n + " of the porridge.").ToList());
        var draft = ScanConfirmModel.From(payload);

        draft.Discard();

        Assert.Empty(draft.Lines);
        Assert.Equal(string.Empty, draft.Title);
        Assert.DoesNotContain("Intent", Read("Constitution.Domain/Recipes/Screens/ScanReturn.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLauncherAsksPlateForNothingElse()
    {
        var launcher = Read("Constitution.Android/MealsTab/ScanRecipeLauncher.cs");

        Assert.DoesNotContain("PutExtra", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("SetData", launcher, StringComparison.Ordinal);
    }
}
