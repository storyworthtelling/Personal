using Constitution.Domain.Household.Records;
using Constitution.Domain.Recipes;
using Storyworthtelling.Household;

namespace Constitution.Tests.Recipes;

/// <summary>Moving a card to another cuisine (C-6.2.4, D64): the model returns the edit and its inverse; the UI owns the undo window.</summary>
public class CuisineMoveTests
{
    private static Cuisine CuisineOf(HouseholdDocument document, string id) =>
        HouseholdRecords.Read(document, RecipeMaps.Recipe).Items.Single(card => card.Id.Value == id).Cuisine;

    [Fact]
    public void AMoveNamesTheCardAndBothCuisines()
    {
        var move = new RecipeBox(RecipeData.Box()).Move(RecipeData.Box()[0], Cuisine.Other);

        Assert.NotNull(move);
        Assert.Equal(new RecipeId("black-bean-chili"), move!.Card);
        Assert.Equal(Cuisine.Mexican, move.From);
        Assert.Equal(Cuisine.Other, move.To);
    }

    [Fact]
    public void TheInverseSwapsTheEnds()
    {
        var move = new CuisineMove(new RecipeId("lentil-soup"), Cuisine.Other, Cuisine.Italian);

        Assert.Equal(new CuisineMove(new RecipeId("lentil-soup"), Cuisine.Italian, Cuisine.Other), move.Inverse);
        Assert.Equal(move, move.Inverse.Inverse);
    }

    [Fact]
    public void MovingToTheCuisineItIsAlreadyInIsNoMove()
    {
        Assert.Null(new RecipeBox(RecipeData.Box()).Move(RecipeData.Box()[0], Cuisine.Mexican));
    }

    [Fact]
    public void ACardNotInTheBoxCannotBeMoved()
    {
        var stranger = RecipeData.Plain("stranger", "Stranger stew", Cuisine.Other);

        Assert.Throws<ArgumentException>(() => new RecipeBox(RecipeData.Box()).Move(stranger, Cuisine.Asian));
    }

    [Fact]
    public void ApplyFilesTheCardUnderTheNewOne()
    {
        var document = RecipeData.Document(RecipeData.Box());
        var move = new RecipeBox(RecipeData.Box()).Move(RecipeData.Box()[0], Cuisine.Other)!;

        var result = move.Apply(document, RecipeData.At(5000), RecipeData.OwnerWriter);

        Assert.True(result.Accepted);
        Assert.Equal(Cuisine.Other, CuisineOf(result.Document, "black-bean-chili"));
        Assert.Equal(Cuisine.Mexican, CuisineOf(document, "black-bean-chili"));
    }

    [Fact]
    public void MoveIsUndoable()
    {
        var document = RecipeData.Document(RecipeData.Box());
        var move = new RecipeBox(RecipeData.Box()).Move(RecipeData.Box()[0], Cuisine.Other)!;

        var moved = move.Apply(document, RecipeData.At(5000), RecipeData.OwnerWriter).Document;
        var undone = move.Inverse.Apply(moved, RecipeData.At(5001), RecipeData.OwnerWriter).Document;

        Assert.Equal(
            HouseholdRecords.Read(document, RecipeMaps.Recipe).Items.OrderBy(card => card.Id.Value),
            HouseholdRecords.Read(undone, RecipeMaps.Recipe).Items.OrderBy(card => card.Id.Value));
    }

    [Fact]
    public void OnlyTheCuisineChanges()
    {
        var newer = HouseholdRecord.Write(
            new RecordKey("recipe", "black-bean-chili"), 7,
            new Dictionary<string, string>(RecipeMaps.Recipe.FieldsOf(RecipeData.Chili())) { ["nutrition"] = "future-value" },
            RecipeData.At(1), RecipeData.PartnerWriter);
        var document = HouseholdDocument.Empty.With(newer);

        var result = new CuisineMove(new RecipeId("black-bean-chili"), Cuisine.Mexican, Cuisine.Other)
            .Apply(document, RecipeData.At(2), RecipeData.OwnerWriter);

        var read = Assert.Single(HouseholdRecords.Read(result.Document, RecipeMaps.Recipe).Items);
        Assert.Equal(RecipeData.Chili() with { Cuisine = Cuisine.Other }, read);
        Assert.Equal("future-value", result.Document.Live(RecipeMaps.Recipe.KeyOf(read))!.Fields["nutrition"]);
    }

    [Fact]
    public void ADeletedCardIsRefusedInWords()
    {
        var document = RecipeData.Document(RecipeData.Box());
        var gone = HouseholdRecords.Remove(document, new RecordKey("recipe", "black-bean-chili"), RecipeData.At(4000), RecipeData.PartnerWriter);

        var result = new CuisineMove(new RecipeId("black-bean-chili"), Cuisine.Mexican, Cuisine.Other)
            .Apply(gone, RecipeData.At(5000), RecipeData.OwnerWriter);

        Assert.False(result.Accepted);
        Assert.Equal(CuisineMove.CardGoneSentence, result.Refusal);
        Assert.Same(gone, result.Document);
    }

    [Fact]
    public void AnUnreadableCardIsNotOverwritten()
    {
        var odd = HouseholdRecord.Write(
            new RecordKey("recipe", "black-bean-chili"), 9,
            new Dictionary<string, string>(RecipeMaps.Recipe.FieldsOf(RecipeData.Chili())) { ["cuisine"] = "thai" },
            RecipeData.At(1), RecipeData.PartnerWriter);
        var document = HouseholdDocument.Empty.With(odd);

        var result = new CuisineMove(new RecipeId("black-bean-chili"), Cuisine.Mexican, Cuisine.Other)
            .Apply(document, RecipeData.At(5000), RecipeData.OwnerWriter);

        Assert.False(result.Accepted);
        Assert.Equal("thai", result.Document.Live(odd.Key)!.Fields["cuisine"]);
    }

    [Fact]
    public void AStaleFromStillApplies()
    {
        // The other install moved it first; undoing our window still puts it where the owner last saw it.
        var document = RecipeData.Document(RecipeData.Box());
        var elsewhere = new CuisineMove(new RecipeId("black-bean-chili"), Cuisine.Mexican, Cuisine.Asian).Apply(document, RecipeData.At(4000), RecipeData.PartnerWriter).Document;

        var result = new CuisineMove(new RecipeId("black-bean-chili"), Cuisine.Other, Cuisine.Mexican).Apply(elsewhere, RecipeData.At(5000), RecipeData.OwnerWriter);

        Assert.Equal(Cuisine.Mexican, CuisineOf(result.Document, "black-bean-chili"));
    }
}
