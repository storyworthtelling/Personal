namespace Foreword.Domain.Prayer;

/// <summary>
/// What came back from reading a journal page.
///
/// <para><see cref="Succeeded"/> is the flag the morning hangs the trash on, and it is not the same
/// question as whether anything was written. A read that worked and found a blank page means the
/// sheet can be thrown away; a read that <i>failed</i> means it must not be — and since the journal
/// page is the only copy until it has been transcribed, collapsing those two would quietly destroy
/// a morning's writing every time the network was down.</para>
/// </summary>
public readonly record struct JournalRead(
    bool Succeeded, PrayerText Journalled, IReadOnlyList<PrayerText> Added)
{
    public static JournalRead Nothing { get; } = new(false, PrayerText.Empty, []);
}
