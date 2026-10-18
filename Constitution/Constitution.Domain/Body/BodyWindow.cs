namespace Constitution.Domain.Body;

/// <summary>The stretch a chart shows, ending today. <see cref="Month"/> is the default, so it is the zero member.</summary>
public enum BodyWindow
{
    /// <summary>The last 30 days.</summary>
    Month,

    /// <summary>The last 90 days.</summary>
    Quarter,

    /// <summary>The last 365 days.</summary>
    Year,
}

/// <summary>What a <see cref="BodyWindow"/> spans.</summary>
public static class BodyWindows
{
    /// <summary>The window Body opens on.</summary>
    public const BodyWindow Default = BodyWindow.Month;

    /// <summary>The three windows in the order the segmented switch shows them.</summary>
    public static IReadOnlyList<BodyWindow> All { get; } = [BodyWindow.Month, BodyWindow.Quarter, BodyWindow.Year];

    /// <summary>Days in the window, today included: 30, 90 or 365.</summary>
    public static int Days(BodyWindow window) => window switch
    {
        BodyWindow.Month => 30,
        BodyWindow.Quarter => 90,
        BodyWindow.Year => 365,
        _ => throw new ArgumentOutOfRangeException(nameof(window), window, "That is not one of Body's windows."),
    };
}
