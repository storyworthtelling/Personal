using Constitution.Domain.Diagnostics;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// The picker that grows from the bar (C-6.5.4, book p.14): which ways in a phone is offered, in what order, and what choosing one
/// does. The paste row waits on written permission (PC8) and typing is always there.
/// </summary>
public class RecipePickerTests
{
    private static RecipePickerModel Picker(InstallRole role, bool reader = false, bool paste = false) =>
        RecipePickerModel.For(role, reader, paste);

    [Fact]
    public void PasteRowOnlyWithPermission()
    {
        Assert.DoesNotContain(Picker(InstallRole.Owner).Rows, row => row.Source == PickerSource.Paste);
        Assert.DoesNotContain(Picker(InstallRole.Partner, reader: true).Rows, row => row.Source == PickerSource.Paste);

        var allowed = Picker(InstallRole.Owner, paste: true).Rows.Single(row => row.Source == PickerSource.Paste);
        Assert.Equal("Paste an NYT Cooking link", allowed.Title);
        Assert.Equal("From the Cooking app’s Share button", allowed.Subtitle);
    }

    [Fact]
    public void TheOwnersScanRowOpensPlate()
    {
        var row = Picker(InstallRole.Owner).Rows[0];

        Assert.Equal(PickerSource.Plate, row.Source);
        Assert.Equal(RecipeBoxText.ScanAction, row.Title);
        Assert.Equal("Opens Plate · as many pages as it takes", row.Subtitle);
        Assert.True(row.Enabled);
    }

    [Fact]
    public void AnUnknownRoleStillOffersPlate()
    {
        var row = Picker(InstallRole.Unknown).Rows[0];

        Assert.Equal(PickerSource.Plate, row.Source);
        Assert.True(row.Enabled);
    }

    [Fact]
    public void ThePartnerWithAReaderGetsTheCamera()
    {
        var row = Picker(InstallRole.Partner, reader: true).Rows[0];

        Assert.Equal(PickerSource.Camera, row.Source);
        Assert.Equal(RecipeBoxText.ScanAction, row.Title);
        Assert.Equal("One photo per page", row.Subtitle);
        Assert.True(row.Enabled);
    }

    [Fact]
    public void ThePartnerWithoutAReaderIsToldSo()
    {
        var row = Picker(InstallRole.Partner).Rows[0];

        Assert.Equal(PickerSource.Camera, row.Source);
        Assert.False(row.Enabled);
        Assert.Equal(RecipePickerText.CannotReadSubtitle, row.Subtitle);
        Assert.Contains("can’t read a card", row.Subtitle, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(InstallRole.Owner, false, false)]
    [InlineData(InstallRole.Owner, true, true)]
    [InlineData(InstallRole.Partner, false, false)]
    [InlineData(InstallRole.Partner, true, true)]
    [InlineData(InstallRole.Unknown, false, true)]
    public void TypingIsAlwaysOfferedLast(InstallRole role, bool reader, bool paste)
    {
        var rows = Picker(role, reader, paste).Rows;

        var last = rows[^1];
        Assert.Equal(PickerSource.TypeIn, last.Source);
        Assert.True(last.Enabled);
        Assert.Equal("Type it in", last.Title);
    }

    [Fact]
    public void ItIsClosedUntilTheBarOpensIt()
    {
        var picker = Picker(InstallRole.Owner);

        Assert.False(picker.IsOpen);
        picker.Toggle();
        Assert.True(picker.IsOpen);
        picker.Toggle();
        Assert.False(picker.IsOpen);
    }

    [Fact]
    public void ChoosingARowClosesItAndNamesTheSource()
    {
        var picker = Picker(InstallRole.Owner);
        picker.Toggle();

        var choice = picker.Choose(PickerSource.TypeIn);

        Assert.Equal(PickerSource.TypeIn, choice.Started);
        Assert.Null(choice.Sentence);
        Assert.False(picker.IsOpen);
    }

    [Fact]
    public void ADisabledRowStaysOpenAndSaysWhy()
    {
        var picker = Picker(InstallRole.Partner);
        picker.Toggle();

        var choice = picker.Choose(PickerSource.Camera);

        Assert.Null(choice.Started);
        Assert.Equal(RecipePickerText.CannotReadSubtitle, choice.Sentence);
        Assert.True(picker.IsOpen);
    }

    [Fact]
    public void ARowThatIsNotThereStartsNothing()
    {
        var picker = Picker(InstallRole.Owner);
        picker.Toggle();

        var choice = picker.Choose(PickerSource.Paste);

        Assert.Null(choice.Started);
        Assert.Null(choice.Sentence);
        Assert.True(picker.IsOpen);
    }

    [Fact]
    public void TheBarsScanActionFollowsTheInstall()
    {
        Assert.Equal(PickerSource.Plate, Picker(InstallRole.Owner).Choose(PickerSource.Plate).Started);
        Assert.Equal(PickerSource.Plate, Picker(InstallRole.Owner).ScanChoice().Started);
        Assert.Equal(PickerSource.Camera, Picker(InstallRole.Partner, reader: true).ScanChoice().Started);

        var cannot = Picker(InstallRole.Partner).ScanChoice();
        Assert.Null(cannot.Started);
        Assert.Equal(RecipePickerText.CannotReadSubtitle, cannot.Sentence);
    }
}
