namespace Constitution.Domain.Meals;

/// <summary>Where one line of the sheet stands.</summary>
public enum MealItemOutcome
{
    /// <summary>Nothing is decided yet: the deadline has not passed and nobody has said no.</summary>
    Pending,

    /// <summary>The line is approved.</summary>
    Approved,

    /// <summary>Somebody said no. The line needs a revised sheet.</summary>
    Denied,

    /// <summary>The deadline has passed and no tablet's ink was read for the line, so there is no silence to approve.</summary>
    Unheard,
}

/// <summary>Why a line is approved.</summary>
public enum MealApprovalBasis
{
    /// <summary>Not approved.</summary>
    None,

    /// <summary>At least one adult ticked it and nobody denied it.</summary>
    Tick,

    /// <summary>Nobody ticked or denied it, and the deadline passed.</summary>
    Silence,
}

/// <summary>One adult's no, with their words if they wrote any.</summary>
public sealed record MealDenial(string Person, string? Reason);

/// <summary>
/// The verdict on one line. <see cref="Denials"/> are in person order. <see cref="Ignored"/> are the answers that were set aside
/// because they came after the deadline and the line was already decided without them: a notice's raw material, never a vote.
/// </summary>
public sealed record MealItemDecision(
    string ItemId,
    MealItemOutcome Outcome,
    MealApprovalBasis Basis,
    IReadOnlyList<MealDenial> Denials,
    IReadOnlyList<MealAnswer> Ignored);

/// <summary>
/// The verdict on the whole sheet, line by line: the typed result the planner would consume. <see cref="IsFinal"/> says the
/// deadline has passed, so silence has been read as approval.
/// </summary>
public sealed class MealDecision
{
    public MealDecision(DateOnly? week, DateTimeOffset deadline, bool isFinal, IReadOnlyList<MealItemDecision> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Week = week;
        Deadline = deadline;
        IsFinal = isFinal;
        Items = items;
    }

    /// <summary>The week the answers are for; null when there are no answers.</summary>
    public DateOnly? Week { get; }

    public DateTimeOffset Deadline { get; }

    public bool IsFinal { get; }

    public IReadOnlyList<MealItemDecision> Items { get; }

    /// <summary>The lines that are approved, in the decision's line order.</summary>
    public IReadOnlyList<string> ApprovedItemIds => Items.Where(item => item.Outcome == MealItemOutcome.Approved).Select(item => item.ItemId).ToList();

    public IReadOnlyList<MealItemDecision> Denied => Of(MealItemOutcome.Denied);

    public IReadOnlyList<MealItemDecision> Pending => Of(MealItemOutcome.Pending);

    public IReadOnlyList<MealItemDecision> Unheard => Of(MealItemOutcome.Unheard);

    /// <summary>Any no means the sheet is sent again, replaced on both tablets.</summary>
    public bool NeedsRevisedSheet => Denied.Count > 0;

    /// <summary>Every line is approved or denied: nothing waits on the clock or on a tablet.</summary>
    public bool IsSettled => Pending.Count == 0 && Unheard.Count == 0;

    public MealItemDecision? Item(string itemId) => Items.FirstOrDefault(item => string.Equals(item.ItemId, itemId, StringComparison.Ordinal));

    private List<MealItemDecision> Of(MealItemOutcome outcome) => Items.Where(item => item.Outcome == outcome).ToList();
}
