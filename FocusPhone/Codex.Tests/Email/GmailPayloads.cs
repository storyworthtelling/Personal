using System.Text;
using System.Text.Json;

namespace Codex.Tests.Email;

/// <summary>Gmail's <c>format=full</c> replies, built from invented messages for the API tests.</summary>
internal static class GmailPayloads
{
    public static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static object[] MessageHeaders(string from, string to, string subject, string messageId) =>
    [
        new { name = "From", value = from },
        new { name = "To", value = to },
        new { name = "Subject", value = subject },
        new { name = "Message-Id", value = messageId },
        new { name = "Date", value = "Sat, 29 Aug 2026 10:00:00 +0000" },
    ];

    /// <summary>A one-part message.</summary>
    public static string Plain(string id, string from, string to, string subject, string body,
        string messageId = "<m1@example.com>", string contentType = "text/plain; charset=UTF-8") =>
        JsonSerializer.Serialize(new
        {
            id,
            historyId = "100",
            payload = new
            {
                mimeType = contentType.Split(';')[0],
                headers = MessageHeaders(from, to, subject, messageId).Append(new { name = "Content-Type", value = contentType }).ToArray(),
                body = new { size = body.Length, data = Base64Url(body) },
            },
        });

    /// <summary>Text plus one attachment Gmail did not send the bytes of, only its id and size.</summary>
    public static string WithAttachment(string id, string body, string fileName, long size) =>
        JsonSerializer.Serialize(new
        {
            id,
            historyId = "100",
            payload = new
            {
                mimeType = "multipart/mixed",
                headers = MessageHeaders("plumber@example.com", "owner@example.com", "Quote", "<a1@example.com>")
                    .Append(new { name = "Content-Type", value = "multipart/mixed; boundary=\"invented-boundary\"" }).ToArray(),
                body = new { size = 0 },
                parts = new object[]
                {
                    new
                    {
                        partId = "0",
                        mimeType = "text/plain",
                        filename = string.Empty,
                        headers = new object[]
                        {
                            new { name = "Content-Type", value = "text/plain; charset=UTF-8" },
                            new { name = "Content-Transfer-Encoding", value = "quoted-printable" },
                        },
                        body = new { size = body.Length, data = Base64Url(body) },
                    },
                    new
                    {
                        partId = "1",
                        mimeType = "application/pdf",
                        filename = fileName,
                        headers = new object[]
                        {
                            new { name = "Content-Type", value = $"application/pdf; name=\"{fileName}\"" },
                            new { name = "Content-Disposition", value = $"attachment; filename=\"{fileName}\"" },
                            new { name = "Content-Transfer-Encoding", value = "base64" },
                        },
                        body = new { attachmentId = "invented-attachment-id", size },
                    },
                },
            },
        });
}
