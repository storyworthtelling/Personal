namespace Index.Domain.Model;

/// <summary>
/// D81: one MONTH's recorded balance for one savings account — the Savings tab's own cadence,
/// deliberately slower than Snowball's weekly <c>SnowballSnapshot</c> (D81's own reasoning: "savings
/// moves slower, and a monthly chart stays calmer"). <see cref="MonthOf"/> is always the first day of
/// its calendar month, so a row's identity is the (account, calendar-month) pair rather than an exact
/// date within it.
///
/// <para><b>Dedupe-at-write</b>, the same shape <c>SnowballSnapshot</c> already established: a row is
/// only ever inserted for an <c>(AccountId, MonthOf)</c> pair that does not already exist
/// (<c>SavingsService.RecordCurrentMonthSnapshotAsync</c>), so opening the Savings tab more than once
/// in the same month is a no-op, never a duplicate row. <c>LedgerDbContext</c> puts a unique index on
/// that pair as the database's own backstop, not the primary enforcement. Deliberately its own writer,
/// not a reuse of <c>SnowballSnapshot</c>'s weekly one — two different cadences for two different
/// tabs, by design (D81), not two copies of the same idea.</para>
/// </summary>
public sealed class SavingsSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly MonthOf { get; set; }
    public Guid AccountId { get; set; }
    public decimal Balance { get; set; }
}
