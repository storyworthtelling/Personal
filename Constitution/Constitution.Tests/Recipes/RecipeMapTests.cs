using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Recipes;
using Storyworthtelling.Household;

namespace Constitution.Tests.Recipes;

/// <summary>The typed map for a <c>recipe</c> record: frozen field names, frozen words, and what an edit leaves alone.</summary>
public class RecipeMapTests
{
    private static HouseholdDocument PutOne(Recipe recipe, long at = 1) =>
        HouseholdRecords.Put(HouseholdDocument.Empty, RecipeMaps.Recipe, recipe, RecipeData.At(at), RecipeData.OwnerWriter);

    [Fact]
    public void TheKeyIsRecipeSlashTheId()
    {
        Assert.Equal("recipe/black-bean-chili", RecipeMaps.Recipe.KeyOf(RecipeData.Chili()).Value);
        Assert.Equal(ConstitutionKinds.Recipe, RecipeMaps.Recipe.Kind);
    }

    [Fact]
    public void TheFieldNamesAreFrozen()
    {
        var names = RecipeMaps.Recipe.FieldsOf(RecipeData.Chili()).Keys.Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["cuisine", "ingredients", "kids", "recipe", "servings", "source", "steps", "title"], names);
    }

    [Fact]
    public void TheStoredValuesAreFrozen()
    {
        var fields = RecipeMaps.Recipe.FieldsOf(RecipeData.Chili());

        Assert.Equal("black-bean-chili", fields["recipe"]);
        Assert.Equal("Black bean chili", fields["title"]);
        Assert.Equal("mexican", fields["cuisine"]);
        Assert.Equal("true", fields["kids"]);
        Assert.Equal("6", fields["servings"]);
        Assert.Equal("typed", fields["source"]);
        Assert.Equal(
            "[{\"quantity\":\"2\",\"unit\":\"cans\",\"name\":\"black beans\"},"
            + "{\"quantity\":\"1\",\"unit\":\"\",\"name\":\"onion\"},"
            + "{\"quantity\":\"\",\"unit\":\"\",\"name\":\"salt\"}]",
            fields["ingredients"]);
        Assert.Equal(
            "[{\"text\":\"Soften the onion.\",\"minutes\":10,\"active\":true},"
            + "{\"text\":\"Simmer everything together.\",\"minutes\":35,\"active\":false},"
            + "{\"text\":\"Taste and salt.\",\"minutes\":null,\"active\":true}]",
            fields["steps"]);
    }

    [Fact]
    public void EveryFieldRoundTrips()
    {
        var bare = new Recipe(new RecipeId("toast"), "Toast", Cuisine.American, [], [], null, null, forKids: false);
        var accents = new Recipe(
            new RecipeId("apple-crisp"), "Crème brûlée \"light\"", Cuisine.French,
            [new Ingredient("¾", "cup", "brown sugar"), new Ingredient("1", null, "vanilla bean, split")],
            [new RecipeStep("Bake 40 min, until bubbling.", 40, false)],
            8, "Grandma's card", forKids: true);

        foreach (var recipe in new[] { RecipeData.Chili(), bare, accents }.Concat(RecipeData.Box()))
        {
            var read = Assert.Single(HouseholdRecords.Read(PutOne(recipe), RecipeMaps.Recipe).Items);

            Assert.Equal(recipe, read);
        }
    }

    [Fact]
    public void AnEditThatDropsAnOptionalClearsItsField()
    {
        var document = PutOne(RecipeData.Chili());
        var edited = RecipeData.Chili() with { Servings = null, SourceLabel = null, Steps = [] };

        document = HouseholdRecords.Put(document, RecipeMaps.Recipe, edited, RecipeData.At(2), RecipeData.OwnerWriter);

        var read = Assert.Single(HouseholdRecords.Read(document, RecipeMaps.Recipe).Items);
        Assert.Null(read.Servings);
        Assert.Null(read.SourceLabel);
        Assert.Empty(read.Steps);
        var fields = document.Live(RecipeMaps.Recipe.KeyOf(edited))!.Fields;
        Assert.Equal(string.Empty, fields["servings"]);
        Assert.Equal("[]", fields["steps"]);
    }

    [Fact]
    public void FieldsOnlyANewerWriterKnowsSurviveAnEdit()
    {
        var newer = HouseholdRecord.Write(
            new RecordKey(ConstitutionKinds.Recipe, "black-bean-chili"), 7,
            new Dictionary<string, string>(RecipeMaps.Recipe.FieldsOf(RecipeData.Chili())) { ["nutrition"] = "future-value" },
            RecipeData.At(1), RecipeData.PartnerWriter);
        var document = HouseholdDocument.Empty.With(newer);

        var edited = HouseholdRecords.Put(document, RecipeMaps.Recipe, RecipeData.Chili() with { Title = "Chili, our way" }, RecipeData.At(2), RecipeData.OwnerWriter);

        var record = edited.Live(RecipeMaps.Recipe.KeyOf(RecipeData.Chili()))!;
        Assert.Equal("future-value", record.Fields["nutrition"]);
        Assert.Equal(7, record.SchemaVersion);
        Assert.Equal("Chili, our way", Assert.Single(HouseholdRecords.Read(edited, RecipeMaps.Recipe).Items).Title);
    }

    [Fact]
    public void AMoveLeavesTheRestOfTheCardAlone()
    {
        var document = PutOne(RecipeData.Chili());

        var moved = HouseholdRecords.Put(document, RecipeMaps.Recipe, RecipeData.Chili() with { Cuisine = Cuisine.Other }, RecipeData.At(2), RecipeData.OwnerWriter);

        var read = Assert.Single(HouseholdRecords.Read(moved, RecipeMaps.Recipe).Items);
        Assert.Equal(RecipeData.Chili() with { Cuisine = Cuisine.Other }, read);
    }

    [Fact]
    public void AnUnknownCuisineWordIsUnreadable()
    {
        var fields = new Dictionary<string, string>(RecipeMaps.Recipe.FieldsOf(RecipeData.Chili())) { ["cuisine"] = "thai" };
        var odd = HouseholdRecord.Write(new RecordKey(ConstitutionKinds.Recipe, "black-bean-chili"), 9, fields, RecipeData.At(1), RecipeData.PartnerWriter);

        var set = HouseholdRecords.Read(HouseholdDocument.Empty.With(odd), RecipeMaps.Recipe);

        Assert.Empty(set.Items);
        Assert.Contains("cuisine", Assert.Single(set.Unreadable).Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("title", "")]
    [InlineData("recipe", "")]
    [InlineData("ingredients", "not a list")]
    [InlineData("ingredients", "[{\"quantity\":\"1\",\"unit\":\"\"}]")]
    [InlineData("ingredients", "{\"quantity\":\"1\"}")]
    [InlineData("steps", "[1,2]")]
    [InlineData("steps", "[{\"text\":\"Stir.\",\"minutes\":\"ten\",\"active\":true}]")]
    [InlineData("servings", "six")]
    [InlineData("servings", "0")]
    [InlineData("kids", "maybe")]
    [InlineData("source", "https://cooking.example.com/recipes/9")]
    public void ABadFieldMakesTheRecordUnreadable(string field, string value)
    {
        var fields = new Dictionary<string, string>(RecipeMaps.Recipe.FieldsOf(RecipeData.Chili())) { [field] = value };
        var odd = HouseholdRecord.Write(new RecordKey(ConstitutionKinds.Recipe, "black-bean-chili"), 1, fields, RecipeData.At(1), RecipeData.PartnerWriter);

        var set = HouseholdRecords.Read(HouseholdDocument.Empty.With(odd), RecipeMaps.Recipe);

        Assert.Empty(set.Items);
        Assert.Single(set.Unreadable);
    }

    [Fact]
    public void AnOlderCardWithoutListsStillReads()
    {
        var older = HouseholdRecord.Write(
            new RecordKey(ConstitutionKinds.Recipe, "toast"), 1,
            new Dictionary<string, string> { ["recipe"] = "toast", ["title"] = "Toast", ["cuisine"] = "american" },
            RecipeData.At(1), RecipeData.PartnerWriter);

        var read = Assert.Single(HouseholdRecords.Read(HouseholdDocument.Empty.With(older), RecipeMaps.Recipe).Items);

        Assert.Empty(read.Ingredients);
        Assert.Empty(read.Steps);
        Assert.False(read.ForKids);
        Assert.Null(read.Servings);
    }

    [Fact]
    public void AnInvalidCardIsNotWritten()
    {
        Assert.Throws<ArgumentException>(() => RecipeMaps.Recipe.FieldsOf(RecipeData.Chili() with { Servings = 0 }));
    }

    [Fact]
    public void TheMapNamesNoIdentifierAsAStoredWord()
    {
        var file = ConstitutionSource.Files(".cs").Single(path => ConstitutionSource.IsGuard(path, "RecipeMaps.cs"));
        var code = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(file), @"///.*|//.*", string.Empty);

        Assert.DoesNotContain("nameof(", code, StringComparison.Ordinal);
    }
}
