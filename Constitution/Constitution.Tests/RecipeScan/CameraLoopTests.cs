using Constitution.Domain.Diagnostics;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// The partner's one-photo-per-page loop (C-6.5.2), as far as it can be built without a way to read a photograph on the phone: the
/// loop takes the lines each page was read into, and what it does with them is pure. The capture itself waits on a dependency
/// decision, so nothing here opens a camera.
/// </summary>
public class CameraLoopTests
{
    private static readonly string[] FirstPage = ["Apple crisp", "Ingredients", "6 apples, peeled and sliced", "¾ cup brown sugar"];

    private static readonly string[] SecondPage = ["1 cup rolled oats", "Directions", "Heat the oven to 350°F.", "Bake for 40 minutes, until bubbling."];

    [Fact]
    public void OnePhotoPerPage()
    {
        var loop = new CameraLoop();

        Assert.Equal(0, loop.Pages);
        Assert.True(loop.AddPage(FirstPage));
        Assert.Equal(1, loop.Pages);
        Assert.True(loop.AddPage(SecondPage));
        Assert.Equal(2, loop.Pages);
    }

    [Fact]
    public void SubtitleMatchesTheInstall()
    {
        var partner = RecipePickerModel.For(InstallRole.Partner, true, false).Rows[0].Subtitle;
        var owner = RecipePickerModel.For(InstallRole.Owner, true, false).Rows[0].Subtitle;

        Assert.Equal(RecipePickerText.CameraSubtitle, partner);
        Assert.Equal("One photo per page", partner);
        Assert.Equal(RecipePickerText.PlateSubtitle, owner);
        Assert.NotEqual(partner, owner);
    }

    [Fact]
    public void BetweenPagesItOffersAddAPageAndDone()
    {
        var loop = new CameraLoop();

        Assert.Equal("Add a page", loop.AddPageAction);
        Assert.Equal("Done", loop.DoneAction);
        Assert.False(loop.CanFinish);
        loop.AddPage(FirstPage);
        Assert.True(loop.CanFinish);
    }

    [Fact]
    public void ThePageCountIsSaidInWords()
    {
        var loop = new CameraLoop();
        Assert.Equal("No pages yet.", loop.Status);
        loop.AddPage(FirstPage);
        Assert.Equal("1 page read.", loop.Status);
        loop.AddPage(SecondPage);
        Assert.Equal("2 pages read.", loop.Status);
    }

    [Fact]
    public void APageWithNoWordsIsNotAdded()
    {
        var loop = new CameraLoop();

        Assert.False(loop.AddPage(["  ", string.Empty]));
        Assert.Equal(0, loop.Pages);
        Assert.Equal(CameraLoop.EmptyPageSentence, loop.Status);
    }

    [Fact]
    public void DoneReadsEveryPageInOrder()
    {
        var loop = new CameraLoop();
        loop.AddPage(FirstPage);
        loop.AddPage(SecondPage);

        var read = Assert.IsType<ScanReturn.Read>(loop.Finish());

        Assert.Equal("Apple crisp", read.Payload.Title);
        Assert.Equal(["6 apples, peeled and sliced", "¾ cup brown sugar", "1 cup rolled oats"], read.Payload.Ingredients);
        Assert.Equal(["Heat the oven to 350°F.", "Bake for 40 minutes, until bubbling."], read.Payload.Steps);
    }

    [Fact]
    public void DoneWithNoPagesIsABackOut()
    {
        Assert.IsType<ScanReturn.Cancelled>(new CameraLoop().Finish());
    }

    [Fact]
    public void PagesWithNothingARecipeAreRefusedPlainly()
    {
        var loop = new CameraLoop();
        loop.AddPage(["Happy birthday", "love from all of us"]);

        var refused = Assert.IsType<ScanReturn.Refused>(loop.Finish());

        Assert.Equal(CameraLoop.NothingOnTheCardSentence, refused.Sentence);
    }

    [Fact]
    public void FinishingForgetsTheLines()
    {
        var loop = new CameraLoop();
        loop.AddPage(FirstPage);
        loop.Finish();

        Assert.Equal(0, loop.Pages);
        Assert.False(loop.CanFinish);
        Assert.IsType<ScanReturn.Cancelled>(loop.Finish());
    }

    [Fact]
    public void ANextCardNeverCarriesTheLastOnesWords()
    {
        var loop = new CameraLoop();
        loop.AddPage(FirstPage);
        loop.Finish();
        loop.AddPage(["Plum jam", "Ingredients", "1 kg plums"]);

        var read = Assert.IsType<ScanReturn.Read>(loop.Finish());

        Assert.Equal(["1 kg plums"], read.Payload.Ingredients);
        Assert.Equal("Plum jam", read.Payload.Title);
    }

    [Fact]
    public void TheNoReaderSaysItCannotRead()
    {
        var reader = new NoRecipeTextReader();

        Assert.False(reader.CanRead);
        Assert.Null(reader.ReadPage());
    }
}
