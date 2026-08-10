using System.Net.Http.Headers;

namespace FocusPhone.Audio.Google;

/// <summary>
/// A random-access read-only <see cref="Stream"/> over a Drive file, backed by HTTP
/// Range GETs against <c>files/{id}?alt=media</c>. Lets TagLibSharp seek and read
/// headers without downloading whole files. TagLibSharp reads synchronously, but the
/// transport is <c>AndroidMessageHandler</c>, which only implements <c>SendAsync</c>
/// (its synchronous <c>Send</c> throws <see cref="NotSupportedException"/>); so we call
/// the async send and block. That is safe here because the scanner drives these reads
/// on <c>Task.Run</c> pool threads with no captured synchronization context.
/// </summary>
public sealed class DriveSeekableStream : Stream
{
    private readonly string fileId;
    private readonly long length;
    private readonly DeviceAccountTokenProvider tokens;
    private readonly HttpClient http;
    private long position;

    public DriveSeekableStream(string fileId, long length, DeviceAccountTokenProvider tokens, HttpClient http)
    {
        this.fileId = fileId;
        this.length = length;
        this.tokens = tokens;
        this.http = http;
    }

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
        var token = tokens.GetAccessToken().GetAwaiter().GetResult();

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Range = new RangeHeaderValue(position, last);

        using var response = http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
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
