using Constitution.Domain.Diagnostics;

namespace Constitution.Tests.Diagnostics;

/// <summary>
/// What the weekly run assumed because the household had not said: a closed list, with a fixed phrase for each, in the export. An assumption
/// is named so it can be corrected; nothing the household wrote (a time, a zone, a name) can ride along, because the type holds an enum.
/// </summary>
public class AssumedSettingsTests
{
    private static DiagnosticsSnapshot Snapshot(IEnumerable<AssumedSetting>? assumed) =>
        new(AppVersion.Parse("1.0.1"), InstallRole.Owner, new DiagnosticsCounts(0, 0, 0, []), [], [], [], null, assumed);

    [Fact]
    public void EveryAssumedSettingHasItsFixedPhrase()
    {
        Assert.Equal(
            ["Dinner time", "Bedtime routine", "Lunch gap", "Pickup duty", "Work hours", "Time zone"],
            Enum.GetValues<AssumedSetting>().Select(DiagnosticsPhrases.Of));
    }

    [Fact]
    public void NoPhraseCarriesADigitAHealthWordOrAName()
    {
        Assert.NotEmpty(Enum.GetValues<AssumedSetting>());
        foreach (var setting in Enum.GetValues<AssumedSetting>())
        {
            Assert.Empty(PrivacyScan.Find(DiagnosticsPhrases.Of(setting)));
        }
    }

    [Fact]
    public void AnUndefinedSettingIsRefusedRatherThanPrintedAsANumber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticsPhrases.Of((AssumedSetting)99));
    }

    [Fact]
    public void TheExportListsWhatWasAssumedInTheListsOrder()
    {
        var text = DiagnosticsRenderer.Render(Snapshot([AssumedSetting.DinnerTime, AssumedSetting.LunchGap, AssumedSetting.TimeZone]));

        Assert.Contains("\nAssumed (not given by the household)\n  Dinner time\n  Lunch gap\n  Time zone\n", text, StringComparison.Ordinal);
        Assert.Empty(PrivacyScan.Find(text));
    }

    [Fact]
    public void NothingAssumedAddsNoSection()
    {
        Assert.DoesNotContain("Assumed", DiagnosticsRenderer.Render(Snapshot(null)), StringComparison.Ordinal);
        Assert.DoesNotContain("Assumed", DiagnosticsRenderer.Render(Snapshot([])), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSnapshotKeepsWhatItWasGiven()
    {
        Assert.Equal([AssumedSetting.WorkHours], Snapshot([AssumedSetting.WorkHours]).Assumed);
        Assert.Empty(Snapshot(null).Assumed);
    }

    [Fact]
    public void ASettingIsAssumedOnceOrNotAtAll()
    {
        Assert.Throws<ArgumentException>(() => Snapshot([AssumedSetting.DinnerTime, AssumedSetting.DinnerTime]));
    }

    [Fact]
    public void AnUndefinedSettingCannotEnterTheSnapshot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Snapshot([(AssumedSetting)99]));
    }
}
