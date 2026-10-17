using Constitution.Domain.Areas;

namespace Constitution.Domain.Ground;

/// <summary>
/// Which ground a page sits on. Settings is plain (D71); the other four areas are crinkled
/// exam-table paper (D43).
/// </summary>
public sealed class PageGround
{
    private PageGround(string name) => Name = name;

    public string Name { get; }

    /// <summary>Plain suite paper, nothing on it.</summary>
    public static PageGround Plain { get; } = new("plain");

    /// <summary>Crinkled paper with one tear line.</summary>
    public static PageGround Crinkled { get; } = new("crinkled");

    public static PageGround For(ConstitutionArea area) =>
        area.Id == "settings" ? Plain : Crinkled;

    public override string ToString() => Name;
}
