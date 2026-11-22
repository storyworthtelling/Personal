using System.Net.Http.Headers;

namespace Codex.Infrastructure.Music;

/// <summary>
/// A random-access read-only <see cref="Stream"/> over a Drive file, backed by HTTP
/// Range GETs against <c>files/{id}?alt=media</c>. Lets TagLibSharp seek and read
/// headers without downloading whole files. TagLibSharp reads synchronously, but the
/// head's transport is <c>AndroidMessageHandler</c>, which only implements <c>SendAsync</c>
/// (its synchronous <c>Send</c> throws <see cref="NotSupportedException"/>); so we call
/// the async send and block. That is safe here because the scanner drives these reads
/// on <c>Task.Run</c> pool threads with no captured synchronization context.
///
/// A refused read throws <see cref="DriveReadException"/> with the status and keeps it as
/// <see cref="LastFailure"/>: TagLib rethrows a reader's exception wrapped in its own, so the
/// scanner reads the status from here rather than from what reaches it.
/// </summary>
public sealed class DriveSeekableStream : Stream
{
    private readonly string fileId;
    private readonly long length;
    private readonly Func<Task<string>> getToken;
    private readonly Action<string> forgetToken;
    private readonly HttpClient http;
    private long position;

    /// <param name="getToken">Hands out the current Drive access token.</param>
    /// <param name="forgetToken">Forgets a token Drive rejected, wherever it was issued.</param>
    public DriveSeekableStream(
        string fileId, long length, Func<Task<string>> getToken, Action<string> forgetToken, HttpClient http)
    {
        this.fileId = fileId;
        this.length = length;
        this.getToken = getToken;
        this.forgetToken = forgetToken;
        this.http = http;
    }

    /// <summary>The last refusal this stream threw, so a caller holding TagLib's wrapper can still read its status.</summary>
    public DriveReadException? LastFailure { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;

    public override long Position
    {
        get => position;
        set => position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count <= 0 || position >= length)
        {
            return 0;
        }

        var last = Math.Min(position + count, length) - 1;
        var token = getToken().GetAwaiter().GetResult();

        using var response = Send(token, last);
        if (!response.IsSuccessStatusCode)
        {
            LastFailure = new DriveReadException((int)response.StatusCode);
            throw LastFailure;
        }

        using var content = response.Content.ReadAsStream();

        var total = 0;
        int read;
        while (total < count && (read = content.Read(buffer, offset + total, count - total)) > 0)
        {
            total += read;
        }

        position += total;
        return total;
    }

    private HttpResponseMessage Send(string token, long last)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Range = new RangeHeaderValue(position, last);

        return http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter().GetResult();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => length + offset,
            _ => position,
        };
        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Drive refused a range read; <see cref="Status"/> is its HTTP status. Names no file.</summary>
public sealed class DriveReadException(int status)
    : HttpRequestException($"Drive refused the read with HTTP {status}.", null, (System.Net.HttpStatusCode)status)
{
    public int Status { get; } = status;
}
