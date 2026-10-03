using Storyworthtelling.Voice;

namespace Index.Tests.Support;

// A single in-memory named file, standing in for a real Google Drive file over the narrow
// IDriveTextFile seam — the same fake shape Storyworthtelling.Voice.Tests already uses for its own
// Drive-backed stores.
public sealed class InMemoryDriveTextFile : IDriveTextFile
{
    public string? Content { get; private set; }

    public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Content);

    public Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        Content = content;
        return Task.CompletedTask;
    }
}
