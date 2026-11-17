using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// The recipe scan's photo route (D176): pages are photographed (in Plate on the owner's phone), held in memory only until the
/// owner says Read the recipe, read by Claude into a title, ingredient lines and step lines, and put into the three editable boxes.
/// </summary>
public class RecipePhotoTests
{
    private static byte[] Page(byte fill = 1) => [0xFF, 0xD8, fill, 0xFF, 0xD9];

    // ---- the loop of pages ----

    [Fact]
    public void ALoopStartsEmptyAndCannotFinish()
    {
        var loop = new RecipePhotoLoop();

        Assert.Equal(0, loop.Pages);
        Assert.False(loop.CanFinish);
        Assert.True(loop.CanAddPage);
        Assert.Equal("No pages yet.", loop.Status);
    }

    [Fact]
    public void EachPhotoIsAPageAndTheStatusCountsThem()
    {
        var loop = new RecipePhotoLoop();

        Assert.True(loop.AddPage(Page()));
        Assert.Equal("1 page taken.", loop.Status);
        Assert.True(loop.AddPage(Page(2)));
        Assert.Equal("2 pages taken.", loop.Status);
        Assert.True(loop.CanFinish);
    }

    [Fact]
    public void AnEmptyPhotoIsNotAPageAndTheLoopSaysSo()
    {
        var loop = new RecipePhotoLoop();

        Assert.False(loop.AddPage([]));

        Assert.Equal(0, loop.Pages);
        Assert.Equal(RecipePhotoLoop.EmptyPhotoSentence, loop.Status);
    }

    [Fact]
    public void ThePagesStopAtTheLimit()
    {
        var loop = new RecipePhotoLoop();
        for (var page = 0; page < RecipePhotoLoop.MaxPages; page++)
        {
            Assert.True(loop.AddPage(Page()));
        }

        Assert.False(loop.CanAddPage);
        Assert.False(loop.AddPage(Page()));
        Assert.Equal(RecipePhotoLoop.MaxPages, loop.Pages);
    }

    [Fact]
    public void TakingThePagesHandsThemOverInOrderAndForgetsThem()
    {
        var loop = new RecipePhotoLoop();
        loop.AddPage(Page(1));
        loop.AddPage(Page(2));

        var pages = loop.Take();

        Assert.Equal(2, pages.Count);
        Assert.Equal(1, pages[0][2]);
        Assert.Equal(2, pages[1][2]);
        Assert.Equal(0, loop.Pages);
        Assert.False(loop.CanFinish);
    }

    [Fact]
    public void TheReadingSentenceCountsThePages()
    {
        Assert.Equal("Reading the recipe from 1 page.", RecipePhotoText.ReadingSentence(1));
        Assert.Equal("Reading the recipe from 3 pages.", RecipePhotoText.ReadingSentence(3));
    }

    // ---- the reply ----

    [Fact]
    public void AGoodReplyIsTheThreeParts()
    {
        var read = RecipePhotoReply.Parse(
            "{\"is_recipe\":true,\"title\":\"Lemon Lentil Soup\",\"ingredients\":[\"1 cup red lentils\",\"2 lemons\"],"
            + "\"steps\":[\"Rinse the lentils.\",\"Simmer for 20 minutes.\"]}");

        var payload = Assert.IsType<RecipePhotoRead.Read>(read).Payload;
        Assert.Equal("Lemon Lentil Soup", payload.Title);
        Assert.Equal(["1 cup red lentils", "2 lemons"], payload.Ingredients);
        Assert.Equal(["Rinse the lentils.", "Simmer for 20 minutes."], payload.Steps);
    }

    [Fact]
    public void AReplyInACodeFenceOrWithWordsAroundItIsStillRead()
    {
        var read = RecipePhotoReply.Parse(
            "Here it is:\n```json\n{\"is_recipe\":true,\"title\":\"Toast\",\"ingredients\":[\"1 slice bread\"],\"steps\":[\"Toast it.\"]}\n```");

        Assert.Equal("Toast", Assert.IsType<RecipePhotoRead.Read>(read).Payload.Title);
    }

    [Fact]
    public void StepNumbersTheModelLeftInAreTakenOff()
    {
        var read = RecipePhotoReply.Parse(
            "{\"is_recipe\":true,\"title\":\"Rice\",\"ingredients\":[\"1 cup rice\"],\"steps\":[\"1. Rinse the rice.\",\"Step 2: Cook it.\",\"3) Rest it.\"]}");

        Assert.Equal(["Rinse the rice.", "Cook it.", "Rest it."], Assert.IsType<RecipePhotoRead.Read>(read).Payload.Steps);
    }

    [Fact]
    public void AQuantityIsNotMistakenForAStepNumber()
    {
        var read = RecipePhotoReply.Parse(
            "{\"is_recipe\":true,\"title\":\"Rice\",\"ingredients\":[\"1.5 cups rice\"],\"steps\":[\"2 cups of water go in first.\"]}");

        var payload = Assert.IsType<RecipePhotoRead.Read>(read).Payload;
        Assert.Equal(["1.5 cups rice"], payload.Ingredients);
        Assert.Equal(["2 cups of water go in first."], payload.Steps);
    }

    [Fact]
    public void NoRecipeInThePhotosSaysSo()
    {
        var read = RecipePhotoReply.Parse("{\"is_recipe\":false}");

        Assert.Equal(RecipePhotoText.NoRecipeSentence, Assert.IsType<RecipePhotoRead.Unreadable>(read).Sentence);
    }

    [Fact]
    public void ARecipeWithNoIngredientsAndNoStepsIsNoRecipe()
    {
        var read = RecipePhotoReply.Parse("{\"is_recipe\":true,\"title\":\"Soup\",\"ingredients\":[],\"steps\":[]}");

        Assert.Equal(RecipePhotoText.NoRecipeSentence, Assert.IsType<RecipePhotoRead.Unreadable>(read).Sentence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I can’t help with that.")]
    [InlineData("{\"is_recipe\":true,\"title\":\"Soup\",\"ingredients\":\"one onion\",\"steps\":[]}")]
    [InlineData("{\"is_recipe\":true,\"title\":\"Soup\",\"ingredients\":[1,2],\"steps\":[]}")]
    [InlineData("[\"not an object\"]")]
    public void AReplyThatMakesNoSenseIsUnreadableAndQuotesNothing(string? reply)
    {
        var read = RecipePhotoReply.Parse(reply);

        var sentence = Assert.IsType<RecipePhotoRead.Unreadable>(read).Sentence;
        Assert.Equal(RecipePhotoText.UnreadableSentence, sentence);
    }

    [Fact]
    public void AMissingTitleLeavesTheBoxEmptyRatherThanGuessing()
    {
        var read = RecipePhotoReply.Parse("{\"is_recipe\":true,\"ingredients\":[\"1 egg\"],\"steps\":[\"Boil it.\"]}");

        Assert.Equal(string.Empty, Assert.IsType<RecipePhotoRead.Read>(read).Payload.Title);
    }

    // ---- the prompt ----

    [Fact]
    public void ThePromptAsksForTheThreePartsAndLetsEverythingElseGo()
    {
        foreach (var word in new[] { "title", "ingredients", "steps", "is_recipe", "page number", "story", "tips", "JSON" })
        {
            Assert.Contains(word, RecipePhotoPrompt.System, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheQuestionNamesHowManyPagesThereAre()
    {
        Assert.Equal("Read the recipe on this page.", RecipePhotoPrompt.Question(1));
        Assert.Equal("Read the recipe across these 3 pages, in order.", RecipePhotoPrompt.Question(3));
    }

    // ---- into the three boxes ----

    [Fact]
    public void FillingTheBoxesPutsOneItemPerLineAndMarksThemAsRead()
    {
        var entry = new TypedEntry();
        var payload = new RecipeReadPayload("Lemon Lentil Soup", ["1 cup red lentils", "2 lemons"], ["Rinse.", "Simmer."]);

        entry.Fill(payload);

        Assert.Equal("Lemon Lentil Soup", entry.Title);
        Assert.Equal("1 cup red lentils\n2 lemons", entry.IngredientsText);
        Assert.Equal("Rinse.\nSimmer.", entry.StepsText);
        Assert.True(entry.FromPhotos);
        Assert.Equal(TypedEntry.ReadHeading, entry.PageHeading);
    }

    [Fact]
    public void FilledBoxesReadBackAsTheSameWords()
    {
        var entry = new TypedEntry();
        var payload = new RecipeReadPayload("Soup", ["1 onion"], ["Chop it."]);
        entry.Fill(payload);

        var read = entry.Read().Payload!;

        Assert.Equal(payload.Title, read.Title);
        Assert.Equal(payload.Ingredients, read.Ingredients);
        Assert.Equal(payload.Steps, read.Steps);
    }

    [Fact]
    public void TypedBoxesKeepTheTypingHeadingAndClearingForgetsThePhotos()
    {
        var entry = new TypedEntry();
        Assert.Equal(TypedEntry.Heading, entry.PageHeading);

        entry.Fill(new RecipeReadPayload("Soup", ["1 onion"], []));
        entry.Clear();

        Assert.False(entry.FromPhotos);
        Assert.Equal(TypedEntry.Heading, entry.PageHeading);
    }

    [Fact]
    public void TheKeepActionIsOneStepFromTheBoxes()
    {
        Assert.Equal("Keep the card", TypedEntry.KeepAction);
    }
}
