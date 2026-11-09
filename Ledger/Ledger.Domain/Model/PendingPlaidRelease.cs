namespace Index.Domain.Model;

/// <summary>
/// A bank disconnected here whose Item Plaid has not yet confirmed releasing (<c>/item/remove</c>). It keeps the
/// access token — the only credential that can release the Item — until Plaid answers, because a release that
/// failed and was forgotten left the Item live and billed at Plaid with nothing on the phone able to end it
/// (error-handling §9). The refresh worker asks again on each pass once <see cref="NextAttemptAtUtc"/> is due.
/// </summary>
public sealed class PendingPlaidRelease
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ItemId { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public string InstitutionName { get; set; } = string.Empty;

    public DateTimeOffset QueuedAtUtc { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAtUtc { get; set; }

    public string? LastError { get; set; }
}
