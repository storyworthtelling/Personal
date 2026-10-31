using System.Globalization;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;

namespace Constitution.Domain.Documents;

/// <summary>
/// The rating page, the last page of tonight's recipe (book p.9, C-2.5.4): "Who liked it?" with a row per eater naming the
/// dish that person ate (D37), the approved Loved / Fine / Never again circle lanes (D14), the last rating of that dish
/// pre-underlined or "FIRST TIME · PLEASE CIRCLE", the Never-again rule printed at the foot, and the "We didn't cook this
/// tonight" box (D68).
///
/// <para><b>One model builds the page, the rules that read it and the rows the taste profile reads</b>
/// (<see cref="Build"/>, <see cref="Rules"/>, <see cref="RatingRows"/>), so the ids cannot drift: the row's group is
/// <see cref="RecipeIds.RatingGroup"/>, the resolver reports a row's answer under that group, and
/// <see cref="TasteInk.ReadRatings"/> finds it there. The underline is a printed rule, not ink the reader looks at: only the
/// lanes are read, and an uncircled row is <see cref="InkResolution.Unchanged"/>.</para>
///
/// <para>Where the page departs from the book is in <c>RecipeRatingPageTests</c> and the build notes: a first-time label sits
/// lower than drawn so it keeps the page's own 8 point clearance from the lanes, and there is no notes margin (the lanes run
/// to 407, the book draws none).</para>
/// </summary>
public static class RecipeRatingPage
{
    /// <summary>The most eaters a page holds: the foot has to stay above the page's last line.</summary>
    public const int MostEaters = 4;

    private const string Title = "Who liked it?";
    private const string Kicker = "RECIPE · TONIGHT’S RATINGS · THE COOK RATES FOR EVERYONE";
    private const string FirstTime = "FIRST TIME · PLEASE CIRCLE";
    private const string RuleFirst = "“Never again” from one person means this dish is planned only";
    private const string RuleSecond = "when they eat something else or are away.";
    private const string DidntCookWords = "We didn’t cook this tonight";
    private const string DidntCookHint = "Tick it and the ratings are ignored; its fresh ingredients are written off.";

    /// <summary>
    /// The name column runs 36 to 119, 8 points short of the first lane. The book's own first dish line, "Sheet-pan chicken",
    /// measures 83 points in the real face, so the column is that wide and the lanes start 3 points right of where the book
    /// draws them (124): drawn there, the book's own line would sit 5 points from a lane, inside the 8 every target keeps.
    /// </summary>
    private const float NameWidth = 83f;

    private const int NameChars = 12;
    private const int DishChars = 17;
    private const int DishLines = 2;

    private const float FirstRule = 150f;
    private const float RowPitch = 66f;
    private const float LaneDrop = 12f;
    private const float NameDrop = 26f;
    private const float DishDrop = 41f;
    private const float DishPitch = 14f;
    private const float WordDrop = 31f;
    private const float UnderlineDrop = 36f;
    private const float DishTail = 11f;
    private const float LabelTail = 3.5f;

    /// <summary>
    /// A first-time label's baseline, counted from the row's rule. The book draws it at 55, 0.9 points under the lanes, inside
    /// the 8 point clearance every target keeps; this is the nearest place that clears (lane foot at 44, 8 clear, and the
    /// label's own ink above its baseline).
    /// </summary>
    private const float FirstTimeDrop = 62.5f;

    private const float LaneLeft = 127f;
    private const float LaneEnd = 407f;
    private const float UnderlinePerChar = 7.4f;

    private const float RuleLineDrop = 30f;
    private const float FootRuleDrop = 66f;
    private const float TickDrop = 82f;
    private const float TickLabelDrop = 95f;
    private const float TickHintDrop = 109f;
    private const float TickLabelLeft = 62f;

    /// <summary>The three lanes as the book draws them, 3 points right: 88, 88 and 104 wide (the last word is the longest).</summary>
    private static readonly float[] LaneEdges = [LaneLeft, 215f, 303f, LaneEnd];

    public static InkPageLayout Build(RecipeRatingModel model, int page, int total)
    {
        Validate(model, page, total);

        var draft = new PageDraft(Title);
        draft.Kicker(Kicker);
        Footer(draft, model, page, total);
        draft.Line(PrintedRole.Small, PageDraft.Left, 112f, PageDraft.FullWidth, "Underlined is each person’s last rating of this dish.", name: "intro-1");
        draft.Line(PrintedRole.Small, PageDraft.Left, 126f, PageDraft.FullWidth, "Circle only if it’s new or has changed. No circle means unchanged.", name: "intro-2");

        var top = FirstRule;
        draft.Rule("rule-1", PageDraft.Left, top, PageDraft.FullWidth, 0f);
        for (var n = 1; n <= model.Rows.Count; n++)
        {
            top += Row(draft, model.Rows[n - 1], n, top);
            draft.Rule($"rule-{n + 1}", PageDraft.Left, top, PageDraft.FullWidth, 0f);
        }

        Foot(draft, top, model);
        return draft.ToLayout();
    }

    /// <summary>The rows the taste profile reads the page's answers into, each under the group the page drew for it.</summary>
    public static IReadOnlyList<TasteRatingRow> RatingRows(RecipeRatingModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Rows.Select((row, index) => new TasteRatingRow(RecipeIds.RatingGroup(index + 1), row.Person, row.Recipe)).ToList();
    }

    /// <summary>What the book says each box means: a circle group per eater (uncircled is unchanged) and the one tick.</summary>
    public static IReadOnlyList<InkRule> Rules(RecipeRatingModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var rules = new List<InkRule>();
        for (var n = 1; n <= model.Rows.Count; n++)
        {
            var row = model.Rows[n - 1];
            rules.Add(new RatingRule($"{row.Name.Trim()}’s rating of {row.Dish.Trim()}", RecipeIds.RatingGroup(n)));
        }

        rules.Add(new WorkoutBoxRule("the “We didn’t cook this tonight” box", RecipeIds.DidntCook));
        return rules;
    }

    /// <summary>
    /// True only when the box was ticked, scribble included (D18). A box left blank, one the read was unsure of and one on a page
    /// that was not read are not "we didn't cook": a rating is dropped only on the owner's own mark.
    /// </summary>
    public static bool DidntCook(IReadOnlyList<InkResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        return resolutions.Any(r => r is InkResolution.Ticked && r.SubjectId == RecipeIds.DidntCook);
    }

    private static float Row(PageDraft draft, RecipeRatingRow row, int number, float top)
    {
        var group = RecipeIds.RatingGroup(number);
        var dish = DishText(row.Dish);
        draft.Line(PrintedRole.Strong, PageDraft.Left, top + NameDrop, NameWidth, row.Name.Trim(), name: $"{group}-name");
        for (var k = 0; k < dish.Count; k++)
        {
            draft.Line(PrintedRole.Small, PageDraft.Left, top + DishDrop + (DishPitch * k), NameWidth, dish[k], name: $"{group}-dish-{k + 1}");
        }

        var lanes = new List<InkTarget>();
        for (var i = 0; i < RecipeIds.RatingWords.Count; i++)
        {
            var word = RecipeIds.RatingWords[i];
            var lane = new InkTarget(
                $"{group}-{i + 1}", InkTargetKind.CircleLane,
                new InkRect(LaneEdges[i], top + LaneDrop, LaneEdges[i + 1] - LaneEdges[i], InkTargets.CircleLaneHeight), word, Group: group);
            lanes.Add(lane);
            draft.Target(lane);
            draft.Line(PrintedRole.Choice, lane.Bounds.Left, top + WordDrop, lane.Bounds.Width, word, PrintedAlign.Centre, lane.Id, $"{lane.Id}-word");
            if (i > 0)
            {
                draft.Rule($"{group}-divider-{i}", lane.Bounds.Left, lane.Bounds.Top, 0f, lane.Bounds.Height, group);
            }
        }

        var lastDish = DishDrop + (DishPitch * (dish.Count - 1));
        if (row.Last is { } last)
        {
            var lane = lanes[(int)last];
            var width = UnderlinePerChar * RecipeIds.RatingWords[(int)last].Length;
            draft.Rule($"{group}-underline", lane.Bounds.CentreX - (width / 2f), top + UnderlineDrop, width, 0f, lane.Id);
            return Math.Max(RowPitch, lastDish + DishTail);
        }

        var labelDrop = Math.Max(FirstTimeDrop, lastDish + DishPitch);
        draft.Line(PrintedRole.Label, PageDraft.Left, top + labelDrop, 200f, FirstTime, name: $"{group}-first-time");
        return Math.Max(RowPitch, labelDrop + LabelTail);
    }

    private static void Foot(PageDraft draft, float last, RecipeRatingModel model)
    {
        PageDraft.RequireRoom(last + TickHintDrop, PageDraft.CoverContentLimit, $"The rating page for {model.Rows.Count} eaters, with its foot,");
        draft.Line(PrintedRole.Small, PageDraft.Left, last + RuleLineDrop, PageDraft.FullWidth, RuleFirst, name: "rule-line-1");
        draft.Line(PrintedRole.Small, PageDraft.Left, last + RuleLineDrop + DishPitch, PageDraft.FullWidth, RuleSecond, name: "rule-line-2");
        draft.Rule("foot-rule", PageDraft.Left, last + FootRuleDrop, PageDraft.FullWidth, 0f);

        var tick = new InkTarget(
            RecipeIds.DidntCook, InkTargetKind.StandardTick,
            new InkRect(PageDraft.Left, last + TickDrop, InkTargets.StandardTickSize, InkTargets.StandardTickSize), "we did not cook this dinner tonight");
        draft.Target(tick);
        draft.Line(PrintedRole.Strong, TickLabelLeft, last + TickLabelDrop, 220f, DidntCookWords, owner: tick.Id, name: "didnt-cook-label");
        draft.Line(PrintedRole.Small, TickLabelLeft, last + TickHintDrop, PageDraft.RightEdge - TickLabelLeft, DidntCookHint, owner: tick.Id, name: "didnt-cook-hint");
    }

    /// <summary>The dish in at most two lines of the narrow name column; anything past them is cut with an ellipsis, never run into a lane.</summary>
    private static IReadOnlyList<string> DishText(string dish)
    {
        var lines = PageText.Wrap(dish.Trim(), DishChars).Select(Cut).ToList();
        if (lines.Count <= DishLines)
        {
            return lines;
        }

        var second = lines[1].TrimEnd('…');
        return [lines[0], (second.Length > DishChars - 1 ? second[..(DishChars - 1)] : second).TrimEnd() + "…"];
    }

    private static string Cut(string line) => line.Length <= DishChars ? line : line[..(DishChars - 1)] + "…";

    private static void Footer(PageDraft draft, RecipeRatingModel model, int page, int total)
    {
        var date = model.Day.ToString("ddd MMM d", CultureInfo.InvariantCulture);
        draft.Line(PrintedRole.Small, PageDraft.Left, 578f, 300f, $"Constitution · Recipe · {date} · {model.Cook.Trim()}", name: "footer");
        draft.Line(PrintedRole.Small, PageDraft.RightEdge - 100f, 578f, 100f, $"{page.ToString(CultureInfo.InvariantCulture)} of {total.ToString(CultureInfo.InvariantCulture)}", PrintedAlign.Right, name: "page-number");
    }

    private static void Validate(RecipeRatingModel model, int page, int total)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.Cook))
        {
            throw new ArgumentException("The rating page needs the cook's name for its footer.", nameof(model));
        }

        if (model.Rows.Count is < 1 or > MostEaters)
        {
            throw new ArgumentException($"A rating page has a row for each of one to {MostEaters} eaters; this one was given {model.Rows.Count}.", nameof(model));
        }

        if (page < 1 || total < page)
        {
            throw new ArgumentException($"Page {page} of {total} is not a page of a document.", nameof(page));
        }

        foreach (var row in model.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || row.Name.Trim().Length > NameChars)
            {
                throw new ArgumentException($"The name '{row.Name}' does not fit the rating row's name column; it takes one to {NameChars} characters.", nameof(model));
            }

            if (string.IsNullOrWhiteSpace(row.Dish))
            {
                throw new ArgumentException($"{row.Name.Trim()} has no dish to rate; the row names the dish that person ate.", nameof(model));
            }
        }
    }
}
