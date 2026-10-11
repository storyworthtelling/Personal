namespace Pulp.Domain.Shell;

/// <summary>
/// "Open the shell on this area" — how a sub-page's binder tab reaches the area it names. The title
/// page and Recently watched draw the whole gutter, so a tab there must land on the tab tapped, not
/// on whatever area happened to be under the page. The request travels as an intent extra holding the
/// area's word; an absent or unknown word asks for nothing and the shell stays where it is.
/// </summary>
public static class AreaRequest
{
    /// <summary>The intent extra the shell reads in OnCreate and OnNewIntent.</summary>
    public const string Extra = "pulp.open-area";

    public static string Word(PulpArea area) => area switch
    {
        PulpArea.Watching => "watching",
        PulpArea.Playlists => "playlists",
        PulpArea.Discover => "discover",
        PulpArea.Sports => "sports",
        PulpArea.Calendar => "calendar",
        PulpArea.Settings => "settings",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "An area with no word for the shell's intent."),
    };

    public static PulpArea? Read(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return null;
        }

        foreach (var spec in PulpAreas.All)
        {
            if (string.Equals(Word(spec.Area), word.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return spec.Area;
            }
        }

        return null;
    }

    /// <summary>The area a binder tab at <paramref name="index"/> stands for: tabs are drawn in <see cref="PulpAreas.All"/>'s order.</summary>
    public static PulpArea? ForTab(int index) =>
        index >= 0 && index < PulpAreas.All.Count ? PulpAreas.All[index].Area : null;

    public static int IndexOf(PulpArea area)
    {
        for (var index = 0; index < PulpAreas.All.Count; index++)
        {
            if (PulpAreas.All[index].Area == area)
            {
                return index;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(area), area, "An area that is not on the binder strip.");
    }
}
