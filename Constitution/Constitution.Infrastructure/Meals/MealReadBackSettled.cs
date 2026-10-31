using System.Text.RegularExpressions;
using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Household;
using Constitution.Infrastructure.ReadBack;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Meals;

/// <summary>A meal sheet's or a recipe page's answers could not all be recorded. The message holds counts only, never a name or a word the household wrote.</summary>
public sealed class MealReadBackException(string message) : InvalidOperationException(message);

/// <summary>
/// What the household answered on paper, written into the household file (C-2.4.5, C-6.3.2). It is the read-back's hook for the meal documents: the
/// workouts' hook (<c>FileWorkoutLearning</c>) ignores them and this ignores workouts, and <see cref="SettledHooks"/> tells both.
///
/// <para><b>A meal sheet</b>: every dinner (and other meal line) the read settled becomes a <see cref="MealAnswer"/> under the adult this phone's tablet
/// belongs to, written with <see cref="MealAnswerRecords.Write"/> against the sheet's own deadline. The partner's install writes its adult's answers
/// too; the planning lease is for planning and has nothing to do with this. A line the read could not settle (<see cref="InkResolution.NeedsAsking"/>,
/// <see cref="InkResolution.NotRead"/>) writes nothing, and is never taken for silence. It is told both when the page is let go (<see cref="Settled"/>)
/// and when it is kept for its deadline (<see cref="Observed"/>), so a no written on Saturday is recorded with Saturday's reading, not with the Monday
/// reading on which the whole sheet can finally be settled and the same ink would be late.</para>
///
/// <para><b>A recipe page</b>: each eater's circled lane is a <see cref="TasteRating"/> for that person's own dish (<see cref="TasteInk.ReadRatings"/>);
/// "We didn't cook this tonight" ignores them all. Words written beside a no on a meal sheet live only on that answer; nothing else is stored.</para>
///
/// <para><b>Never over a file or a record that cannot be read.</b> A file that will not open refuses the whole write
/// (<see cref="HouseholdUnreadableException"/>); a single record this build cannot read is left exactly as it is, the others are written, and
/// <see cref="MealReadBackException"/> is raised so the read-back keeps the page and tries again. So is an eater who cannot be identified. Everything here
/// is idempotent, so the retry changes nothing that was already recorded.</para>
///
/// <para>The household service is blocked on, as the rest of the head does: the read-back's persist step is synchronous and runs on a worker thread,
/// and the phone's copy of the file is local.</para>
/// </summary>
public sealed class MealReadBackSettled(HouseholdFileService household) : IReadBackSettled, IReadBackObserved
{
    // The rating rule's words as RecipeRatingPage.Rules writes them: "<name>’s rating of <dish>".
    private static readonly Regex RatingSubject = new(
        @"^(?<name>.+?)’s rating of (?<dish>.+)$", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));

    // Only asked whether a resolution amounts to an answer at all, so it names nobody.
    private static readonly PersonId Probe = new("probe");

    public void Settled(ExpectedDocument document, DocumentResult result)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(result);
        switch (document.Kind)
        {
            case DeliveryKind.Meals:
                RecordAnswers(document, result);
                break;
            case DeliveryKind.Recipe:
                RecordRatings(document, result);
                break;
        }
    }

    public void Observed(ExpectedDocument document, DocumentResult result)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(result);
        if (document.Kind == DeliveryKind.Meals)
        {
            RecordAnswers(document, result);
        }
    }

    // ---- the meal sheet ----

    private void RecordAnswers(ExpectedDocument document, DocumentResult result)
    {
        var items = document.Pages.SelectMany(page => page.Rules).OfType<MealItemRule>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        // Only a sheet's own lines are answers: a staple's tick or a word on the grocery page is read under other rules.
        var lines = items.Select(rule => rule.YesBoxId).ToHashSet(StringComparer.Ordinal);
        var deadline = items.Min(rule => rule.Deadline);
        if (!result.All.Any(resolution => lines.Contains(resolution.SubjectId) && MealAnswerInk.From(resolution, Probe, document.Day, result.ReadAt) is not null))
        {
            return;
        }

        var unreadable = 0;
        household.EditAsync(
            (HouseholdDocument held, HybridLogicalClock clock, string writer) =>
            {
                var person = AdultOf(held, document.Person);
                var next = held;
                foreach (var answer in MealAnswerInk.FromResult(result, person, document.Day).Where(answer => lines.Contains(answer.ItemId)))
                {
                    var written = MealAnswerRecords.Write(next, clock, writer, answer, deadline, household.Now);
                    if (written.Outcome == MealAnswerOutcome.Unreadable)
                    {
                        unreadable++;
                    }

                    next = written.Document;
                }

                return next;
            },
            CancellationToken.None).GetAwaiter().GetResult();

        if (unreadable > 0)
        {
            throw new MealReadBackException(
                $"{unreadable} answer record(s) in the household file could not be read by this version, so they were left as they are and the page is kept.");
        }
    }

    // The adult whose tablet this is: named on the document the way delivery named them, found on the roster once and only once.
    private static PersonId AdultOf(HouseholdDocument held, string name)
    {
        var matches = RosterView.From(held).Adults
            .Where(entry => string.Equals(entry.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 1
            ? matches[0].Id
            : throw new MealReadBackException(
                $"The adult this tablet belongs to is on the household roster {matches.Count} times as an adult, so their answers were not recorded and the page is kept.");
    }

    // ---- the recipe page ----

    private void RecordRatings(ExpectedDocument document, DocumentResult result)
    {
        var rules = document.Pages.SelectMany(page => page.Rules).OfType<RatingRule>().ToList();
        var resolutions = result.All;
        if (rules.Count == 0 || RecipeRatingPage.DidntCook(resolutions))
        {
            return;
        }

        var circled = rules.Where(rule => resolutions.OfType<InkResolution.Circled>()
            .Any(item => item.SubjectId == rule.Group && TasteWords.FromPrinted(item.Word) is not null)).ToList();
        if (circled.Count == 0)
        {
            return;
        }

        var unreadable = 0;
        var unidentified = 0;
        household.EditAsync(
            (HouseholdDocument held, HybridLogicalClock clock, string writer) =>
            {
                var rows = new List<TasteRatingRow>();
                foreach (var rule in circled)
                {
                    if (RowFor(rule, document, held) is { } row)
                    {
                        rows.Add(row);
                    }
                    else
                    {
                        unidentified++;
                    }
                }

                var next = held;
                foreach (var rating in TasteInk.ReadRatings(resolutions, rows, document.Day, didntCook: false).Ratings)
                {
                    var key = TasteMaps.Rating.KeyOf(rating);
                    if (next.Live(key) is { } kept)
                    {
                        TasteRating current;
                        try
                        {
                            current = TasteMaps.Rating.Read(new RecordReader(kept));
                        }
                        catch (HouseholdRecordException)
                        {
                            // Perhaps a newer build's record: left exactly as it is, and reported once the rest are written.
                            unreadable++;
                            continue;
                        }

                        if (current.Verdict == rating.Verdict && string.Equals(current.Reason, rating.Reason, StringComparison.Ordinal))
                        {
                            continue;
                        }
                    }

                    var stamp = clock.Witness(next.MaxStamp, household.Now.ToUnixTimeMilliseconds());
                    next = HouseholdRecords.Put(next, TasteMaps.Rating, rating, stamp, writer);
                }

                return next;
            },
            CancellationToken.None).GetAwaiter().GetResult();

        if (unreadable > 0 || unidentified > 0)
        {
            throw new MealReadBackException(
                $"{unidentified} rating row(s) could not be matched to one person and one dish, and {unreadable} rating record(s) could not be read by this version; "
                + "the rest were recorded and the page is kept.");
        }
    }

    // The carried row when the document has one for this group (and then ONLY that: a carried id that has gone from the file is reported,
    // never replaced by whoever now has the same name); the lookup by words only when the document carries no row for the group.
    private static TasteRatingRow? RowFor(RatingRule rule, ExpectedDocument document, HouseholdDocument held) =>
        document.RatingRows.Any(row => string.Equals(row.Group, rule.Group, StringComparison.Ordinal))
            ? CarriedRowOf(rule, document, held)
            : RowOf(rule, held);

    // A document stored since the rating rows were carried names the person and the card by id, exactly as the page was built: no lookup, so a
    // renamed person, a kids' dish whose card title differs from the printed dish, or two cards of one title cannot make the row ambiguous.
    // The ids must still be on the roster and in the box: a rating is never written for someone who has gone or a card that has been removed.
    private static TasteRatingRow? CarriedRowOf(RatingRule rule, ExpectedDocument document, HouseholdDocument held)
    {
        var carried = document.RatingRows.First(row => string.Equals(row.Group, rule.Group, StringComparison.Ordinal));
        var onRoster = RosterView.From(held).Entries.Any(entry => entry.Id == carried.Person);
        var inTheBox = HouseholdRecords.Read(held, RecipeMaps.Recipe).Items.Any(card => card.Id == carried.Recipe);
        return onRoster && inTheBox ? carried : null;
    }

    // The fallback, for a document stored before the rows were carried: a row's group and its printed words are all it kept, so the person
    // and the card are found again by name and by title, and a row is only ever matched when both are unique.
    private static TasteRatingRow? RowOf(RatingRule rule, HouseholdDocument held)
    {
        var match = RatingSubject.Match(rule.Subject);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim();
        var dish = match.Groups["dish"].Value.Trim();
        var people = RosterView.From(held).Entries
            .Where(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var cards = HouseholdRecords.Read(held, RecipeMaps.Recipe).Items
            .Where(card => string.Equals(card.Title.Trim(), dish, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return people.Count == 1 && cards.Count == 1 ? new TasteRatingRow(rule.Group, people[0].Id, cards[0].Id) : null;
    }
}
