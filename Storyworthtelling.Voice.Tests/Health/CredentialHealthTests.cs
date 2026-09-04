using Storyworthtelling.Voice.Health;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Health;

public class CredentialHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnknownReport_ReportsUnknownForAnyId()
    {
        var report = CredentialHealthReport.Empty;

        var health = report.Get("claude");

        Assert.Equal(CredentialStatus.Unknown, health.Status);
        Assert.Equal(string.Empty, health.Message);
        Assert.Null(health.CheckedAtUtc);
    }

    [Fact]
    public void MarkOk_RecordsOkStatusAndTime()
    {
        var report = CredentialHealthReport.Empty.MarkOk("claude", Now);

        var health = report.Get("claude");

        Assert.Equal(CredentialStatus.Ok, health.Status);
        Assert.Equal(Now, health.CheckedAtUtc);
    }

    [Fact]
    public void MarkFailing_RecordsMessageAndTime()
    {
        var report = CredentialHealthReport.Empty.MarkFailing("gemini", "401 Unauthorized", Now);

        var health = report.Get("gemini");

        Assert.Equal(CredentialStatus.Failing, health.Status);
        Assert.Equal("401 Unauthorized", health.Message);
        Assert.Equal(Now, health.CheckedAtUtc);
    }

    [Fact]
    public void MarkOk_ClearsAPriorFailure()
    {
        var report = CredentialHealthReport.Empty
            .MarkFailing("claude", "bad key", Now)
            .MarkOk("claude", Now.AddMinutes(5));

        var health = report.Get("claude");

        Assert.Equal(CredentialStatus.Ok, health.Status);
        Assert.Equal(string.Empty, health.Message);
        Assert.Equal(Now.AddMinutes(5), health.CheckedAtUtc);
    }

    [Fact]
    public void Marking_LeavesOtherCredentialsUntouched()
    {
        var report = CredentialHealthReport.Empty
            .MarkOk("claude", Now)
            .MarkFailing("gemini", "bad key", Now);

        Assert.Equal(CredentialStatus.Ok, report.Get("claude").Status);
        Assert.Equal(CredentialStatus.Failing, report.Get("gemini").Status);
    }

    [Fact]
    public void Codec_RoundTripsThroughJson()
    {
        var report = CredentialHealthReport.Empty
            .MarkOk("claude", Now)
            .MarkFailing("gemini", "429 rate limited", Now.AddMinutes(1));

        var restored = CredentialHealthCodec.Deserialize(CredentialHealthCodec.Serialize(report));

        Assert.Equal(CredentialStatus.Ok, restored.Get("claude").Status);
        Assert.Equal(Now, restored.Get("claude").CheckedAtUtc);
        var gemini = restored.Get("gemini");
        Assert.Equal(CredentialStatus.Failing, gemini.Status);
        Assert.Equal("429 rate limited", gemini.Message);
        Assert.Equal(Now.AddMinutes(1), gemini.CheckedAtUtc);
    }

    [Fact]
    public void Codec_DeserializesEmptyOrGarbageAsEmptyReport()
    {
        Assert.Empty(CredentialHealthCodec.Deserialize(null).Ids);
        Assert.Empty(CredentialHealthCodec.Deserialize("").Ids);
        Assert.Empty(CredentialHealthCodec.Deserialize("not json").Ids);
    }
}
