using System.Text.RegularExpressions;
using Constitution.Domain.Recipes;

namespace Constitution.Tests.Contracts;

/// <summary>
/// Plate's recipe read held equal from both sides (PBI C-11.2.1), by content: Plate's constants are read as text, because a
/// build never compares two apps. Plate's own <c>Codex.Tests/Plate/RecipeErrandTests</c> pins its side to the same literals.
///
/// <para>The payload is words only. Plate reads the card on the phone and returns a title, ingredient lines and step lines; no
/// image, no file and no URI crosses, and nothing here has a member that could hold one. Every recipe in these tests is invented.</para>
/// </summary>
public class RecipeReadContractTests
{
    private static string RepoRoot() => Directory.GetParent(ConstitutionSource.Root())!.FullName;

    private static string PlateFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), "FocusPhone", .. parts]));

    /// <summary>
    /// The value of <c>public const string name = ...;</c> in the text. The right-hand side is a literal or a sum of literals and
    /// <c>PolicyContext.PlatePackage</c>, which is resolved from its own declaration, so the guard reads the contract as written.
    /// </summary>
    private static string Literal(string source, string name)
    {
        var match = Regex.Match(source, @"public const string " + name + @"\s*=\s*([^;]+);", RegexOptions.None, TimeSpan.FromSeconds(2));
        Assert.True(match.Success, $"{name} is not declared as a constant.");

        return string.Concat(match.Groups[1].Value.Split('+').Select(term => term.Trim()).Select(term =>
        {
            if (term == "PolicyContext.PlatePackage")
            {
                return PlatePackageLiteral();
            }

            var quoted = Regex.Match(term, "^\"([^\"]*)\"$", RegexOptions.None, TimeSpan.FromSeconds(2));
            Assert.True(quoted.Success, $"{name} uses {term}, which the guard cannot resolve.");
            return quoted.Groups[1].Value;
        }));
    }

    private static string PlatePackageLiteral()
    {
        var match = Regex.Match(
            PlateFile("Codex.Domain", "Policy", "PolicyContext.cs"),
            @"public const string PlatePackage\s*=\s*""([^""]+)""\s*;", RegexOptions.None, TimeSpan.FromSeconds(2));
        Assert.True(match.Success, "PlatePackage is not declared as a plain string literal.");
        return match.Groups[1].Value;
    }

    private static string PlateRequest() => PlateFile("Codex.Domain", "Plate", "ReadRequest.cs");

    // ---- The two sides, equal ----

    [Fact]
    public void ThePackageIsPlates()
    {
        Assert.Equal(PlatePackageLiteral(), RecipeReadContract.PlatePackage);
    }

    [Fact]
    public void TheActionIsPlates()
    {
        Assert.Equal(Literal(PlateRequest(), "ReadRecipe"), RecipeReadContract.Action);
    }

    [Fact]
    public void TheActivityIsPlates()
    {
        Assert.Equal(Literal(PlateRequest(), "RecipeActivity"), RecipeReadContract.Activity);
        Assert.StartsWith(RecipeReadContract.PlatePackage + ".", RecipeReadContract.Activity, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePermissionIsPlates()
    {
        Assert.Equal(Literal(PlateRequest(), "RecipePermission"), RecipeReadContract.Permission);
        Assert.StartsWith(RecipeReadContract.PlatePackage + ".", RecipeReadContract.Permission, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExtrasAreNamedAlike()
    {
        var plate = PlateRequest();
        Assert.Equal(Literal(plate, "TitleExtra"), RecipeReadContract.TitleExtra);
        Assert.Equal(Literal(plate, "IngredientsExtra"), RecipeReadContract.IngredientsExtra);
        Assert.Equal(Literal(plate, "StepsExtra"), RecipeReadContract.StepsExtra);
    }

    [Fact]
    public void PlateDeclaresTheComponentWeAim()
    {
        var activity = PlateFile("Plate.Android", "RecipeReadActivity.cs");
        Assert.Contains("Name = ReadRequest.RecipeActivity", activity, StringComparison.Ordinal);
        Assert.Contains("Permission = ReadRequest.RecipePermission", activity, StringComparison.Ordinal);
        Assert.Contains("ReadRequest.ReadRecipe", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWireNamesAreDistinct()
    {
        string[] names = [RecipeReadContract.TitleExtra, RecipeReadContract.IngredientsExtra, RecipeReadContract.StepsExtra];
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    // ---- Reading what Plate sends ----

    [Fact]
    public void AWholeRecipeIsRead()
    {
        var result = RecipeReadContract.Parse(
            "Marigold Lentil Stew",
            "[\"1 cup red lentils\",\"\\u00BD cup cream\"]",
            "[\"Warm the oil.\",\"Add the onion.\"]");

        Assert.True(result.IsValid);
        Assert.Equal("Marigold Lentil Stew", result.Payload!.Title);
        Assert.Equal(["1 cup red lentils", "½ cup cream"], result.Payload.Ingredients);
        Assert.Equal(["Warm the oil.", "Add the onion."], result.Payload.Steps);
    }

    [Fact]
    public void AMissingTitleReadsAsEmpty()
    {
        var result = RecipeReadContract.Parse(null, "[\"2 plums\"]", null);

        Assert.True(result.IsValid);
        Assert.Equal(string.Empty, result.Payload!.Title);
        Assert.Empty(result.Payload.Steps);
    }

    [Fact]
    public void BlankLinesAreDropped()
    {
        var result = RecipeReadContract.Parse("Plum Toast", "[\"2 plums\",\"  \",\"\"]", "[\"Toast it.\"]");

        Assert.Equal(["2 plums"], result.Payload!.Ingredients);
    }

    [Theory]
    [InlineData("not json", "[]", "ingredients")]
    [InlineData("{\"a\":1}", "[]", "ingredients")]
    [InlineData("[1,2]", "[]", "ingredients")]
    [InlineData("[\"2 plums\"]", "[null]", "steps")]
    [InlineData("[\"2 plums\"]", "\"Toast it.\"", "steps")]
    public void AMalformedListIsRefusedByName(string ingredients, string steps, string refused)
    {
        var result = RecipeReadContract.Parse("Plum Toast", ingredients, steps);

        Assert.False(result.IsValid);
        Assert.Equal(refused, result.RefusedField);
    }

    [Fact]
    public void NothingToKeepIsRefused()
    {
        Assert.Equal("recipe", RecipeReadContract.Parse("Plum Toast", "[]", "[]").RefusedField);
        Assert.Equal("recipe", RecipeReadContract.Parse(null, null, null).RefusedField);
        Assert.Equal("recipe", RecipeReadContract.Parse("Plum Toast", "[\"  \"]", null).RefusedField);
    }

    [Fact]
    public void TooManyLinesAreRefused()
    {
        var many = "[" + string.Join(',', Enumerable.Repeat("\"1 plum\"", RecipeReadContract.MaxLines + 1)) + "]";

        Assert.Equal("ingredients", RecipeReadContract.Parse("Plum Toast", many, null).RefusedField);
    }

    [Fact]
    public void AnOverlongLineIsRefused()
    {
        var oversize = "[\"" + new string('x', RecipeReadContract.MaxLineLength + 1) + "\"]";

        Assert.Equal("steps", RecipeReadContract.Parse("Plum Toast", "[\"2 plums\"]", oversize).RefusedField);
    }

    [Fact]
    public void AnOverlongTitleIsRefused()
    {
        var title = new string('x', RecipeReadContract.MaxTitleLength + 1);

        Assert.Equal("title", RecipeReadContract.Parse(title, "[\"2 plums\"]", null).RefusedField);
    }

    [Fact]
    public void ARefusalNamesTheFieldNeverTheValue()
    {
        var result = RecipeReadContract.Parse("Plum Toast", "[7]", null);

        Assert.DoesNotContain("7", result.RefusedField!, StringComparison.Ordinal);
        Assert.DoesNotContain("Plum", result.RefusedField!, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePayloadHasNoRoomForAnImage()
    {
        // Nothing but strings: no byte array, no stream, no URI to open a picture through.
        var types = typeof(RecipeReadPayload).GetProperties().Select(property => property.PropertyType).ToList();

        Assert.All(types, type => Assert.True(
            type == typeof(string) || type == typeof(IReadOnlyList<string>),
            $"{type.Name} could carry something other than the words."));
    }
}
