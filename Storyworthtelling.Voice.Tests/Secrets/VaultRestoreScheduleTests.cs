using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Tests.Secrets;

// Efficiency audit §24 and the owner's decision: Index, Folio and Foreword restored the whole vault from Drive on
// every launch. A launch now restores only when six hours have passed since the last one that opened the vault,
// or at once when a local value is missing or a key was refused.
public class VaultRestoreScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private static bool Due(DateTimeOffset? last, string?[]? locals = null, CredentialHealthReport? health = null) =>
        VaultRestoreSchedule.IsDue(last, Now, locals ?? ["invented-a", "invented-b"], health ?? CredentialHealthReport.Empty);

    [Fact]
    public void ANeverRestoredInstallRestores() => Assert.True(Due(null));

    [Fact]
    public void ARecentRestoreIsNotRepeated() => Assert.False(Due(Now.AddHours(-5)));

    [Fact]
    public void SixHoursLaterItRestoresAgain() => Assert.True(Due(Now - VaultRestoreSchedule.Interval));

    [Fact]
    public void AClockThatWentBackRestores() => Assert.True(Due(Now.AddHours(1)));

    [Fact]
    public void AMissingLocalValueRestoresAtOnce() => Assert.True(Due(Now.AddMinutes(-5), ["invented-a", ""]));

    [Fact]
    public void ARefusedKeyRestoresAtOnce() =>
        Assert.True(Due(Now.AddMinutes(-5), health: CredentialHealthReport.Empty.MarkFailing("claude", "refused", Now)));

    [Fact]
    public void AWorkingKeyDoesNotForceARestore() =>
        Assert.False(Due(Now.AddMinutes(-5), health: CredentialHealthReport.Empty.MarkOk("claude", Now)));
}
