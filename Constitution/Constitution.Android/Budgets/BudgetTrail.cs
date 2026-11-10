using Android.Content;
using Constitution.Services;

namespace Constitution.Budgets;

/// <summary>
/// The budget receiver's outcome trail: a capped file of short phrases in the app's private files folder, under its own name so a
/// line per broadcast cannot roll a failure history out. A phrase names the act and, for a refusal, the field or the exception's
/// type; it never carries a month or an amount.
/// </summary>
public sealed class BudgetTrail : ConstitutionTrail
{
    public const string FileName = "budget.log";

    /// <summary>The trail's cap; past it the older half is sealed into the log loop's outbox, not discarded.</summary>
    public const long MaxBytes = 32 * 1024;
    private const string Source = "Budget";

    public BudgetTrail(Context context)
        : base(context, FileName, MaxBytes, Source)
    {
    }
}
