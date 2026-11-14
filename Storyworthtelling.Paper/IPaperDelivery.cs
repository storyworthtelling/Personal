using Storyworthtelling.Remarkable;

namespace Storyworthtelling.Paper;

/// <summary>
/// The unified delivery seam for a finished, already-rendered PDF: the reMarkable path (root upload — the
/// proven Foreword path — or a named folder) and the Drive-beside-source archive behind one interface, so every
/// app delivers the same way. It is the only door to the tablet for an upload (audit 01 §11,
/// <c>OneRemarkableDoorTests</c>); an app may still read, list and trash through <see cref="IRemarkableClient"/>.
/// The colour profile of the bytes is the caller's to choose before rendering; this seam is a byte-pipe. Either transport may be absent on a given head (no reMarkable
/// pairing, or the Drive write scope not yet granted), reported by the capability flags so the caller can skip
/// a target rather than fail.
/// </summary>
public interface IPaperDelivery
{
    bool CanDeliverToRemarkable { get; }
    bool CanArchiveToDrive { get; }

    /// <summary>
    /// Delivers the PDF to the root of the paired reMarkable tablet. Throws
    /// <see cref="RemarkableRefusedException"/> for a file the tablet will never take (over
    /// <see cref="RemarkableLimits.MaxBytes"/>, or the phone ran out of memory sending it), which a caller settles
    /// rather than retries; any other failure propagates as it was.
    /// </summary>
    Task DeliverToRemarkableRoot(string title, byte[] pdf, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same delivery from a stream of known length — a file on disk — so a long report is sent from where it lies
    /// rather than held whole in memory (efficiency audit §18).
    /// </summary>
    Task DeliverToRemarkableRoot(string title, Stream pdf, long length, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers the PDF into the named folder at the tablet's root, creating the folder when absent. The folder
    /// wire format is reconstructed, not yet seen working on a live account; the root path is the proven one.
    /// </summary>
    Task DeliverToRemarkableFolder(string folderName, string title, byte[] pdf, CancellationToken cancellationToken = default);

    // The Drive archive, three resumable operations beside the source document (see IPaperDriveArchive).
    Task<string?> FindDriveParentFolder(string sourceFileId, CancellationToken cancellationToken = default);
    Task<string> EnsureDriveFolder(string parentFolderId, string name, CancellationToken cancellationToken = default);
    Task<string> UploadDriveFile(
        string folderId, string name, string mimeType, byte[] content, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default composition of the two transports. A head constructs it with whichever transports it has
/// wired; a null transport disables that target (its capability flag is false and its methods throw if
/// called, which the capability-gated callers never do).
/// </summary>
public sealed class PaperDelivery : IPaperDelivery
{
    private readonly IRemarkableClient? remarkable;
    private readonly IPaperDriveArchive? drive;

    public PaperDelivery(IRemarkableClient? remarkable = null, IPaperDriveArchive? drive = null)
    {
        this.remarkable = remarkable;
        this.drive = drive;
    }

    public bool CanDeliverToRemarkable => remarkable is not null;
    public bool CanArchiveToDrive => drive is not null;

    public Task DeliverToRemarkableRoot(string title, byte[] pdf, CancellationToken cancellationToken = default) =>
        Require(remarkable).Upload(title, pdf, null, cancellationToken);

    public Task DeliverToRemarkableRoot(string title, Stream pdf, long length, CancellationToken cancellationToken = default) =>
        Require(remarkable).Upload(title, pdf, length, null, cancellationToken);

    public async Task DeliverToRemarkableFolder(
        string folderName, string title, byte[] pdf, CancellationToken cancellationToken = default)
    {
        var tablet = Require(remarkable);
        var folderId = await tablet.EnsureFolder(folderName, cancellationToken);
        await tablet.Upload(title, pdf, folderId, cancellationToken);
    }

    public Task<string?> FindDriveParentFolder(string sourceFileId, CancellationToken cancellationToken = default) =>
        Require(drive).FindParentFolder(sourceFileId, cancellationToken);

    public Task<string> EnsureDriveFolder(string parentFolderId, string name, CancellationToken cancellationToken = default) =>
        Require(drive).EnsureFolder(parentFolderId, name, cancellationToken);

    public Task<string> UploadDriveFile(
        string folderId, string name, string mimeType, byte[] content, CancellationToken cancellationToken = default) =>
        Require(drive).UploadFile(folderId, name, mimeType, content, cancellationToken);

    private static T Require<T>(T? transport) where T : class =>
        transport ?? throw new InvalidOperationException(
            $"{typeof(T).Name} is not wired on this head; guard with the capability flag before calling.");
}
