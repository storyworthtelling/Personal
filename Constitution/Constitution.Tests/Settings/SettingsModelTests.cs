using Constitution.Domain.Areas;
using Constitution.Domain.Equipment;
using Constitution.Domain.Settings;

namespace Constitution.Tests.Settings;

/// <summary>C-8.1.1: Settings plain and short (book p.17; D71, D72). Names here are invented.</summary>
public class SettingsModelTests
{
    private static SettingsModel Build(Pillars? pillars = null, EquipmentSet? equipment = null) =>
        SettingsModel.Build(pillars ?? new Pillars(Body: true, Workouts: true, Meals: false), equipment ?? EquipmentSet.Of(
            [EquipmentCatalogue.DumbbellsId, EquipmentCatalogue.BandsId, EquipmentCatalogue.TreadmillId,
             EquipmentCatalogue.BenchId, EquipmentCatalogue.PullUpBarId]), []);

    [Fact]
    public void PassphraseFieldIsNotMasked()
    {
        var field = Build().Passphrase;

        Assert.False(field.Masked);
        Assert.True(field.Mono);
        Assert.Equal("Shown in the clear so it can be read back into each app.", field.Helper);
    }

    [Fact]
    public void EquipmentIsCheckboxRows()
    {
        var model = Build();

        Assert.Equal(
            ["Dumbbells or a kettlebell", "Resistance bands", "Treadmill", "Bench", "Pull-up bar", "Mat"],
            model.EquipmentRows.Select(row => row.Name));
        Assert.All(model.EquipmentRows, row => Assert.Equal(SettingControl.Checkbox, row.Control));
        Assert.Equal("Equipment 5 of 6", model.EquipmentHeader);
        Assert.False(model.EquipmentRows.Single(row => row.Id == EquipmentCatalogue.MatId).Picked);
        Assert.Equal(5, model.EquipmentRows.Count(row => row.Picked));
    }

    [Fact]
    public void PillarsAreOneSwitchEachWithMealsCaveat()
    {
        var model = Build();

        Assert.Equal(["Workouts", "Meals", "Body"], model.PillarRows.Select(row => row.Name));
        Assert.All(model.PillarRows, row => Assert.Equal(SettingControl.Switch, row.Control));
        Assert.Equal("Pillars 2 on", model.PillarsHeader);
        Assert.Equal(
            "Stays a household plan even when off here",
            model.PillarRows.Single(row => row.Id == "meals").Caveat);
        Assert.All(model.PillarRows.Where(row => row.Id != "meals"), row => Assert.Null(row.Caveat));
        Assert.False(model.PillarRows.Single(row => row.Id == "meals").On);
    }

    [Fact]
    public void ThePillarRowIdsAreTheAreaIds()
    {
        var ids = Build().PillarRows.Select(row => row.Id).ToList();

        Assert.All(ids, id => Assert.Contains(ConstitutionAreas.All, area => area.Id == id));
    }

    [Fact]
    public void ExportIsAQuietButtonAtTheFoot()
    {
        Assert.Equal("Export diagnostics", Build().ExportLabel);
    }

    // ---- review S10: the overnight permission is asked for by a line, not promised ----

    [Fact]
    public void ConnectionsCarryAnOvernightReadsLineWithItsPlainSentence()
    {
        var model = SettingsModel.Build(new Pillars(Body: true, Workouts: true, Meals: true), EquipmentSet.Of([]), []);

        Assert.Equal("Overnight reads", model.OvernightReads.Name);
        Assert.Equal(
            "Allow overnight reads in Health Connect so the morning check is current; otherwise values refresh when you open the app.",
            model.OvernightReads.Sentence);
        Assert.Equal("Open Health Connect", model.OvernightReads.ButtonLabel);
    }
}

public class EquipmentSetTests
{
    [Fact]
    public void TheSixFrozenIds()
    {
        Assert.Equal(
            ["dumbbells-or-kettlebell", "resistance-bands", "treadmill", "bench", "pull-up-bar", "mat"],
            EquipmentCatalogue.All.Select(item => item.Id));
    }

    [Fact]
    public void CountOnCountsOnlyKnownIds()
    {
        var set = EquipmentSet.Of(["mat", "bench", "a-seventh-thing"]);

        Assert.Equal(2, set.CountOn);
    }

    [Fact]
    public void NothingStoredMeansEverythingTicked()
    {
        Assert.Equal(6, EquipmentSet.Decode(null).CountOn);
        Assert.Equal(6, EquipmentSet.Decode("").CountOn);
    }

    [Fact]
    public void AStoredEmptyChoiceStaysEmpty()
    {
        var none = EquipmentSet.Of([]);

        Assert.Equal(0, EquipmentSet.Decode(none.Encode()).CountOn);
        Assert.NotEqual(string.Empty, none.Encode());
    }

    [Fact]
    public void RoundTripsAndWithToggles()
    {
        var set = EquipmentSet.Decode(EquipmentSet.Of(["mat", "treadmill"]).Encode()).With("bench", true).With("mat", false);

        Assert.True(set.Has("bench"));
        Assert.True(set.Has("treadmill"));
        Assert.False(set.Has("mat"));
        Assert.Equal(2, set.CountOn);
    }
}
