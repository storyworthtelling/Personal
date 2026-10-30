using System.Text.Json;
using System.Text.Json.Nodes;
using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Recipes;
using Constitution.Infrastructure.ReadBack;
using Constitution.Tests.RecipeDocuments;
using static Constitution.Tests.RecipeRating.RatingSamples;

namespace Constitution.Tests.RatingRows;

/// <summary>The expected document of a recipe carries the rating page's rows (person, card, group) through its file, so the hook needs no lookup.</summary>
public sealed class RatingRowsDocumentTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 11, 17);
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-rows-");

    private string PathOf() => Path.Combine(folder.FullName, "expected.json");

    private FileExpectedDocuments Make() => new(PathOf());

    public void Dispose() => folder.Delete(recursive: true);

    private static ExpectedDocument Recipe() => ExpectedDocument.ForRecipe(RecipeSamples.Timed(), Book(), "Lee", Day, "Lee");

    private static JsonObject Stored(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    [Fact]
    public void ForRecipeCarriesTheRowsThePageWasBuiltFrom()
    {
        var document = Recipe();

        Assert.Equal(RecipeRatingPage.RatingRows(Book()), document.RatingRows);
        Assert.Equal([SamId, LeeId, RobinId, JuniperId], document.RatingRows.Select(row => row.Person));
        Assert.Equal(["rate-1", "rate-2", "rate-3", "rate-4"], document.RatingRows.Select(row => row.Group));
    }

    [Fact]
    public void ADocumentMadeWithoutRowsHasNone()
    {
        var plain = new ExpectedDocument(DeliveryKind.Recipe, Day, "Lee", "Lee", null, Recipe().Pages);

        Assert.Empty(plain.RatingRows);
    }

    [Fact]
    public void OnlyARecipeCanCarryRatingRows()
    {
        var rows = RecipeRatingPage.RatingRows(Book());
        var pages = Recipe().Pages;

        Assert.Throws<ArgumentException>(() => new ExpectedDocument(DeliveryKind.Meals, Day, null, "Lee", null, pages, rows));
    }

    [Fact]
    public void TwoRowsOfOneGroupAreRefused()
    {
        var one = new TasteRatingRow("rate-1", SamId, Chicken);

        Assert.Throws<ArgumentException>(() => new ExpectedDocument(DeliveryKind.Recipe, Day, "Lee", "Lee", null, Recipe().Pages, [one, one]));
    }

    [Fact]
    public void TheDocumentKeepsItsOwnCopyOfTheRows()
    {
        var rows = RecipeRatingPage.RatingRows(Book()).ToList();
        var document = new ExpectedDocument(DeliveryKind.Recipe, Day, "Lee", "Lee", null, Recipe().Pages, rows);

        rows.Clear();

        Assert.Equal(4, document.RatingRows.Count);
    }

    [Fact]
    public void TheRowsSurviveTheFile()
    {
        var document = Recipe();
        Make().Record(document);

        var back = Assert.Single(Make().Outstanding()).Document;

        Assert.Equal(document.RatingRows, back.RatingRows);
        Assert.Equal(document.Pages[0].Rules, back.Pages[0].Rules);
    }

    [Fact]
    public void TheRowsAreStoredUnderFrozenLiteralNames()
    {
        Make().Record(Recipe());

        var rows = Stored(PathOf())["documents"]![0]!["ratingRows"]!.AsArray();

        Assert.Equal(4, rows.Count);
        var third = rows[2]!.AsObject();
        Assert.Equal(["group", "person", "recipe"], third.Select(pair => pair.Key).Order());
        Assert.Equal("rate-3", (string?)third["group"]);
        Assert.Equal("robin", (string?)third["person"]);
        Assert.Equal("buttered-noodles", (string?)third["recipe"]);
    }

    [Fact]
    public void AFileWrittenWithTheLiteralWordsReadsBack()
    {
        Make().Record(Recipe());
        var node = Stored(PathOf());
        var rows = node["documents"]![0]!["ratingRows"]!.AsArray();
        rows.Clear();
        rows.Add(JsonNode.Parse("""{"group":"rate-2","person":"juniper","recipe":"lemon-orzo"}"""));
        File.WriteAllText(PathOf(), node.ToJsonString());

        var back = Assert.Single(Make().Outstanding()).Document;

        Assert.Equal(new TasteRatingRow("rate-2", new PersonId("juniper"), new RecipeId("lemon-orzo")), Assert.Single(back.RatingRows));
    }

    [Fact]
    public void AnOlderFileWithNoRowsStillReads()
    {
        Make().Record(Recipe());
        var node = Stored(PathOf());
        node["documents"]![0]!.AsObject().Remove("ratingRows");
        File.WriteAllText(PathOf(), node.ToJsonString());

        var back = Assert.Single(Make().Outstanding()).Document;

        Assert.Empty(back.RatingRows);
        Assert.Equal(Recipe().Pages[0].Rules, back.Pages[0].Rules);
        Assert.False(File.Exists(PathOf() + ".unreadable"));
    }

    [Fact]
    public void ADocumentThatCarriesNoRowsWritesNoRowsProperty()
    {
        Make().Record(new ExpectedDocument(DeliveryKind.Recipe, Day, "Lee", "Lee", null, Recipe().Pages));

        Assert.DoesNotContain("ratingRows", File.ReadAllText(PathOf()), StringComparison.Ordinal);
    }

    [Fact]
    public void ARowWithNoSuchPersonSetsTheFileAside()
    {
        Make().Record(Recipe());
        File.WriteAllText(PathOf(), File.ReadAllText(PathOf()).Replace("\"robin\"", "\"Not A Person\"", StringComparison.Ordinal));

        Assert.Empty(Make().Outstanding());
        Assert.True(File.Exists(PathOf() + ".unreadable"));
    }

    [Fact]
    public void ARowWithNoGroupSetsTheFileAside()
    {
        Make().Record(Recipe());
        var node = Stored(PathOf());
        node["documents"]![0]!["ratingRows"]![0]!.AsObject().Remove("group");
        File.WriteAllText(PathOf(), node.ToJsonString());

        Assert.Empty(Make().Outstanding());
        Assert.True(File.Exists(PathOf() + ".unreadable"));
    }

    [Fact]
    public void TheFileHoldsIdsNotNamesOrDishes()
    {
        Make().Record(Recipe());

        var text = File.ReadAllText(PathOf());
        var rows = JsonSerializer.Serialize(Stored(PathOf())["documents"]![0]!["ratingRows"]);

        Assert.DoesNotContain("Sheet-pan chicken", rows, StringComparison.Ordinal);
        Assert.DoesNotContain("Sam", rows, StringComparison.Ordinal);
        Assert.Contains("ratingRows", text, StringComparison.Ordinal);
    }
}
