using Constitution.Domain.Documents;
using Constitution.Domain.Waiting;

namespace Constitution.Domain.Ink;

/// <summary>
/// C-2.2.1. Turns what the vision read saw into what the owner answered, by exactly the rules the book
/// wrote down and no others. Pure: the read and the clock are passed in; nothing is stored or sent.
///
/// <para><b>Every default is named.</b> Silence resolves only through a rule above, and only when the
/// read was <i>sure</i> the target was empty. A page that was not read, a target the read left out and
/// a read below <see cref="MinimumConfidence"/> are never silence: they come back as
/// <see cref="InkResolution.NotRead"/> or <see cref="InkResolution.NeedsAsking"/>, so a failed call
/// cannot approve a dinner or skip a workout.</para>
///
/// <para><b>The rules</b> (D14 to D18, D28; p.4, p.5, p.7, p.9): any mark in a box is a tick; a written
/// reason on a meal row denies and beats a tick (D17); an empty workout box is skipped; an untouched meal
/// item is approved at its deadline and not before; an uncircled rating is unchanged; a blank "Still
/// sore?" is better (D28); a blank staple is no need; a circled word with no Done tick makes the page done
/// with that rating. A circle that touches two lanes is two marked lanes, and is asked, not guessed.</para>
/// </summary>
public sealed class InkResolver
{
    /// <summary>A read below this is not acted on; it is asked about.</summary>
    public const double MinimumConfidence = 0.75;

    private const string Unchanged = "Unchanged";
    private const string TickedChip = "Ticked";
    private const string NotTickedChip = "Not ticked";
    private const string KeepChip = "Keep it";
    private const string LeaveOutChip = "Leave it out";
    private const string StillSoreChip = "Still sore";
    private const string BetterChip = "Better";
    private const string NeedChip = "Need it";
    private const string NoNeedChip = "No need";
    private const string BlankChip = "Blank";
    private const string ElseChip = "Something else";

    public InkResolver(InkReadout readout) => Readout = readout ?? throw new ArgumentNullException(nameof(readout));

    /// <summary>The page's map, the one the painter drew from; the resolver finds a group's lanes and their words in it.</summary>
    public InkReadout Readout { get; }

    /// <summary>
    /// Resolves each rule against the page, in the order the rules came. A rule that names a target the
    /// page does not have is the caller's bug and throws; a target the <i>read</i> did not report is not,
    /// and resolves to <see cref="InkResolution.NotRead"/>.
    /// </summary>
    public IReadOnlyList<InkResolution> Resolve(InkPageRead page, IReadOnlyList<InkRule> rules, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(rules);

        var found = new List<InkResolution>();
        foreach (var rule in rules)
        {
            found.AddRange(rule switch
            {
                WorkoutBoxRule r => WorkoutBox(page, r),
                ExercisePageRule r => ExercisePage(page, r),
                MealItemRule r => MealItem(page, r, now),
                RatingRule r => [RateGroup(page, r.Group, r.Subject)],
                StillSoreRule r => StillSore(page, r),
                StapleRule r => Staple(page, r),
                NumberRule r => Number(page, r),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(rules), rule, "This rule has no resolution; add it to InkResolver."),
            });
        }

        return found;
    }

    private IReadOnlyList<InkResolution> WorkoutBox(InkPageRead page, WorkoutBoxRule rule)
    {
        var box = Readout.Map.ById(rule.BoxId);
        if (NotReadWhy(page, box.Id) is { } why)
        {
            return [new InkResolution.NotRead(box.Id, why)];
        }

        var read = page.For(box.Id)!;
        if (Doubtful(read))
        {
            return [Ask(box.Id, rule.Subject, TickedChip, NotTickedChip)];
        }

        return [read.Marked ? new InkResolution.Ticked(box.Id) : new InkResolution.Skipped(box.Id)];
    }

    private IReadOnlyList<InkResolution> ExercisePage(InkPageRead page, ExercisePageRule rule)
    {
        var done = Readout.Map.ById(rule.DoneBoxId);
        var rating = RateGroup(page, rule.RatingGroup, rule.Subject);

        InkResolution doneResolution;
        if (NotReadWhy(page, done.Id) is { } why)
        {
            doneResolution = new InkResolution.NotRead(done.Id, why);
        }
        else if (Doubtful(page.For(done.Id)!))
        {
            doneResolution = Ask(done.Id, rule.Subject, TickedChip, NotTickedChip);
        }
        else if (page.For(done.Id)!.Marked)
        {
            doneResolution = new InkResolution.Ticked(done.Id);
        }
        else
        {
            // A sure, empty Done box: what the page means depends on the rating beside it (p.5).
            switch (rating)
            {
                case InkResolution.Circled circled:
                    return [new InkResolution.PageDone(done.Id, circled.Word)];
                case InkResolution.Unchanged:
                    doneResolution = new InkResolution.Skipped(done.Id);
                    break;
                case InkResolution.NotRead notRead:
                    doneResolution = new InkResolution.NotRead(done.Id, notRead.Why);
                    break;
                default:
                    // The rating is waiting on a question, and until it is answered the page is neither done nor skipped.
                    doneResolution = new InkResolution.Undecided(done.Id);
                    break;
            }
        }

        return [doneResolution, rating];
    }

    private IReadOnlyList<InkResolution> MealItem(InkPageRead page, MealItemRule rule, DateTimeOffset now)
    {
        var yes = Readout.Map.ById(rule.YesBoxId);
        var reason = Readout.Map.ById(rule.ReasonId);

        // The reason is looked at first because it wins: a clear denial stands whatever the tick read says (D17).
        if (NotReadWhy(page, reason.Id) is { } reasonWhy)
        {
            return [new InkResolution.NotRead(yes.Id, reasonWhy)];
        }

        var reasonRead = page.For(reason.Id)!;
        if (Doubtful(reasonRead))
        {
            return [Ask(yes.Id, rule.Subject, KeepChip, LeaveOutChip)];
        }

        if (reasonRead.Text is not null)
        {
            return [new InkResolution.Denied(yes.Id, reasonRead.Text)];
        }

        if (reasonRead.Marked)
        {
            // Ink in the reason field that says nothing legible: approving it and denying it are both guesses.
            return [Ask(yes.Id, rule.Subject, KeepChip, LeaveOutChip)];
        }

        if (NotReadWhy(page, yes.Id) is { } yesWhy)
        {
            return [new InkResolution.NotRead(yes.Id, yesWhy)];
        }

        var yesRead = page.For(yes.Id)!;
        if (Doubtful(yesRead))
        {
            return [Ask(yes.Id, rule.Subject, KeepChip, LeaveOutChip)];
        }

        if (yesRead.Marked)
        {
            return [new InkResolution.Ticked(yes.Id)];
        }

        return [now >= rule.Deadline ? new InkResolution.ApprovedAtDeadline(yes.Id) : new InkResolution.Undecided(yes.Id)];
    }

    private IReadOnlyList<InkResolution> StillSore(InkPageRead page, StillSoreRule rule)
    {
        var box = Readout.Map.ById(rule.BoxId);
        if (NotReadWhy(page, box.Id) is { } why)
        {
            return [new InkResolution.NotRead(box.Id, why)];
        }

        var read = page.For(box.Id)!;
        if (Doubtful(read))
        {
            return [Ask(box.Id, rule.Subject, StillSoreChip, BetterChip)];
        }

        return [read.Marked ? new InkResolution.StillSore(box.Id) : new InkResolution.Better(box.Id)];
    }

    private IReadOnlyList<InkResolution> Staple(InkPageRead page, StapleRule rule)
    {
        var line = Readout.Map.ById(rule.LineId);
        if (NotReadWhy(page, line.Id) is { } why)
        {
            return [new InkResolution.NotRead(line.Id, why)];
        }

        var read = page.For(line.Id)!;
        if (Doubtful(read))
        {
            return [Ask(line.Id, rule.Subject, NeedChip, NoNeedChip)];
        }

        // A staple's line is written on, so ink there is a need even when the words cannot be made out (D18's reading).
        return [read.Marked || read.Text is not null
            ? new InkResolution.Needed(line.Id, read.Text)
            : new InkResolution.NoNeed(line.Id)];
    }

    private IReadOnlyList<InkResolution> Number(InkPageRead page, NumberRule rule)
    {
        var box = Readout.Map.ById(rule.BoxId);
        if (NotReadWhy(page, box.Id) is { } why)
        {
            return [new InkResolution.NotRead(box.Id, why)];
        }

        var read = page.For(box.Id)!;
        if (Doubtful(read))
        {
            return [Ask(box.Id, rule.Subject, read.Text, read.RunnerUpText, BlankChip, ElseChip)];
        }

        if (read.Text is not null)
        {
            return [new InkResolution.Written(box.Id, read.Text)];
        }

        if (read.Marked)
        {
            // Ink in a number box with no number in it: not a number, and not nothing either.
            return [Ask(box.Id, rule.Subject, read.RunnerUpText, BlankChip, ElseChip)];
        }

        return [new InkResolution.Skipped(box.Id)];
    }

    /// <summary>
    /// A circle-a-word group: its lanes are every lane in the map that shares the group. Nothing circled
    /// is unchanged, one lane is that word, and two lanes (or a read of any lane below the floor) is asked.
    /// </summary>
    private InkResolution RateGroup(InkPageRead page, string group, string subject)
    {
        var lanes = Readout.Map.Targets
            .Where(t => t.Kind == InkTargetKind.CircleLane && string.Equals(t.Group, group, StringComparison.Ordinal))
            .ToList();
        if (lanes.Count == 0)
        {
            throw new ArgumentException($"No circle lanes on this page belong to the group '{group}'.", nameof(group));
        }

        foreach (var lane in lanes)
        {
            if (NotReadWhy(page, lane.Id) is { } why)
            {
                return new InkResolution.NotRead(group, why);
            }
        }

        var seen = lanes.Select(lane => (Lane: lane, Read: page.For(lane.Id)!)).ToList();
        var circled = seen.Where(s => s.Read.Marked).ToList();
        var weakest = seen.OrderBy(s => s.Read.Confidence).First();

        if (circled.Count > 1 || Doubtful(weakest.Read))
        {
            // The chips are the words in play: those circled, the lane the read was least sure of, else "unchanged".
            return Ask(
                group, subject,
                [.. circled.Select(s => s.Lane.Meaning), weakest.Lane.Meaning, Unchanged]);
        }

        return circled.Count == 1
            ? new InkResolution.Circled(group, circled[0].Lane.Meaning)
            : new InkResolution.Unchanged(group);
    }

    private static string? NotReadWhy(InkPageRead page, string targetId)
    {
        if (!page.WasRead)
        {
            return page.WhyNotRead;
        }

        return page.For(targetId) is null ? $"The read did not report '{targetId}'." : null;
    }

    private static bool Doubtful(InkTargetRead read) => read.Confidence < MinimumConfidence;

    /// <summary>
    /// Two chips for a question. Each candidate is made safe for the cause key (which uses ':' and '|'
    /// itself), blank ones are dropped, repeats are dropped, and the first two left are the chips. Every
    /// caller ends its list with fixed words so two always remain.
    /// </summary>
    private static InkResolution.NeedsAsking Ask(string subjectId, string subject, params string?[] candidates)
    {
        var chips = candidates
            .Select(c => Safe(c))
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        if (chips.Count < 2)
        {
            throw new InvalidOperationException($"A question about '{subjectId}' was built with fewer than two distinct answers to offer.");
        }

        var about = Safe(subject);
        if (about.Length == 0)
        {
            about = "this";
        }

        return new InkResolution.NeedsAsking(
            subjectId, about, chips[0], chips[1], LowConfidenceCause.Key(about, chips[0], chips[1]));
    }

    private static string Safe(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split([':', '|', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
}
