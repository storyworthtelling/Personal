namespace Storyworthtelling.Remarkable;

/// <summary>
/// The limits every app's delivery shares (audit 01 §11). They lived in Folio alone: the others would read a
/// 173 MB report into memory (the Android HTTP stack keeps a second copy), run out, and send it again.
/// </summary>
public static class RemarkableLimits
{
    /// <summary>reMarkable's documented upper bound for one document.</summary>
    public const long MaxBytes = 100_000_000;

    /// <summary>The client timeout for every head: a large document over a phone's radio outlasts the 100-second default.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>The refusal for a file of this size, or null when the tablet takes it. Call it before reading the file.</summary>
    public static RemarkableRefusedException? TooLarge(long bytes) =>
        bytes > MaxBytes ? new RemarkableRefusedException(RemarkableRefusal.TooLarge, bytes) : null;
}

/// <summary>Why a document will not reach the tablet however often it is sent.</summary>
public enum RemarkableRefusal
{
    TooLarge,
    OutOfMemory,
}

/// <summary>
/// A delivery that is final, not a failure to retry: the same file is the same size, and in the same heap it runs
/// out of memory again, filling the heap further each time first. Callers settle without the tablet and keep
/// <see cref="Sentence"/> for the owner.
/// </summary>
public sealed class RemarkableRefusedException(RemarkableRefusal reason, long bytes, Exception? inner = null)
    : Exception(Describe(reason, bytes), inner)
{
    public RemarkableRefusal Reason { get; } = reason;
    public long Bytes { get; } = bytes;

    /// <summary>The reason in plain words, without a leading capital or a full stop, so a caller can frame it.</summary>
    public string Sentence => Describe(Reason, Bytes);

    private static string Describe(RemarkableRefusal reason, long bytes) => reason switch
    {
        RemarkableRefusal.TooLarge =>
            $"the PDF is {bytes / 1_000_000.0:0} MB, over the tablet's {RemarkableLimits.MaxBytes / 1_000_000.0:0} MB limit",
        _ => $"the phone ran out of memory sending it ({bytes / 1_000_000.0:0} MB)",
    };
}
