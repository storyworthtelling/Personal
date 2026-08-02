using Storyworthtelling.Voice;

namespace FocusPhone.Tests.Support;

/// <summary>An in-memory <see cref="IDriveTextFile"/> for store tests — starts absent (null) until written.</summary>
public sealed class InMemoryDriveTextFile : IDriveTextFile
{
    public string? Content { get; set; }

    public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Content);

    public Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        Content = content;
        return Task.CompletedTask;
    }
}
