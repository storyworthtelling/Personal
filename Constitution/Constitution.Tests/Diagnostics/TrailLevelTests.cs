using Constitution.Domain.Diagnostics;

namespace Constitution.Tests.Diagnostics;

/// <summary>
/// Audit 08 §10: every Constitution trail line was INFO, so a refused vault, an unreadable household file, Health
/// Connect revoked and a failed calendar write read at the same level as "ok, 2 readings". The phrases are the code's
/// own, so the rule is tested against what the trails really say.
/// </summary>
public class TrailLevelTests
{
    [Theory]
    [InlineData("body sync worker failed: InvalidOperationException")]
    [InlineData("vault unreachable")]
    [InlineData("meal photo: unreadable")]
    [InlineData("meal photo: permission denied")]
    [InlineData("budget broadcast not kept: IOException")]
    [InlineData("ink read back questions not stored: IOException")]
    [InlineData("health settings page not opened: ActivityNotFoundException")]
    [InlineData("health permission weight: not granted")]
    [InlineData("health read steps: unsupported (no health connect service)")]
    [InlineData("body sync worker skipped: background read not granted")]
    [InlineData("sync refused, file here corrupt")]
    [InlineData("sync offline, try again")]
    [InlineData("sync, not written, try again")]
    [InlineData("3 records unreadable")]
    [InlineData("sleepiq sign in: unexpected shape (no key)")]
    [InlineData("sleepiq sleepers: offline (HttpRequestException)")]
    [InlineData("body bed sleep: login refused earlier, not asked")]
    [InlineData("vault restore failed: HttpRequestException")]
    [InlineData("meal photo: not deleted")]
    public void AFailureIsAProblem(string phrase) => Assert.True(TrailLevel.IsProblem(phrase), phrase);

    [Theory]
    [InlineData("sync ok")]
    [InlineData("body install day recorded")]
    [InlineData("health permission weight: granted")]
    [InlineData("health read steps: 4 records, 4 usable")]
    [InlineData("ink read back worker ran, 2 documents")]
    [InlineData("vault restore skipped: no passphrase")]
    [InlineData("sync skipped, not signed in")]
    [InlineData("meal photo: deleted")]
    [InlineData("body read sleep: bed only, 1 reading")]
    public void AnOrdinaryOutcomeIsNot(string phrase) => Assert.False(TrailLevel.IsProblem(phrase), phrase);
}
