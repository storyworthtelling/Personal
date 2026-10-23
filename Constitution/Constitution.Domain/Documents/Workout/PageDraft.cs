using System.Globalization;
using Constitution.Domain.Time;

namespace Constitution.Domain.Documents;

/// <summary>
/// One page being laid out. Text is placed by its baseline, as the book's pages are drawn, and the
/// bounds each item reports are the ink's own box: the font's height and the room under the baseline
/// for descenders, per role. Those two tables are measured from the embedded faces and held to them
/// by a read-back test, so a painter that draws a baseline where the book put it is checked, not assumed.
/// </summary>
internal sealed class PageDraft
{
    public const float Left = 36f;
    public const float ColumnWidth = 270f;
    public const float ColumnRight = Left + ColumnWidth;
    public const float RightEdge = 409f;
    public const float FullWidth = RightEdge - Left;

    public const float NotesTop = 100f;
    public const float NotesBottom = 556f;

    public const float DoneTop = 526f;
    public const float RatingLabelBaseline = 460f;
    public const float RatingTop = 474f;

    /// <summary>Content may run down to here; the rating's label and lanes own what is below.</summary>
    public const float ColumnContentLimit = 440f;

    /// <summary>On a page with no rating, content may run down to just above the Done tick.</summary>
    public const float TestContentLimit = 500f;

    /// <summary>Content on a cover may run down to the foot of the body.</summary>
    public const float CoverContentLimit = 552f;

    private const float FooterBaseline = 578f;
    private const float KickerBaseline = 20f;

    private readonly List<InkTarget> targets = [];
    private readonly List<PrintedItem> printed = [];
    private int counter;

    public PageDraft(string title) => Title = title;

    public string Title { get; }

    /// <summary>The height of a role's ink box, ascent and descent together.</summary>
    public static float InkHeight(PrintedRole role) => role switch
    {
        PrintedRole.Dose => 74f,
        PrintedRole.Voice => 27f,
        PrintedRole.Small => 15f,
        PrintedRole.Label => 14f,
        _ => 17f,
    };

    /// <summary>How far below its baseline a role's ink reaches (the face's descent at its rung).</summary>
    public static float Descent(PrintedRole role) => role switch
    {
        PrintedRole.Dose => 18f,
        PrintedRole.Voice => 8f,
        PrintedRole.Heading or PrintedRole.Choice => 4.5f,
        PrintedRole.Small => 4.2f,
        PrintedRole.Label => 3.9f,
        _ => 4.8f,
    };

    public PrintedItem Line(
        PrintedRole role, float left, float baseline, float width, string text,
        PrintedAlign align = PrintedAlign.Left, string? owner = null, string? name = null)
    {
        var bottom = baseline + Descent(role);
        var item = new PrintedItem(
            name ?? $"{role.ToString().ToLowerInvariant()}-{++counter}", role,
            new InkRect(left, bottom - InkHeight(role), width, InkHeight(role)), text, owner, align);
        printed.Add(item);
        return item;
    }

    /// <summary>The line's baseline, as the page lays it out (the reverse of <see cref="Line"/>).</summary>
    public static float BaselineOf(PrintedItem item) => item.Bounds.Bottom - Descent(item.Role);

    public void Target(InkTarget target) => targets.Add(target);

    public void Add(PrintedItem item) => printed.Add(item);

    public void AddRange(IEnumerable<PrintedItem> items) => printed.AddRange(items);

    public void Rule(string name, float left, float top, float width, float height, string? owner = null) =>
        printed.Add(new PrintedItem(name, PrintedRole.Rule, new InkRect(left, top, width, height), null, owner));

    public InkPageLayout ToLayout() => InkPageLayout.Create(Title, targets, printed);

    /// <summary>The small caps line above the title: "EXERCISE 2 OF 7 · STRENGTH".</summary>
    public void Kicker(string text) => Line(PrintedRole.Label, Left, KickerBaseline, FullWidth, text, name: "kicker");

    /// <summary>"Constitution · Strength · Tue Oct 20" on the left and "3 of 8" on the right.</summary>
    public void Footer(string kind, DateTime day, int page, int pages)
    {
        var date = day.ToString("ddd MMM d", CultureInfo.InvariantCulture);
        Line(PrintedRole.Small, Left, FooterBaseline, 300f, $"Constitution · {kind} · {date}", name: "footer");
        Line(PrintedRole.Small, RightEdge - 100f, FooterBaseline, 100f, $"{page} of {pages}", PrintedAlign.Right, name: "page-number");
    }

    /// <summary>"TUESDAY 5:30 PM": the day and the twelve-hour time, upper case for the kicker.</summary>
    public static string DayAndTime(DateTime start) =>
        (start.ToString("dddd", CultureInfo.InvariantCulture) + " " + TimeFormats.Clock12(start)).ToUpperInvariant();

    /// <summary>The 91 point ruled column on the right, with its edge, its label and its dotted lines (p.5).</summary>
    public void NotesColumn()
    {
        var notes = InkTargets.NotesMargin(WorkoutIds.Notes, NotesTop, NotesBottom);
        Target(notes);
        Rule("notes-edge", notes.Bounds.Left, notes.Bounds.Top, 0f, notes.Bounds.Height, notes.Id);
        Line(PrintedRole.Label, notes.Bounds.Left + 10f, NotesTop + 16f, notes.Bounds.Width - 10f, "NOTES", owner: notes.Id, name: "notes-label");

        var count = 0;
        for (var y = NotesTop + 40f; y <= NotesBottom - 20f; y += 22f)
        {
            printed.Add(new PrintedItem(
                $"notes-dots-{++count}", PrintedRole.Dotted,
                new InkRect(notes.Bounds.Left + 10f, y, notes.Bounds.Width - 10f, 0f), null, notes.Id));
        }
    }

    /// <summary>The large Done tick, fixed at the foot of every page so the hand finds it without looking.</summary>
    public void DoneTick()
    {
        var done = InkTargets.LargeTick(WorkoutIds.Done, Left, DoneTop, "this page is finished");
        Target(done);
        Line(PrintedRole.Strong, Left + 38f, 538f, 120f, "Done", owner: done.Id, name: "done-label");
        Line(PrintedRole.Small, Left + 38f, 551f, 160f, "tick when you finish", owner: done.Id, name: "done-hint");
    }

    /// <summary>Easy, right, hard: three 84 point lanes just above the tick. The planner learns which exercise was too much.</summary>
    public void Rating()
    {
        Line(PrintedRole.Label, Left, RatingLabelBaseline, 252f, "HOW WAS IT? CIRCLE ONE", name: "feel-label");
        Lanes(WorkoutIds.Rating, RatingTop, WorkoutIds.RatingWords);
    }

    /// <summary>A circle-a-word choice, each word centred in its own lane with a hairline between.</summary>
    public IReadOnlyList<InkTarget> Lanes(string group, float top, IReadOnlyList<string> words)
    {
        var set = InkTargets.CircleLanes(group, Left, top, words);
        foreach (var lane in set.Lanes)
        {
            Target(lane);
            Line(PrintedRole.Choice, lane.Bounds.Left, lane.Bounds.CentreY + 5f, lane.Bounds.Width, lane.Meaning, PrintedAlign.Centre, lane.Id, $"{lane.Id}-word");
        }

        AddRange(set.PrintedItems().Where(item => item.Role == PrintedRole.Rule));
        return set.Lanes;
    }

    /// <summary>
    /// The encouragement seat, one sentence to a line; hidden when there is none or it will not fit. A voiced line prints in the
    /// owner's hand; the plain sentence the seat falls back to prints in the plain body face (the hand is for the voice alone),
    /// on the same line pitch, so the page is laid out the same either way.
    /// </summary>
    public float Voice(string? text, float firstBaseline, float after, bool voiced = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return after;
        }

        var lines = VoiceLines(text, voiced);
        if (lines.Count == 0)
        {
            return after;
        }

        var baseline = firstBaseline;
        foreach (var line in lines)
        {
            Line(voiced ? PrintedRole.Voice : PrintedRole.Body, Left, baseline, ColumnWidth, line);
            baseline += 20f;
        }

        return baseline - 20f;
    }

    public const int VoiceMaxLines = 3;

    /// <summary>The lines the seat prints: none when there is no text or it would take more than three.</summary>
    public static IReadOnlyList<string> VoiceLines(string? text, bool voiced = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var lines = PageText.WrapBySentence(text, voiced ? PageText.VoiceLineChars : PageText.BodyColumnChars);
        return lines.Count > VoiceMaxLines ? [] : lines;
    }

    /// <summary>Refuses a page whose content would run into what is fixed below it.</summary>
    public static void RequireRoom(float lastBaseline, float limit, string what)
    {
        if (lastBaseline > limit)
        {
            throw new ArgumentException(
                $"{what} runs to {lastBaseline:0} points and would run into the part of the page that is fixed below it (the limit is {limit:0}).");
        }
    }
}
