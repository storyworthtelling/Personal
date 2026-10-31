using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Storyworthtelling.Diagnostics;

/// <summary>A batch that could not be read back: not base64, not gzip, or not a batch.</summary>
public sealed class LogBatchFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// JSON, gzip-compressed, base64'd (plan §3.2) — the string VaultEnvelope then seals. The hash is of the
/// JSON, so a read-back compares content: the ciphertext is fresh on every seal and gzip need not be
/// byte-stable, but the JSON of one batch always is.
/// </summary>
public static class LogBatchCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Json(LogBatch batch) => JsonSerializer.Serialize(Dto.From(batch), Options);

    public static string Encode(LogBatch batch)
    {
        var json = Encoding.UTF8.GetBytes(Json(batch));
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(json);
        }
        return Convert.ToBase64String(buffer.ToArray());
    }

    public static LogBatch Decode(string encoded)
    {
        byte[] compressed;
        try
        {
            compressed = Convert.FromBase64String(encoded);
        }
        catch (FormatException e)
        {
            throw new LogBatchFormatException("The batch is not base64.", e);
        }

        string json;
        try
        {
            using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            json = reader.ReadToEnd();
        }
        catch (InvalidDataException e)
        {
            throw new LogBatchFormatException("The batch is not gzip.", e);
        }

        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(json, Options)
                ?? throw new LogBatchFormatException("The batch is empty JSON.");
            return dto.ToBatch();
        }
        catch (JsonException e)
        {
            throw new LogBatchFormatException("The batch is not a log batch.", e);
        }
    }

    /// <summary>Lower-case hex SHA-256 of the batch's JSON.</summary>
    public static string Sha256(LogBatch batch) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json(batch)))).ToLowerInvariant();

    // The wire shape, kept apart from the records so a renamed property cannot silently rename a field
    // already sitting in Drive.
    private sealed record Dto(HeaderDto? Header, List<PartDto>? Parts)
    {
        public static Dto From(LogBatch batch) => new(
            new HeaderDto(
                batch.Header.Schema, batch.Header.App, batch.Header.InstallId, batch.Header.Phone,
                batch.Header.AppVersion, batch.Header.VersionCode, batch.Header.LogKeyEpoch,
                batch.Header.FirstUtc, batch.Header.LastUtc, batch.Header.Segments.ToList()),
            batch.Parts.Select(p => new PartDto(p.Sequence, p.Log, p.Text)).ToList());

        public LogBatch ToBatch()
        {
            if (Header is null || Parts is null || Header.App is null || Header.InstallId is null)
            {
                throw new LogBatchFormatException("The batch has no header or no parts.");
            }
            return new LogBatch(
                new LogBatchHeader(
                    Header.Schema, Header.App, Header.InstallId, Header.Phone ?? string.Empty,
                    Header.AppVersion ?? string.Empty, Header.VersionCode, Header.LogKeyEpoch,
                    Header.FirstUtc, Header.LastUtc, Header.Segments ?? []),
                Parts.Select(p => new LogBatchPart(p.Sequence, p.Log ?? string.Empty, p.Text ?? string.Empty)).ToList());
        }
    }

    private sealed record HeaderDto(
        int Schema, string? App, string? InstallId, string? Phone, string? AppVersion, long VersionCode,
        int LogKeyEpoch, DateTimeOffset FirstUtc, DateTimeOffset LastUtc, List<long>? Segments);

    private sealed record PartDto(long Sequence, string? Log, string? Text);
}
