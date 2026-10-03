namespace Index.Domain.Model;

/// <summary>
/// One loan account's place in the Snowball's focus order (D04, D43;
/// INDEX-P11-TECHNICAL-FINDING.md §3, the "Resolved" EF schema list). One row per loan account;
/// <see cref="FocusOrder"/> is dense <c>0..N-1</c>, renumbered on every reorder exactly the way
/// <see cref="InstitutionOrder"/> already renumbers Accounts' bank order — <c>SnowballService</c> reuses
/// that class's own list-move arithmetic directly for loans, rather than a second, near-identical
/// <c>SnowballOrder</c> duplicating logic that has nothing loan-specific to it (see
/// <c>InstitutionOrder</c>'s own remarks: it already operates purely on a sequence of <c>Guid</c>s and
/// takes no dependency on <see cref="Institution"/>).
///
/// <para><see cref="ExtraPaymentAmount"/> is the mockup's single "Extra each week" field (D05/D73) — ONE
/// number for the whole plan, added to whichever loan is currently the focus, never a per-loan value
/// (confirmed against mockup 6's own copy: "Added to the focus loan's payment"). It still lives as a
/// column on this per-loan table, per the finding's own schema shape, rather than a second table with
/// one row: every row of a plan is kept in sync to carry the same value
/// (<c>SnowballService.SetExtraPaymentAmountAsync</c> writes it to every row atomically), so there is no
/// privileged "the extra payment lives on the focus loan's row" case to special-case as the focus loan
/// changes when a loan pays off and the next one becomes the focus.</para>
/// </summary>
public sealed class SnowballPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LoanAccountId { get; set; }
    public int FocusOrder { get; set; }
    public decimal ExtraPaymentAmount { get; set; }
}
