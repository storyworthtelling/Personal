namespace Index.Domain.Model;

public enum SnowballHistoryStatus
{
    Loaded,
    Absent,
    NeedsPassphrase,
    WrongPassphrase,
    Unreadable,
}

/// <summary>What reading the owner's history file came to. <see cref="Reason"/> is for the log and never
/// carries the payload or the passphrase.</summary>
public sealed record SnowballHistoryReadResult(
    SnowballHistoryStatus Status, SnowballHistoryData? History = null, string? Reason = null)
{
    public static SnowballHistoryReadResult Loaded(SnowballHistoryData history) => new(SnowballHistoryStatus.Loaded, history);

    public static SnowballHistoryReadResult Absent { get; } = new(SnowballHistoryStatus.Absent);

    public static SnowballHistoryReadResult NeedsPassphrase { get; } = new(SnowballHistoryStatus.NeedsPassphrase);

    public static SnowballHistoryReadResult WrongPassphrase { get; } = new(SnowballHistoryStatus.WrongPassphrase);

    public static SnowballHistoryReadResult Unreadable(string reason) => new(SnowballHistoryStatus.Unreadable, null, reason);
}

/// <summary>Where the owner's recorded history comes from. It never throws: a failure is a status.</summary>
public interface ISnowballHistorySource
{
    Task<SnowballHistoryReadResult> ReadAsync(string? passphrase, CancellationToken cancellationToken);
}

/// <summary>Remembers, on the device, that the history has been brought in, so later page opens do not ask
/// Drive again.</summary>
public interface ISnowballHistoryImportMemory
{
    bool Imported { get; }

    void MarkImported();
}
