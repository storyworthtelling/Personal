using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Tests.Documents;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// What the vision read is told (R2). The page goes with a list of boxes, each in its own words and its own
/// place, and nothing else about the document or the person: a model that is asked about "box 3" without being
/// told where it is, or one that is handed the plan, is a model that guesses.
/// </summary>
public class InkInstructionTests
{
    [Fact]
    public void TheSystemTextIsTheSameForEveryDocument()
    {
        // A property over constant text: the same value every time, so a model call can cache it.
        Assert.Equal(InkInstruction.System, InkInstruction.System);
        Assert.NotEmpty(InkInstruction.System);
    }

    [Fact]
    public void TheSystemTextCarriesNothingAboutThisDocument()
    {
        var document = Workout();
        var system = InkInstruction.System;

        Assert.DoesNotContain(Person, system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Goblet", system, StringComparison.Ordinal);
        Assert.DoesNotContain(document.Name, system, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSystemTextSaysAnyMarkIsAMarkAndPrintIsNot()
    {
        var system = InkInstruction.System;

        Assert.Contains("scribble", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("printed", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("circle", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSystemTextDescribesTheReplyItWants()
    {
        var system = InkInstruction.System;

        foreach (var word in new[] { "penStrokes", "targets", "page", "id", "marked", "text", "alternative", "confidence" })
        {
            Assert.Contains(word, system, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheSystemTextNamesNoModelAndHoldsNoKey()
    {
        Assert.DoesNotContain("claude", InkInstruction.System, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", InkInstruction.System, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRequestDescribesEveryAskedBox()
    {
        var document = Workout();

        var request = InkInstruction.Request(document);

        foreach (var page in document.Pages)
        {
            Assert.Contains($"Page {page.Number}", request, StringComparison.Ordinal);
            foreach (var target in page.AskedTargets)
            {
                Assert.Contains(target.Describe(), request, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TheRequestQualifiesEveryIdByItsPage()
    {
        var request = InkInstruction.Request(Workout());

        // Both exercise pages print a "done" box and a "feel" choice; only the page-qualified id says which is meant.
        Assert.Contains("- p2:done is ", request, StringComparison.Ordinal);
        Assert.Contains("- p3:done is ", request, StringComparison.Ordinal);
        Assert.Contains("- p2:feel-1 is ", request, StringComparison.Ordinal);
        Assert.Contains("- p3:feel-1 is ", request, StringComparison.Ordinal);
        Assert.DoesNotContain("- done is ", request, StringComparison.Ordinal);
        Assert.DoesNotContain("- feel-1 is ", request, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSystemTextShowsTheQualifiedIdInTheReply()
    {
        Assert.Contains("\"id\":\"p2:done\"", InkInstruction.System, StringComparison.Ordinal);
        Assert.Contains("qualified", InkInstruction.System, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheRequestSaysWhereEachBoxIs()
    {
        var done = Workout().Pages[0].AskedTargets.Single(target => target.Id == "done");

        var request = InkInstruction.Request(Workout());

        // The box is 28 points square at 36, 526: it is told as from-and-to corners in page points.
        Assert.Equal(new InkRect(36, 526, 28, 28), done.Bounds);
        Assert.Contains("box from 36,526 to 64,554", request, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRequestSaysHowBigThePageIs()
    {
        Assert.Contains("445", InkInstruction.Request(Workout()), StringComparison.Ordinal);
        Assert.Contains("594", InkInstruction.Request(Workout()), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRequestLeavesTheNotesMarginUnasked()
    {
        var request = InkInstruction.Request(Workout());

        Assert.DoesNotContain("notes written in the margin", request, StringComparison.Ordinal);
        Assert.DoesNotContain("notes is", request, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRequestNeverNamesThePersonOrThePlan()
    {
        var request = InkInstruction.Request(Workout());

        Assert.DoesNotContain(Person, request, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("25 lb", request, StringComparison.Ordinal);
        Assert.DoesNotContain("Five more pounds", request, StringComparison.Ordinal);
        Assert.DoesNotContain(Workout().Name, request, StringComparison.Ordinal);
        Assert.DoesNotContain("2026", request, StringComparison.Ordinal);
    }

    [Fact]
    public void ANumberBoxIsToldItsUnit()
    {
        var layouts = AssessmentDocument.Build(WorkoutSamples.Assessment());
        var page = layouts[1];
        var expected = new ExpectedPage(
            2, new InkReadout(page.Map), [new Constitution.Domain.Ink.NumberRule("Push-ups in a row", "result")]);
        var document = new ExpectedDocument(DeliveryKind.Workout, Day, "Assessment", Person, SessionKind.Strength, [expected]);

        var request = InkInstruction.Request(document);

        Assert.Contains("result is Push-ups in a row, in reps", request, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithNoInkAsksNothing()
    {
        Assert.Equal(string.Empty, InkInstruction.Request(Digest()));
    }

    [Fact]
    public void TheRequestReadsTheSameInAnyCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            // A box at a fractional point would print "36,5" in German and "36.5" in English; the model must see one spelling.
            var readout = new InkReadout(new InkMap([new InkTarget("done", InkTargetKind.LargeTick, new InkRect(36.5f, 526.25f, 28, 28), "this page is finished")]));
            var document = new ExpectedDocument(
                DeliveryKind.Workout, Day, null, Person, SessionKind.Strength,
                [new ExpectedPage(2, readout, [new Constitution.Domain.Ink.WorkoutBoxRule("squats", "done")])]);

            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            var english = InkInstruction.Request(document);
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var german = InkInstruction.Request(document);

            Assert.Equal(english, german);
            Assert.Contains("36.5,526.25", english, StringComparison.Ordinal);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
