using Storyworthtelling.Remarkable;

namespace Index.Tests.Support;

/// <summary>A fake <see cref="IRemarkableClient"/>, matching the shape Foreword's own
/// <c>PrayerDeliveryServiceTests.FakeRemarkable</c> uses, extended to record the parent folder id and
/// the uploaded bytes so a delivery test can assert D60's replace-by-title behaviour.</summary>
public sealed class FakeRemarkableClient : IRemarkableClient
{
    public sealed record UploadCall(string Title, byte[] PdfBytes, string? ParentFolderId);

    public List<UploadCall> Uploads { get; } = [];
    public List<string> Trashed { get; } = [];
    public RemarkableDoc? Existing { get; set; }
    public bool ThrowOnUpload { get; set; }
    public bool ThrowOnDownload { get; set; }

    public Task Upload(string title, byte[] pdfBytes) => Upload(title, pdfBytes, null);

    public Task Upload(string title, byte[] pdfBytes, string? parentFolderId)
    {
        if (ThrowOnUpload)
        {
            throw new InvalidOperationException("upload failed");
        }
        Uploads.Add(new UploadCall(title, pdfBytes, parentFolderId));
        return Task.CompletedTask;
    }

    public Task<string> EnsureFolder(string name) => Task.FromResult($"folder:{name}");

    public Task<IReadOnlyList<RemarkableDoc>> ListRecent() =>
        Task.FromResult<IReadOnlyList<RemarkableDoc>>(Existing is null ? [] : [Existing]);

    public Task<RemarkableDoc?> DownloadByTitle(string title)
    {
        if (ThrowOnDownload)
        {
            throw new InvalidOperationException("download failed");
        }
        return Task.FromResult(Existing is { } doc && doc.Title == title ? doc : null);
    }

    public Task MoveToTrash(string docId)
    {
        Trashed.Add(docId);
        return Task.CompletedTask;
    }
}
