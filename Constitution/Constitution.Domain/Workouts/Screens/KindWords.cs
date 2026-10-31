namespace Constitution.Domain.Workouts.Screens;

/// <summary>The words a kind of session goes by on the tab and on Today's clipping, one way each (book p.12). A kind with no word cannot be shown.</summary>
public static class KindWords
{
    public static string Of(SessionKind kind, Together together) => kind switch
    {
        SessionKind.Strength => "Strength",
        SessionKind.OutdoorCardio => "Outdoor walk",
        SessionKind.Treadmill => "Treadmill",
        SessionKind.Mobility => "Mobility",
        SessionKind.GentleWalk => together == Together.FamilyWalk ? "Family walk" : "Gentle walk",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The tab has no word for this kind of session."),
    };
}
