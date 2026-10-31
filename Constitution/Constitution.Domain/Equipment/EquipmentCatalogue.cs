namespace Constitution.Domain.Equipment;

/// <summary>One thing the household may own, by a frozen id and the name the checklist shows.</summary>
public sealed record EquipmentItem(string Id, string Name);

/// <summary>
/// The six pieces of equipment the planner may prescribe (D72, book p.17), in the order the checklist
/// shows them. Ids are string literals, stored and matched on, so a renamed identifier must not rename
/// them; <c>EquipmentSetTests.TheSixFrozenIds</c> pins them.
/// </summary>
public static class EquipmentCatalogue
{
    public const string DumbbellsId = "dumbbells-or-kettlebell";
    public const string BandsId = "resistance-bands";
    public const string TreadmillId = "treadmill";
    public const string BenchId = "bench";
    public const string PullUpBarId = "pull-up-bar";
    public const string MatId = "mat";

    public static IReadOnlyList<EquipmentItem> All { get; } =
    [
        new(DumbbellsId, "Dumbbells or a kettlebell"),
        new(BandsId, "Resistance bands"),
        new(TreadmillId, "Treadmill"),
        new(BenchId, "Bench"),
        new(PullUpBarId, "Pull-up bar"),
        new(MatId, "Mat"),
    ];
}
