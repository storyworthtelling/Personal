using System.Text;
using System.Text.Json;
using MimeKit;

namespace Codex.Infrastructure.Email;

/// <summary>
/// Turns Gmail's <c>format=full</c> reply into the <see cref="MimeMessage"/> the mapper reads (efficiency audit §2).
/// A raw fetch carries every attachment's bytes; the full format carries the headers, the text bodies and, for each
/// attachment, only its id and size. The message is rebuilt as RFC822 with each body already decoded (written as
/// binary) and each attachment left empty with its size in <see cref="SizeHeader"/>, so the mapper describes the
/// attachment truthfully and its bytes are fetched only when it is opened.
/// </summary>
public static class GmailFullMessage
{
    /// <summary>On an attachment whose bytes were not sent: their decoded size, as Gmail reported it.</summary>
    public const string SizeHeader = "X-Post-Attachment-Size";

    public static MimeMessage ToMime(JsonElement message)
    {
        using var buffer = new MemoryStream();
        Write(buffer, message.GetProperty("payload"));
        buffer.Position = 0;
        return MimeMessage.Load(buffer);
    }

    private static void Write(Stream output, JsonElement part)
    {
        var headers = Headers(part);
        var mimeType = part.TryGetProperty("mimeType", out var type) ? type.GetString() ?? string.Empty : string.Empty;

        if (mimeType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) && part.TryGetProperty("parts", out var children))
        {
            var boundary = Boundary(headers);
            if (boundary is null)
            {
                boundary = "post-" + Guid.NewGuid().ToString("N");
                headers.RemoveAll(header => IsNamed(header, "Content-Type"));
                headers.Add(("Content-Type", $"{mimeType}; boundary=\"{boundary}\""));
            }

            WriteHeaders(output, headers.Where(header => !IsNamed(header, "Content-Transfer-Encoding")));
            WriteText(output, "\r\n");
            foreach (var child in children.EnumerateArray())
            {
                WriteText(output, $"--{boundary}\r\n");
                Write(output, child);
                WriteText(output, "\r\n");
            }
            WriteText(output, $"--{boundary}--\r\n");
            return;
        }

        var body = part.TryGetProperty("body", out var described) ? described : default;
        var data = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("data", out var encoded) ? encoded.GetString() : null;
        var kept = headers.Where(header => !IsNamed(header, "Content-Transfer-Encoding")).ToList();
        if (data is null && body.ValueKind == JsonValueKind.Object && body.TryGetProperty("size", out var size) && size.GetInt64() > 0)
        {
            kept.Add((SizeHeader, size.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        kept.Add(("Content-Transfer-Encoding", "binary"));

        WriteHeaders(output, kept);
        WriteText(output, "\r\n");
        if (data is not null)
        {
            output.Write(FromBase64Url(data));
        }
    }

    private static List<(string Name, string Value)> Headers(JsonElement part) =>
        part.TryGetProperty("headers", out var headers)
            ? headers.EnumerateArray()
                .Select(header => (header.GetProperty("name").GetString() ?? string.Empty, header.GetProperty("value").GetString() ?? string.Empty))
                .Where(header => header.Item1.Length > 0)
                .ToList()
            : [];

    private static string? Boundary(IEnumerable<(string Name, string Value)> headers)
    {
        var contentType = headers.FirstOrDefault(header => IsNamed(header, "Content-Type")).Value;
        return contentType is not null && ContentType.TryParse(contentType, out var parsed) && !string.IsNullOrEmpty(parsed.Boundary)
            ? parsed.Boundary
            : null;
    }

    private static bool IsNamed((string Name, string Value) header, string name) =>
        string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase);

    // Gmail hands header values back unfolded; a stray line break would end the header early, so it is folded away.
    private static void WriteHeaders(Stream output, IEnumerable<(string Name, string Value)> headers)
    {
        foreach (var (name, value) in headers)
        {
            WriteText(output, $"{name}: {value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)}\r\n");
        }
    }

    private static void WriteText(Stream output, string text) => output.Write(Encoding.UTF8.GetBytes(text));

    private static byte[] FromBase64Url(string encoded)
    {
        var padded = encoded.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
    }
}
