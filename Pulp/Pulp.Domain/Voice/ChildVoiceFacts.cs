using Pulp.Domain.Children;

namespace Pulp.Domain.Voice;

/// <summary>
/// The facts a child's seat may be told (D39, D232, D233) — only what the child's own view already
/// holds. Every builder takes the child's <see cref="ChildRules"/>, whose ceiling becomes the reading
/// level, and nothing from an adult: no past titles, no other viewer, no stack (a child has none,
/// D42). A title is named only when the child's filter shows it, so the line can never be the one
/// place a hidden title surfaces.
/// </summary>
public static class ChildVoiceFacts
{
    public static VoiceFacts Greeting(ChildRules child, DateTimeOffset localNow, bool firstOpenOfDay)
    {
        ArgumentNullException.ThrowIfNull(child);
        var lines = new List<string> { $"It is {VoiceFactBuilder.PartOfDay(localNow)}." };
        if (firstOpenOfDay)
        {
            lines.Add("This is the first visit today.");
        }

        return new VoiceFacts(VoiceSeat.ChildGreeting, lines, child.Ceiling);
    }

    /// <summary>
    /// The Favorites line (D232): how many favorites the child can see, and the name of ONE — the first
    /// in the page's own order that the child's filter shows.
    /// </summary>
    public static VoiceFacts Favorites(ChildRules child, IEnumerable<NamedTitle> favorites)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(favorites);

        var visible = favorites
            .Where(title => !string.IsNullOrWhiteSpace(title.Name) && ChildFilter.Decide(child, title.Facts).Visible)
            .ToList();

        var lines = new List<string>
        {
            visible.Count switch
            {
                0 => "There are no favorites yet.",
                1 => "There is one favorite.",
                _ => $"There are {NumberWords.Of(visible.Count)} favorites.",
            },
        };
        if (visible.Count > 0)
        {
            lines.Add($"One of them is {visible[0].Name}.");
        }

        return new VoiceFacts(VoiceSeat.ChildFavorites, lines, child.Ceiling);
    }

    public static VoiceFacts EmptyFavorites(ChildRules child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return new VoiceFacts(VoiceSeat.EmptyChildFavorites, ["There are no favorites yet."], child.Ceiling);
    }
}
