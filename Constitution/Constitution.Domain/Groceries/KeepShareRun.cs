using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Groceries;

/// <summary>How the phone answered the intent aimed at Keep.</summary>
public enum KeepStart
{
    Started,
    NotFound,
    Refused,
}

/// <summary>What sharing the list to Keep came to, in the words the Meals tab shows inline.</summary>
public enum KeepShareOutcome
{
    Sent,
    NothingToSend,
    KeepNotInstalled,
    KeepRefused,
    ListNotBuilt,
}

/// <summary>An outcome and its sentence; a failure that stopped the list being built is kept with its stack.</summary>
public sealed record KeepShareReport(KeepShareOutcome Outcome, string Sentence)
{
    public Exception? Failure { get; init; }
}

/// <summary>The list as far as it can be got before anything is sent: ready to send, or already settled.</summary>
public abstract record KeepSharePrepared
{
    private KeepSharePrepared()
    {
    }

    public sealed record Ready(GroceryShareDecision Decision, string? Caveat) : KeepSharePrepared;

    public sealed record Settled(KeepShareReport Report) : KeepSharePrepared;
}

/// <summary>
/// Sharing the grocery list to Google Keep from the Meals tab (D112), in two steps so the reads stay off the UI thread and the one intent is started on it.
/// <see cref="Prepare"/> reads the pantry, the roster and the decision record and builds the list; <see cref="Finish"/> turns how the phone answered the intent into the sentence.
///
/// <para><b>Failing closed where it matters, open where it does not.</b> A pantry or roster that is there and cannot be read stops the share with its reason: a list built
/// on a guess would tell the shopper to buy what is on the shelf, or the wrong amount. The decision record is a convenience, so one that cannot be read shares the list without
/// the denied evenings' lines and says so (<see cref="KeepSharePrepared.Ready.Caveat"/>). The sentences quote nothing the household wrote.</para>
/// </summary>
public sealed class KeepShareRun(IMealPantrySource pantry, IMealRosterSource roster, IMealDecisionRecords decisions)
{
    public const string ActionLabel = "Share the list";
    public const string SentSentence = "The list went to Keep.";
    public const string RefusedSentence = "Keep would not take the list, so it was not sent.";
    public const string RecordSentence = "The record of dinners nobody wanted could not be read, so those lines are not on it.";
    private const string NotSent = " The list was not sent.";

    public KeepSharePrepared Prepare(MealWeek week, IReadOnlyList<Recipe> cards)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(cards);

        int people;
        switch (roster.Read())
        {
            case MealRosterRead.Ready table:
                people = table.People.Count;
                break;
            case MealRosterRead.Unavailable rosterReason:
                return Settled(KeepShareOutcome.ListNotBuilt, rosterReason.Reason + NotSent);
            default:
                throw new InvalidOperationException("The roster came back as neither people nor a reason.");
        }

        if (people < 1)
        {
            return Settled(KeepShareOutcome.ListNotBuilt, "The household lists nobody at the table, so there is no amount to shop for." + NotSent);
        }

        Pantry held;
        switch (pantry.Read())
        {
            case MealPantryRead.Ready ready:
                held = ready.Pantry;
                break;
            case MealPantryRead.NoneYet:
                held = Pantry.Empty;
                break;
            case MealPantryRead.Unreadable damaged:
                return Settled(KeepShareOutcome.ListNotBuilt, damaged.Reason + NotSent);
            default:
                throw new InvalidOperationException("The pantry came back as neither a pantry nor a reason.");
        }

        var extra = MealExtraLines.Read(decisions, week.WeekStart);
        GroceryList list;
        try
        {
            list = GroceryList.From(week, cards, held, people, extra.Lines);
        }
        catch (ArgumentException failure)
        {
            return new KeepSharePrepared.Settled(new KeepShareReport(
                KeepShareOutcome.ListNotBuilt, "A dinner this week names a recipe card that is no longer in the box." + NotSent) { Failure = failure });
        }

        var decision = GroceryShare.Plan(list, keepIsInstalled: true);
        return decision.Outcome == GroceryShareOutcome.Send
            ? new KeepSharePrepared.Ready(decision, extra.UnreadableBecause is null ? null : RecordSentence)
            : Settled(KeepShareOutcome.NothingToSend, decision.Notice);
    }

    public static KeepShareReport Finish(KeepSharePrepared.Ready ready, KeepStart start)
    {
        ArgumentNullException.ThrowIfNull(ready);
        switch (start)
        {
            case KeepStart.Started:
                return new KeepShareReport(KeepShareOutcome.Sent, ready.Caveat is null ? SentSentence : SentSentence + " " + ready.Caveat);
            case KeepStart.NotFound:
                return new KeepShareReport(KeepShareOutcome.KeepNotInstalled, GroceryShare.KeepNotInstalledSentence);
            case KeepStart.Refused:
                return new KeepShareReport(KeepShareOutcome.KeepRefused, RefusedSentence);
            default:
                throw new ArgumentOutOfRangeException(nameof(start), start, "That is not a way Keep can answer.");
        }
    }

    private static KeepSharePrepared Settled(KeepShareOutcome outcome, string sentence) =>
        new KeepSharePrepared.Settled(new KeepShareReport(outcome, sentence));
}
