using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;

namespace Constitution.Tests.Documents;

/// <summary>
/// C-2.1.2: one source of rectangles. The painter draws from the map and the reader reads the same
/// instances, so "box 3 is push-ups, in reps" is looked up, never guessed. The sizes are page 4's.
/// </summary>
public class InkTargetsTests
{
    [Fact]
    public void SizesMatchPageFour()
    {
        var large = InkTargets.LargeTick("done", 40, 100, "finished");
        Assert.Equal((28f, 28f), (large.Bounds.Width, large.Bounds.Height));
        Assert.Equal(InkTargetKind.LargeTick, large.Kind);

        var rows = InkTargets.TickRows("buy", 40, 100, ["a", "b", "c"]);
        Assert.All(rows, t => Assert.Equal((18f, 18f), (t.Bounds.Width, t.Bounds.Height)));
        Assert.Equal(26f, rows[1].Bounds.Top - rows[0].Bounds.Top);
        Assert.Equal(26f, rows[2].Bounds.Top - rows[1].Bounds.Top);

        var three = InkTargets.CircleLanes("feel", 40, 100, ["easy", "right", "hard"]);
        Assert.All(three.Lanes, lane => Assert.Equal(84f, lane.Bounds.Width));

        var four = InkTargets.CircleLanes("pick", 40, 100, ["one", "two", "three", "four"]);
        Assert.All(four.Lanes, lane => Assert.Equal(68f, lane.Bounds.Width));

        var number = InkTargets.NumberBox("load", 40, 100, "weight", "pounds");
        Assert.Equal((96f, 40f), (number.Bounds.Width, number.Bounds.Height));
        Assert.Equal("pounds", number.Unit);

        var notes = InkTargets.NotesMargin("notes", 100, 500);
        Assert.Equal(91f, notes.Bounds.Width);
        Assert.Equal(ConstitutionPage.Width - ConstitutionPage.MarginRight, notes.Bounds.Right);
    }

    [Fact]
    public void LanesTouchWithAHairlineBetween()
    {
        var lanes = InkTargets.CircleLanes("feel", 40, 100, ["easy", "right", "hard"]);

        Assert.Equal(lanes.Lanes[0].Bounds.Right, lanes.Lanes[1].Bounds.Left);
        Assert.Equal(lanes.Lanes[1].Bounds.Right, lanes.Lanes[2].Bounds.Left);
        Assert.Equal(2, lanes.Dividers.Count);
        Assert.Equal(lanes.Lanes[0].Bounds.Right, lanes.Dividers[0].Left);
        Assert.Equal(lanes.Lanes[1].Bounds.Right, lanes.Dividers[1].Left);
        Assert.All(lanes.Dividers, d => Assert.Equal(0f, d.Width));
        Assert.All(lanes.Lanes, lane => Assert.Equal("feel", lane.Group));
    }

    [Fact]
    public void ALaneSetOfOneOrFiveWordsIsRefused()
    {
        var one = Assert.Throws<ArgumentOutOfRangeException>(() => InkTargets.CircleLanes("g", 0, 0, ["only"]));
        var five = Assert.Throws<ArgumentOutOfRangeException>(() => InkTargets.CircleLanes("g", 0, 0, ["a", "b", "c", "d", "e"]));

        Assert.Contains("two to four", one.Message, StringComparison.Ordinal);
        Assert.Contains("two to four", five.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUnitIsPrintedOutsideTheNumberBox()
    {
        var box = InkTargets.NumberBox("load", 40, 100, "weight", "pounds");
        var unit = InkTargets.UnitLabelBounds(box, 60);

        Assert.False(unit.Overlaps(box.Bounds));
        Assert.True(unit.Left - box.Bounds.Right >= InkTargets.MinimumClearance);
    }

    [Fact]
    public void TheReasonFieldSitsOppositeYes()
    {
        var yes = InkTargets.TickRows("yes1", 40, 100, ["serve"])[0];
        var reason = InkTargets.NoBecause("no1", yes, 409, 119);

        Assert.Equal(InkTargetKind.NoBecause, reason.Kind);
        Assert.True(reason.Bounds.Left > yes.Bounds.Right + InkTargets.MinimumClearance);
        Assert.Equal(yes.Bounds.CentreY, reason.Bounds.CentreY, 0.01f);
        Assert.Equal(409f, reason.Bounds.Right);
        Assert.Equal(119f, reason.Bounds.Width);
    }

    [Fact]
    public void EveryTargetNeedsAWordForWhatItMeans()
    {
        Assert.Throws<ArgumentException>(() => InkTargets.LargeTick("done", 40, 100, " "));
        Assert.Throws<ArgumentException>(() => InkTargets.NumberBox("load", 40, 100, "weight", ""));
        Assert.Throws<ArgumentException>(() => InkTargets.TickRows("buy", 40, 100, ["a", ""]));
    }

    [Fact]
    public void ADescriptionNamesTheBoxAndItsUnit()
    {
        var number = InkTargets.NumberBox("box3", 40, 100, "push-ups", "reps");
        var tick = InkTargets.LargeTick("done", 40, 200, "this exercise is finished");

        Assert.Equal("box3 is push-ups, in reps", number.Describe());
        Assert.Equal("done is this exercise is finished", tick.Describe());
    }

    [Fact]
    public void EveryTargetKeepsEightPointsClear()
    {
        foreach (var page in DocumentSamples.Pages())
        {
            Assert.Empty(page.Violations().Where(v => v.Kind == InkViolationKind.TooClose));
        }

        // Exactly eight is clear; standard rows are 26 apart for 18-point boxes, which is exactly that.
        var rows = InkTargets.TickRows("buy", 40, 100, ["a", "b"]);
        var two = InkPageLayout.Create("Two", rows, [DocumentSamples.Text("a", PrintedRole.Label, 66, 100, 40, 16, "a", rows[0].Id), DocumentSamples.Text("b", PrintedRole.Label, 66, 126, 40, 16, "b", rows[1].Id)]);
        Assert.Empty(two.Violations());
    }

    [Fact]
    public void ACrowdedTargetIsCaught()
    {
        var near = new InkTarget("near", InkTargetKind.StandardTick, new InkRect(40, 122, 18, 18), "near");
        var first = new InkTarget("first", InkTargetKind.StandardTick, new InkRect(40, 100, 18, 18), "first");
        var twoTargets = InkPageLayout.Create("Crowded", [first, near], [Caption("c1", "first", 62, 100), Caption("c2", "near", 62, 123)]);
        Assert.Contains(twoTargets.Violations(), v => v.Kind == InkViolationKind.TooClose && v.TargetId == "first");

        var tick = new InkTarget("tick", InkTargetKind.StandardTick, new InkRect(40, 100, 18, 18), "tick");
        var label = DocumentSamples.Text("label", PrintedRole.Label, 58 + 5, 100, 40, 16, "Close", tick.Id);
        var byLabel = InkPageLayout.Create("Label", [tick], [label]);
        Assert.Contains(byLabel.Violations(), v => v.Kind == InkViolationKind.TooClose && v.TargetId == "tick");

        var stranger = DocumentSamples.Text("stranger", PrintedRole.Body, 58 + 5, 100, 40, 16, "Other", null);
        var byStranger = InkPageLayout.Create("Stranger", [tick], [Caption("c", "tick", 80, 100), stranger]);
        Assert.Contains(byStranger.Violations(), v => v.Kind == InkViolationKind.TooClose && v.Other == "stranger");
    }

    [Fact]
    public void NothingPrintsInsideATarget()
    {
        foreach (var page in DocumentSamples.Pages())
        {
            Assert.Empty(page.Violations().Where(v => v.Kind == InkViolationKind.PrintedInside));
        }

        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(DocumentSamples.Pages()));
        var layouts = DocumentSamples.Pages();
        for (var i = 0; i < layouts.Count; i++)
        {
            var page = document.GetPage(i + 1);
            foreach (var target in layouts[i].Map.Targets.Where(t => !t.AllowsOwnPrintInside))
            {
                foreach (var letter in page.Letters)
                {
                    Assert.False(
                        PdfReadBack.Flip(page, letter.GlyphRectangle).Overlaps(target.Bounds),
                        $"The letter '{letter.Value}' was printed inside {target.Id} on page {i + 1}.");
                }
            }
        }
    }

    [Fact]
    public void PrintedInsideATickIsCaught()
    {
        var tick = new InkTarget("tick", InkTargetKind.StandardTick, new InkRect(40, 100, 18, 18), "tick");
        var inside = DocumentSamples.Text("x", PrintedRole.Label, 42, 102, 10, 10, "x", "tick");
        var layout = InkPageLayout.Create("Inside", [tick], [inside]);

        Assert.Contains(layout.Violations(), v => v.Kind == InkViolationKind.PrintedInside && v.TargetId == "tick");
    }

    [Fact]
    public void OnlyALanesOwnWordSitsInIt()
    {
        var lanes = InkTargets.CircleLanes("feel", 40, 100, ["easy", "right"]);
        var ok = InkPageLayout.Create("Lanes", lanes.Lanes, lanes.PrintedItems().ToList());
        Assert.Empty(ok.Violations().Where(v => v.Kind == InkViolationKind.PrintedInside));

        var intruder = DocumentSamples.Text("intruder", PrintedRole.Body, 50, 105, 60, 16, "Hello", null);
        var bad = InkPageLayout.Create("Lanes", lanes.Lanes, lanes.PrintedItems().Append(intruder).ToList());
        Assert.Contains(bad.Violations(), v => v.Kind == InkViolationKind.PrintedInside && v.Other == "intruder");
    }

    [Fact]
    public void ATargetOutsideTheBodyIsCaught()
    {
        var off = new InkTarget("off", InkTargetKind.LargeTick, new InkRect(430, 100, 28, 28), "off");
        var under = new InkTarget("under", InkTargetKind.LargeTick, new InkRect(40, ConstitutionPage.HeaderRuleTop + 2, 28, 28), "under");
        var layout = InkPageLayout.Create("Bad", [off, under], [Caption("c1", "off", 60, 100), Caption("c2", "under", 80, ConstitutionPage.HeaderRuleTop + 2)]);

        Assert.Contains(layout.Violations(), v => v.Kind == InkViolationKind.OutsideTheBody && v.TargetId == "off");
        Assert.Contains(layout.Violations(), v => v.Kind == InkViolationKind.OutsideTheBody && v.TargetId == "under");
    }

    [Fact]
    public void ATargetWithNoWordsPrintedForItIsCaught()
    {
        var tick = new InkTarget("tick", InkTargetKind.LargeTick, new InkRect(40, 100, 28, 28), "finished");
        var layout = InkPageLayout.Create("Mute", [tick], []);

        Assert.Contains(layout.Violations(), v => v.Kind == InkViolationKind.Unnamed && v.TargetId == "tick");
    }

    [Fact]
    public void TwoTargetsMayNotShareAnId()
    {
        var a = new InkTarget("same", InkTargetKind.LargeTick, new InkRect(40, 100, 28, 28), "a");
        var b = new InkTarget("same", InkTargetKind.LargeTick, new InkRect(40, 200, 28, 28), "b");

        var error = Assert.Throws<ArgumentException>(() => InkPageLayout.Create("Twins", [a, b], []));
        Assert.Contains("same", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PainterAndReaderShareOneMap()
    {
        var layouts = DocumentSamples.Pages();
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(layouts));

        for (var i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];
            var page = document.GetPage(i + 1);
            var paths = PdfReadBack.Paths(page);
            var reader = new InkReadout(layout.Map);

            Assert.Same(layout.Map, reader.Map);

            foreach (var target in layout.Map.Targets)
            {
                // What the painter drew sits exactly where the map says.
                if (target.Kind is InkTargetKind.LargeTick or InkTargetKind.StandardTick or InkTargetKind.NumberBox or InkTargetKind.NoBecause)
                {
                    Assert.Contains(paths, p => p.Stroked && PdfReadBack.Same(p.Bounds, target.Bounds));
                }

                // And the reader, asked about the middle of that box, hands back the very same instance.
                Assert.Same(target, reader.TargetAt(target.Bounds.CentreX, target.Bounds.CentreY));
                Assert.Same(target, layout.Map.ById(target.Id));
            }
        }
    }

    [Fact]
    public void AMarkAcrossTwoLanesTouchesBoth()
    {
        var lanes = InkTargets.CircleLanes("feel", 40, 100, ["easy", "right", "hard"]);
        var map = new InkMap(lanes.Lanes);
        var reader = new InkReadout(map);

        var circle = new InkRect(lanes.Lanes[0].Bounds.Right - 12, 104, 24, 24);
        var touched = reader.TargetsTouching(circle);

        Assert.Equal(["feel-1", "feel-2"], touched.Select(t => t.Id));
        Assert.Single(reader.TargetsTouching(new InkRect(lanes.Lanes[2].Bounds.Left + 10, 104, 20, 20)));
    }

    [Fact]
    public void ThePainterCannotBeHandedARectangle()
    {
        var methods = typeof(ConstitutionDocumentRenderer).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(float) || p.ParameterType == typeof(InkRect));
        }
    }

    private static PrintedItem Caption(string name, string owner, float left, float top) =>
        DocumentSamples.Text(name, PrintedRole.Label, left, top, 40, 16, owner, owner);
}
