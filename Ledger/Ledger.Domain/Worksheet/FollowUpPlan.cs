namespace Index.Domain.Worksheet;

/// <summary>
/// D153/D156: everything the follow-up page needs, gathered by <c>FollowUpPlanBuilder</c> (Infrastructure)
/// once a read-back has proven the worksheet doesn't balance, then handed unchanged to
/// <c>FollowUpPainter</c> (which draws it) and <c>FollowUpReadBackInstruction</c> (which describes it
/// back to the vision model) — the same one-plan-one-source shape <see cref="WorksheetPlan"/> already
/// establishes for the worksheet itself.
/// </summary>
public sealed record FollowUpPlan(
    string TargetYearMonth,
    DateOnly PrintedOn,
    bool IsOver,
    long DifferenceWholeDollars,
    long TargetWholeDollars,
    long WrittenTotalWholeDollars,
    IReadOnlyList<FollowUpSuggestion> ActionableLines,
    IReadOnlyList<FollowUpSuggestion> ReferenceLines,
    bool HasTarget = true)
{
    /// <summary>The boxed lines that are there only because their limit could not be read (D49, R-G25).</summary>
    public IReadOnlyList<FollowUpSuggestion> ResendLines => ActionableLines.Where(l => l.WasUnreadable).ToList();

    /// <summary>Whether any limit is being asked for again on this page.</summary>
    public bool HasResend => ActionableLines.Any(l => l.WasUnreadable);

    /// <summary>A page that exists only to ask for unread limits again: the month either balances or has
    /// no target to balance against (<see cref="HasTarget"/> false — income was not confident when the
    /// sheet was printed), and every boxed line is a resend. It has no over/under sentence, no
    /// "With the suggestions" line, and says so in its title.</summary>
    public bool IsResendOnly =>
        HasResend && ActionableLines.All(l => l.WasUnreadable) && (!HasTarget || DifferenceWholeDollars == 0);

    /// <summary>Nothing left to send: the month balances (or has nothing to balance against) and no
    /// limit is waiting to be read again.</summary>
    public bool IsSettled => !HasResend && (!HasTarget || DifferenceWholeDollars == 0);

    /// <summary>Every actionable line's key, in printed order — the same list
    /// <c>FollowUpRegions.ActionableBoxes</c> and the read-back instruction both build their boxes from
    /// (the follow-up's own Guard-G18-style rule: one shared source, never two hand-typed copies).</summary>
    public IReadOnlyList<string> ActionableKeysInPrintedOrder => ActionableLines.Select(l => l.Key).ToList();

    /// <summary>Whether this page suggests the remainder for the extra debt payment (D156). It does
    /// whenever the extra payment line is among the actionable rows; a sheet with no Snowball loan has no
    /// such line, and the page must not promise a suggestion it does not make.</summary>
    public bool RemainderGoesToExtraPayment =>
        ActionableLines.Any(l => l.Key == WorksheetRegions.SetAsideExtraPayment && l.IsSuggested);

    /// <summary>D153's own footer sentence checks itself: had every suggestion been followed exactly
    /// (an untouched line keeps what was written), would the total have matched the target? True for
    /// every worked example in mockup 13(b)/(c); false only in the degenerate case where the over side's
    /// available trims can't fully cover the excess (nothing left to trim further) — in which case
    /// another follow-up is still due tomorrow regardless, per D153's "until it balances."</summary>
    public bool WithSuggestionsBalances
    {
        get
        {
            var followed = ActionableLines.Concat(ReferenceLines)
                .Select(l => (decimal)(l.SuggestedWholeDollars ?? l.WrittenWholeDollars));
            return ZeroBalanceCheck.IsBalanced(followed, TargetWholeDollars);
        }
    }
}
