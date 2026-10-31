using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Storyworthtelling.Diagnostics.Tests;

// One Drive file's plaintext (plan §3.2): a header and the sealed segments, as JSON, gzip-compressed and
// base64'd before VaultEnvelope seals it. The SHA-256 is of the JSON, so a read-back compares content
// rather than ciphertext (which is fresh on every seal).
public class LogBatchCodecTests
{
    private static readonly LogInstall Install = new("post", "inst-0a1b2c", LogPhone.Owner, "1.0.42", 200042);

    private static LogBatch Sample() => new(
        new LogBatchHeader(
            LogBatch.CurrentSchema,
            Install.App,
            Install.InstallId,
            Install.Phone,
            Install.AppVersion,
            Install.VersionCode,
            LogKey.Epoch,
            new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 31, 17, 30, 0, TimeSpan.Zero),
            [7, 8]),
        [
            new LogBatchPart(7, "diagnostic.log", "[Oct 24, 9:00 AM] WARN Sync — failed\n\n"),
            new LogBatchPart(8, "texts.log", "[Oct 31, 5:30 PM] INFO Sms — arrived ·· 01\n\n"),
        ]);

    [Fact]
    public void EncodeThenDecodeRoundTripsEveryField()
    {
        var batch = Sample();

        var decoded = LogBatchCodec.Decode(LogBatchCodec.Encode(batch));

        Assert.Equal(LogBatchCodec.Json(batch), LogBatchCodec.Json(decoded));
        Assert.Equal("post", decoded.Header.App);
        Assert.Equal(LogPhone.Owner, decoded.Header.Phone);
        Assert.Equal(200042, decoded.Header.VersionCode);
        Assert.Equal(1, decoded.Header.LogKeyEpoch);
        Assert.Equal(new long[] { 7, 8 }, decoded.Header.Segments);
        Assert.Equal("texts.log", decoded.Parts[1].Log);
    }

    [Fact]
    public void TheEncodedFormIsBase64OfGzippedJson()
    {
        var encoded = LogBatchCodec.Encode(Sample());

        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(encoded)), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        using var document = JsonDocument.Parse(reader.ReadToEnd());

        var header = document.RootElement.GetProperty("header");
        Assert.Equal(1, header.GetProperty("schema").GetInt32());
        Assert.Equal("post", header.GetProperty("app").GetString());
        Assert.Equal("inst-0a1b2c", header.GetProperty("installId").GetString());
        Assert.Equal("owner", header.GetProperty("phone").GetString());
        Assert.Equal("1.0.42", header.GetProperty("appVersion").GetString());
        Assert.Equal(200042, header.GetProperty("versionCode").GetInt64());
        Assert.Equal(1, header.GetProperty("logKeyEpoch").GetInt32());
        Assert.True(header.TryGetProperty("firstUtc", out _));
        Assert.True(header.TryGetProperty("lastUtc", out _));
        Assert.Equal(2, header.GetProperty("segments").GetArrayLength());
    }

    [Fact]
    public void TheHashIsOfTheContentSoTwoEncodingsOfOneBatchAgree()
    {
        var batch = Sample();

        var first = LogBatchCodec.Sha256(LogBatchCodec.Decode(LogBatchCodec.Encode(batch)));
        var second = LogBatchCodec.Sha256(batch);

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void DifferentContentHashesDifferently()
    {
        var batch = Sample();
        var changed = batch with { Parts = [batch.Parts[0] with { Text = "something else" }, batch.Parts[1]] };

        Assert.NotEqual(LogBatchCodec.Sha256(batch), LogBatchCodec.Sha256(changed));
    }

    [Fact]
    public void TheJsonReadsBackAsTheSameBatch()
    {
        var batch = Sample();

        Assert.Equal(LogBatchCodec.Sha256(batch), LogBatchCodec.Sha256(LogBatchCodec.FromJson(LogBatchCodec.Json(batch))));
        Assert.Throws<LogBatchFormatException>(() => LogBatchCodec.FromJson("[]"));
    }

    [Fact]
    public void GarbageIsAFormatErrorNotAnEmptyBatch()
    {
        Assert.Throws<LogBatchFormatException>(() => LogBatchCodec.Decode("not base64 at all !!"));
        Assert.Throws<LogBatchFormatException>(() => LogBatchCodec.Decode(Convert.ToBase64String([1, 2, 3])));
    }

    [Fact]
    public void BuildReadsTheSegmentsAndDatesTheBatchFromThem()
    {
        var segments = new[]
        {
            new SealedSegment(3, "diagnostic.log", "/x/000003.diagnostic.log.sealed", 10,
                new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero)),
            new SealedSegment(4, "texts.log", "/x/000004.texts.log.sealed", 12,
                new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero)),
        };

        var batch = LogBatch.Build(Install, segments, s => $"text of {s.Sequence}");

        Assert.Equal(new long[] { 3, 4 }, batch.Header.Segments);
        Assert.Equal(segments[0].SealedUtc, batch.Header.FirstUtc);
        Assert.Equal(segments[1].SealedUtc, batch.Header.LastUtc);
        Assert.Equal("text of 4", batch.Parts[1].Text);
        Assert.Equal("diagnostic.log", batch.Parts[0].Log);
        Assert.Equal(LogKey.Epoch, batch.Header.LogKeyEpoch);
    }

    [Fact]
    public void AnInstallIdIsRandomNeverDerivedFromTheDevice()
    {
        var one = LogInstall.NewInstallId();
        var two = LogInstall.NewInstallId();

        Assert.NotEqual(one, two);
        Assert.Matches("^[0-9a-f]{12}$", one);
    }
}
