namespace Pulp.Domain.ChildView;

/// <summary>
/// The child's view's own words. None of them is voiced: the line under the header names who is
/// watching (D179), the buttons are D213's, and a problem is a quiet sentence (DESIGN.md §14).
/// </summary>
public static class ChildViewText
{
    public const string WatchAsAChild = "Watch as a child";
    public const string BackToMe = "Back to me";
    public const string Favorites = "Favorites";
    public const string WhoIsWatching = "Who's watching?";
    public const string NoChildren = "There are no children in the household yet.";
    public const string NoHousehold = "The household isn't set up yet.";
    public const string Reading = "Finding the favorites.";
    public const string ReadingWatching = "Finding where you left off.";
    public const string ReadingPlaylists = "Finding the playlists.";
    public const string Unreadable = "This page could not be read just now.";
    public const string DiscoverStatus = "Look for something by its name, or by how you feel.";
    public const string NothingFound = "Nothing here for this viewer.";

    /// <summary>"Watching as Robin", "Watching as Robin and Sam", "Watching as Robin, Sam and Ada" (D179).</summary>
    public static string WatchingAs(IReadOnlyList<string> names) => "Watching as " + Join(names);

    /// <summary>The picker's choice for every child at once: "Robin and Sam together".</summary>
    public static string Together(IReadOnlyList<string> names) => Join(names) + " together";

    public static string NotAChild(string name) => $"{name} isn't a child in the household any more.";

    public static string NoFilter(string name) => $"{name}'s filter isn't set yet.";

    private static string Join(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var named = names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList();
        return named.Count switch
        {
            0 => throw new ArgumentException("A child's view names at least one child.", nameof(names)),
            1 => named[0],
            _ => string.Join(", ", named.Take(named.Count - 1)) + " and " + named[^1],
        };
    }
}
