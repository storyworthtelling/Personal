namespace Index.Domain.Model;

/// <summary>
/// One week's recorded balance for one loan (D78) — the sheet's own "Loans Over Time" tab. Imported
/// once from the owner's encrypted history file in their Drive (<c>DriveSnowballHistorySource</c>, via
/// <c>SnowballHistoryMatch</c> and <c>SnowballService.ImportHistoryIfNeededAsync</c>) and then written
/// weekly from live balances thereafter (<c>SnowballService.RecordCurrentWeekSnapshotAsync</c>).
///
/// <para><b>Dedupe-at-write</b>: a row is only ever inserted for an <c>(AccountId, WeekOf)</c> pair that
/// does not already exist, so a re-run within the same week is a no-op, never a duplicate row — the
/// unique index <c>LedgerDbContext</c> puts on that pair is the database's own backstop for the same
/// rule, not the primary enforcement (the service checks first, rather than relying on catching a
/// constraint violation).</para>
/// </summary>
public sealed class SnowballSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly WeekOf { get; set; }
    public Guid AccountId { get; set; }
    public decimal Balance { get; set; }
}
