using Constitution.Domain.ReadBack;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// The reply from the vision read, taken apart. The reply is the model's, so every shape of wrong has to land as
/// "not read" for the box or the page it spoils and never as an empty box: a reply that cannot be read must not
/// look like a quiet morning (R2, and the resolver's own rule that silence is only ever a sure, empty read).
/// </summary>
public class InkReplyTests
{
    private static readonly ExpectedDocument Document = Workout();

    // The id is asked for, and answered, qualified by its page ("p2:done"): the same two ids sit on every exercise page.
    private static string Entry(int page, string id, string marked = "false", string text = "null", string alternative = "null", string confidence = "0.95") =>
        Raw(page, InkInstruction.QualifiedId(page, id), marked, text, alternative, confidence);

    // An entry exactly as the reply spells it, so a reply that disagrees with itself can be written.
    private static string Raw(int page, string id, string marked = "false", string text = "null", string alternative = "null", string confidence = "0.95") =>
        $"{{\"page\":{page},\"id\":\"{id}\",\"marked\":{marked},\"text\":{text},\"alternative\":{alternative},\"confidence\":{confidence}}}";

    private static string Reply(string strokes, params string[] entries) =>
        $"{{\"penStrokes\":\"{strokes}\",\"targets\":[{string.Join(",", entries)}]}}";

    private static string Everything(string strokes = "seen", params string[] replaced) =>
        Reply(strokes, [.. new[] { 2, 3 }.SelectMany(page => new[] { "feel-1", "feel-2", "feel-3", "done" }.Select(id => Entry(page, id))), .. replaced]);

    [Fact]
    public void AWellFormedReplyIsReadPageByPage()
    {
        var read = InkReply.Parse(Reply("seen", Entry(2, "done", "true", confidence: "0.9"), Entry(2, "feel-1"), Entry(2, "feel-2"), Entry(2, "feel-3"),
            Entry(3, "done"), Entry(3, "feel-1"), Entry(3, "feel-2"), Entry(3, "feel-3")), Document);

        Assert.Equal(PenStrokes.Seen, read.PenStrokes);
        Assert.True(read.PageFor(2).WasRead);
        Assert.True(read.PageFor(3).WasRead);
        Assert.True(read.PageFor(2).For("done")!.Marked);
        Assert.Equal(0.9, read.PageFor(2).For("done")!.Confidence);
        Assert.False(read.PageFor(3).For("done")!.Marked);
    }

    [Fact]
    public void ProseAndCodeFencesAroundTheJsonAreTolerated()
    {
        var reply = "Here is what I see:\n```json\n" + Everything() + "\n```\nLet me know.";

        var read = InkReply.Parse(reply, Document);

        Assert.True(read.PageFor(2).WasRead);
        Assert.Equal(PenStrokes.Seen, read.PenStrokes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I could not make out the page.")]
    [InlineData("{\"penStrokes\":\"seen\",\"targets\":")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"penStrokes\":\"seen\"}")]
    [InlineData("{\"targets\":\"none\"}")]
    public void AReplyThatCannotBeReadLeavesEveryPageUnread(string? reply)
    {
        var read = InkReply.Parse(reply, Document);

        Assert.False(read.PageFor(2).WasRead);
        Assert.False(read.PageFor(3).WasRead);
        Assert.NotEmpty(read.PageFor(2).WhyNotRead);
        Assert.Equal(PenStrokes.Unknown, read.PenStrokes);
    }

    [Fact]
    public void APageTheReplyHasNothingForIsNotRead()
    {
        var read = InkReply.Parse(Reply("seen", Entry(2, "done"), Entry(2, "feel-1"), Entry(2, "feel-2"), Entry(2, "feel-3")), Document);

        Assert.True(read.PageFor(2).WasRead);
        Assert.False(read.PageFor(3).WasRead);
    }

    [Fact]
    public void ABoxTheReplyLeavesOutIsNotReported()
    {
        var read = InkReply.Parse(Reply("seen", Entry(2, "done"), Entry(2, "feel-1"), Entry(2, "feel-2")), Document);

        Assert.True(read.PageFor(2).WasRead);
        Assert.Null(read.PageFor(2).For("feel-3"));
        Assert.NotNull(read.PageFor(2).For("feel-2"));
    }

    [Fact]
    public void ABoxThePageDoesNotAskAboutIsIgnored()
    {
        var read = InkReply.Parse(Everything("seen", Entry(2, "invented-box", "true"), Entry(2, "notes", "true")), Document);

        Assert.Null(read.PageFor(2).For("invented-box"));
        Assert.Null(read.PageFor(2).For("notes"));
    }

    [Fact]
    public void AnAnswerForAPageTheDocumentDoesNotHaveIsIgnored()
    {
        var read = InkReply.Parse(Everything("seen", Entry(9, "done", "true"), Entry(1, "done", "true")), Document);

        Assert.True(read.PageFor(2).WasRead);
        Assert.False(read.PageFor(9).WasRead);
    }

    [Fact]
    public void ABoxAnsweredTwiceIsNotReportedAtAll()
    {
        var read = InkReply.Parse(Everything("seen", Entry(2, "done", "true")), Document);

        Assert.Null(read.PageFor(2).For("done"));
        Assert.NotNull(read.PageFor(3).For("done"));
    }

    [Fact]
    public void TextAndTheSecondReadingAreKept()
    {
        var read = InkReply.Parse(
            Reply("seen", Entry(2, "done", "true", "\"  12 \"", "\"17\"", "0.6"), Entry(2, "feel-1"), Entry(2, "feel-2"), Entry(2, "feel-3")),
            Document);

        var done = read.PageFor(2).For("done")!;
        Assert.Equal("12", done.Text);
        Assert.Equal("17", done.RunnerUpText);
        Assert.Equal(0.6, done.Confidence);
    }

    [Fact]
    public void ANumberWrittenAsAJsonNumberIsKeptAsItsText()
    {
        var read = InkReply.Parse(
            Reply("seen", Entry(2, "done", "true", "12", "17.5", "0.9"), Entry(2, "feel-1"), Entry(2, "feel-2"), Entry(2, "feel-3")), Document);

        var done = read.PageFor(2).For("done")!;
        Assert.Equal("12", done.Text);
        Assert.Equal("17.5", done.RunnerUpText);
    }

    [Theory]
    [InlineData("1.7", 1.0)]
    [InlineData("-0.2", 0.0)]
    [InlineData("null", 0.0)]
    [InlineData("\"high\"", 0.0)]
    public void ABadConfidenceIsPulledToASafeValue(string confidence, double expected)
    {
        var read = InkReply.Parse(Reply("seen", Entry(2, "done", confidence: confidence), Entry(2, "feel-1"), Entry(2, "feel-2"), Entry(2, "feel-3")), Document);

        Assert.Equal(expected, read.PageFor(2).For("done")!.Confidence);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    public void ANonBooleanMarkLeavesTheBoxOut(string marked)
    {
        var read = InkReply.Parse(Reply("seen", Entry(2, "done", marked), Entry(2, "feel-1"), Entry(2, "feel-2"), Entry(2, "feel-3")), Document);

        Assert.Null(read.PageFor(2).For("done"));
    }

    [Theory]
    [InlineData("seen", PenStrokes.Seen)]
    [InlineData("none", PenStrokes.None)]
    [InlineData("unsure", PenStrokes.Unknown)]
    [InlineData("SEEN", PenStrokes.Seen)]
    [InlineData("something else", PenStrokes.Unknown)]
    public void PenStrokesWordsAreMapped(string word, PenStrokes expected)
    {
        Assert.Equal(expected, InkReply.Parse(Everything(word), Document).PenStrokes);
    }

    [Fact]
    public void ADigestHasNothingToRead()
    {
        var read = InkReply.Parse(Everything(), Digest());

        Assert.False(read.PageFor(1).WasRead);
    }

    // ---- review finding S5: a page number that disagrees with the id is a box not read ----

    [Fact]
    public void APageNumberOffByOneCreditsNothing()
    {
        // The model numbered the pages from the cover: what it saw on page 2 it calls page 3, and the id it was given says 2.
        var read = InkReply.Parse(Everything("seen", Raw(3, "p2:done", "true")), Document);

        Assert.Null(read.PageFor(2).For("done"));
        Assert.Null(read.PageFor(3).For("done"));
        Assert.NotNull(read.PageFor(3).For("feel-1"));
    }

    [Fact]
    public void AMarkTheIdPutsOnOnePageAndTheNumberOnAnotherIsOnNeither()
    {
        var read = InkReply.Parse(Everything("seen", Raw(2, "p3:done", "true")), Document);

        Assert.Null(read.PageFor(2).For("done"));
        Assert.Null(read.PageFor(3).For("done"));
    }

    [Fact]
    public void AReplyWholeShiftedByOneReadsNoBoxAtAll()
    {
        var shifted = Reply(
            "seen",
            [.. new[] { 2, 3 }.SelectMany(page => new[] { "feel-1", "feel-2", "feel-3", "done" }.Select(id => Raw(page + 1, InkInstruction.QualifiedId(page, id), marked: id == "done" && page == 2 ? "true" : "false")))]);

        var read = InkReply.Parse(shifted, Document);

        Assert.False(read.PageFor(2).WasRead);
        Assert.False(read.PageFor(3).WasRead);
    }

    [Fact]
    public void AnIdWithNoPageInItIsNotAnswerEnough()
    {
        var bare = Reply("seen", [.. new[] { 2, 3 }.SelectMany(page => new[] { "feel-1", "feel-2", "feel-3", "done" }.Select(id => Raw(page, id)))]);

        var read = InkReply.Parse(bare, Document);

        Assert.False(read.PageFor(2).WasRead);
        Assert.False(read.PageFor(3).WasRead);
    }

    [Theory]
    [InlineData("p2done")]
    [InlineData("P2:done")]
    [InlineData("p:done")]
    [InlineData("p02:done")]
    [InlineData("p+2:done")]
    [InlineData("p2:")]
    [InlineData("2:done")]
    [InlineData("p2: done")]
    public void AnIdNotSpelledAsAskedIsIgnored(string id)
    {
        var read = InkReply.Parse(Everything("seen", Raw(2, id, "true")), Document);

        Assert.False(read.PageFor(2).For("done")!.Marked);
    }

    [Fact]
    public void AnAgreeingEntryForAnotherPageIsStillThatPages()
    {
        var read = InkReply.Parse(Everything("seen", Entry(3, "done", "true")), Document);

        // Page 3's done is answered twice (the sure empty one and this), which is the older rule: neither is taken.
        Assert.Null(read.PageFor(3).For("done"));
        Assert.NotNull(read.PageFor(2).For("done"));
    }

    [Fact]
    public void TheQualifiedIdIsPageThenBox()
    {
        Assert.Equal("p2:done", InkInstruction.QualifiedId(2, "done"));
        Assert.Equal("p12:feel-3", InkInstruction.QualifiedId(12, "feel-3"));
    }
}
