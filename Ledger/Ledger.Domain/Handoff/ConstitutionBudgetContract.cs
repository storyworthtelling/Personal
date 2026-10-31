namespace Index.Domain.Handoff;

/// <summary>
/// The budget contract's wire, Index as SENDER and Constitution as receiver. Constitution declares the
/// signature-level permission and owns the receiver; Index only uses the permission. The literals are
/// held equal to Constitution's own by <c>BudgetContractTests</c>, which reads its source by content.
/// </summary>
public static class ConstitutionBudgetContract
{
    public const string Package = "com.storyworthtelling.constitution";
    public const string Receiver = "com.storyworthtelling.constitution.BudgetReceiver";
    public const string Action = "com.storyworthtelling.constitution.action.BUDGET";
    public const string Permission = "com.storyworthtelling.constitution.RECEIVE_BUDGET";

    public const string MonthExtra = "month";
    public const string BudgetExtra = "budget";
    public const string SpentExtra = "spent";
}
