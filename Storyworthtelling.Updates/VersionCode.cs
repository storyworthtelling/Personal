namespace Storyworthtelling.Updates;

/// <summary>
/// The suite's one versionCode formula: <c>band × 100,000,000 + minutes since 2020-01-01 UTC</c>.
///
/// <para>It exists because the old arrangement failed twice in the same way. versionCode was the
/// workflow's <c>run_number</c>, so renaming <c>build-focusphone.yml</c> to <c>build-flyleaf.yml</c>
/// reset the counter and the next build looked <b>older</b> than what was already installed — which
/// the update channel rejects silently, and forever, because a counter that restarted never catches
/// up on its own. The fix was a 100000 floor. Splitting Post and Plate into their own workflows reset
/// it again, and the fix was a 200000 floor. A floor is a ratchet: it works only while somebody
/// remembers to bump it before the rename ships.</para>
///
/// <para>So the formula depends on no counter at all. Wall-clock minutes only go forward, whatever
/// happens to a workflow file, and the <b>band</b> gives each app a numbering space chosen once to
/// clear whatever it has already shipped. A band is <i>not</i> the version the owner sees: Foreword
/// displays 2.0.x while sitting in band 7, because it has to clear DailyLoader's old timestamp codes
/// (~609,000,000) to upgrade in place rather than needing an uninstall.</para>
///
/// <para>The unit is the minute rather than the second because a second-resolution code overflows
/// int32 above band 2, and Android's versionCode is a signed 32-bit integer. The cost is that two
/// builds of the same app finishing inside one minute tie, so the later is not installable over the
/// earlier — the next build fixes it, and a workflow takes several minutes anyway.</para>
/// </summary>
public static class VersionCode
{
    /// <summary>Minute zero. Chosen once; moving it would renumber every app downward.</summary>
    public static readonly DateTime Epoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>How many minutes a band holds — about 190 years.</summary>
    public const int BandSize = 100_000_000;

    public const int MinBand = 1;

    /// <summary>
    /// Band 20 tops out at 2,099,999,999, inside int32; band 21 would not, and Android
    /// mangles an out-of-range versionCode rather than refusing the build.
    /// </summary>
    public const int MaxBand = 20;

    public static int For(int band, DateTime moment)
    {
        if (band is < MinBand or > MaxBand)
        {
            throw new ArgumentOutOfRangeException(nameof(band), band,
                $"A versionCode band must be between {MinBand} and {MaxBand}: band {MaxBand + 1} and above "
                + "exceed Android's signed 32-bit versionCode.");
        }

        if (moment.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException(
                "The moment must say whether it is UTC or local. An unspecified kind would be read as UTC "
                + "on the build runner and as local on a developer's machine, producing two different codes "
                + "for the same build.", nameof(moment));
        }

        var minutes = (long)(moment.ToUniversalTime() - Epoch).TotalMinutes;

        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moment), moment,
                $"A versionCode cannot be built from a moment before {Epoch:yyyy-MM-dd}.");
        }

        if (minutes >= BandSize)
        {
            throw new ArgumentOutOfRangeException(nameof(moment), moment,
                "This moment is more than a band's worth of minutes past the epoch, so its code would spill "
                + "into the next band and collide with another app's numbering.");
        }

        return (band * BandSize) + (int)minutes;
    }
}

/// <summary>
/// One app's numbering space, and the highest versionCode it shipped under the old
/// per-workflow schemes. The second number is the reason the first was chosen.
/// </summary>
public sealed record AppVersionBand(string Package, int Band, int LastShippedBeforeUnification);

/// <summary>
/// Every app on the rolling channel, with the band it was given. Recorded here rather than
/// in each workflow so the choice can be checked against what actually shipped — the check
/// nobody ran before either floor bump.
/// </summary>
public static class SuiteVersionBands
{
    public static IReadOnlyList<AppVersionBand> All { get; } =
    [
        // Flyleaf and Score share build-flyleaf.yml, so they shared 100000 + run_number.
        new("com.storyworthtelling.focusphone.launcher", 1, 100_163),
        new("com.storyworthtelling.focusphone.audio", 1, 100_163),

        // Post and Plate took the 200000 floor when they were split into their own workflows.
        new("com.storyworthtelling.focusphone.communication", 1, 200_051),
        new("com.storyworthtelling.focusphone.camera", 1, 200_030),

        // Ledger and Folio were on a bare run_number — two and three digits. A rename of
        // either workflow would have reset them to 1 and bricked their own update channel.
        new("com.storyworthtelling.ledger", 1, 58),
        new("com.storyworthtelling.storyboarder", 1, 169),

        // Foreword keeps DailyLoader's package, so it has to clear the old csproj scheme
        // (yearDigit + MMddHHmm, which reached ~609,060,000 in September 2026). Band 7 puts it
        // above that and lets it upgrade in place. Its display version is 2.0.x regardless.
        new("com.storyworthtelling.dailyloader", 7, 609_999_999),
    ];
}
