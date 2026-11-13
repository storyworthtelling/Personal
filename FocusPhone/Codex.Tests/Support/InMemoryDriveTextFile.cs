using Storyworthtelling.Voice;

namespace Codex.Tests.Support;

/// <summary>An in-memory <see cref="IDriveTextFile"/> for store tests — starts absent (null) until written.</summary>
public sealed class InMemoryDriveTextFile : IDriveTextFile, IDriveFileTrash, IDriveFileRevision
{
    private int writes;

    /// <summary>How many times the content was downloaded.</summary>
    public int Reads { get; private set; }

    /// <summary>Drive's revision: moves on every write, null while there is no file.</summary>
    public Task<string?> RevisionAsync(CancellationToken cancellationToken) =>
        FailReads ? throw new IOException("Drive could not be reached.") : Task.FromResult(Content is null ? null : "rev" + writes);

    public string? Content { get; set; }

    /// <summary>When set, every read throws: Drive unreachable.</summary>
    public bool FailReads { get; set; }

    public bool Trashed { get; private set; }

    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        if (FailReads)
        {
            throw new IOException("Drive could not be reached.");
        }
        Reads++;
        return Task.FromResult(Content);
    }

    public Task TrashAsync(CancellationToken cancellationToken)
    {
        Content = null;
        Trashed = true;
        return Task.CompletedTask;
    }

    public Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        Content = content;
        writes++;
        return Task.CompletedTask;
    }
}
