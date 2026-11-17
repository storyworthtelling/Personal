using Constitution.Domain.Voice;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>
/// The meal sheet opens with a line in the owner's voice (audit W10.4): told the week it is for, its new recipes and
/// its nights out, in words; hidden with no voice. The line is not part of what decides a resend, so a fresh wording
/// on another day does not send the sheet again.
/// </summary>
public class MealSheetIntroTests
{
    private static string TextOf(byte[] pdf)
    {
        using var document = Constitution.Tests.Documents.PdfReadBack.Open(pdf);
        return string.Concat(document.GetPages().Select(page => page.Text));
    }

    [Fact]
    public async Task TheSheetOpensInTheOwnersVoice()
    {
        var rig = new DocsRig();
        SeatRequest? asked = null;
        rig.Seams = rig.Seams with
        {
            Voice = (request, _) => { asked = request; return Task.FromResult<SeatLine?>(new SeatLine(request.Seat, "A quiet week of good dinners.")); },
        };

        await rig.RunAsync();

        Assert.Equal(VoiceSeat.MealSheetIntro, asked!.Seat);
        Assert.StartsWith("The dinner sheet is for the week of", asked.Facts[0], StringComparison.Ordinal);
        Assert.Contains("dinners.", TextOf(rig.Tablet.PdfOf(rig.SheetTitle)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoVoiceTheSheetGoesWithoutALine()
    {
        var rig = new DocsRig();
        rig.Seams = rig.Seams with { Voice = (_, _) => Task.FromResult<SeatLine?>(null) };

        var done = await rig.RunAsync();

        Assert.Equal(Constitution.Domain.Meals.Pipeline.MealDocumentVerdict.Delivered, rig.Sheet(done).Verdict);
        Assert.DoesNotContain("dinners.", TextOf(rig.Tablet.PdfOf(rig.SheetTitle)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewWordingDoesNotSendTheSheetAgain()
    {
        var rig = new DocsRig();
        var wording = "A quiet week of good dinners.";
        rig.Seams = rig.Seams with
        {
            Voice = (request, _) => Task.FromResult<SeatLine?>(new SeatLine(request.Seat, wording)),
        };

        await rig.RunAsync();
        wording = "Seven evenings, mostly at home.";
        await rig.RunAsync();

        Assert.Equal(1, rig.Tablet.Uploads);
    }
}
