namespace Pulp.Domain.Voice;

/// <summary>
/// Every place Pulp speaks in the owner's voice (D38) — a closed list, so a new voiced surface is a
/// decision rather than a diff. Errors, notices and the dog-ear are not on it and never will be
/// (DESIGN.md §14). <see cref="VoicedOccasions"/> gives each its purpose, audience and fallback.
///
/// <para><b>Absent on purpose.</b> The Friday stack opener (D38 rules it out). The watching-ahead
/// line (D99, with D102) is deferred with "together" to the TV phase (D194): in P12 every viewer's
/// progress is their own, so there is nobody to be ahead of. It joins this list, by decision, when
/// together-progress exists.</para>
/// </summary>
public enum VoiceSeat
{
    /// <summary>Under Continue Watching's header, hidden until composed (D38, D118).</summary>
    Greeting,

    /// <summary>The title page's reason this title was chosen; may name one past title (D36, D41).</summary>
    WhyThis,

    /// <summary>A line per playlist (D38).</summary>
    PlaylistLine,

    /// <summary>A gentle greeting in a child's view (D39), at the child's reading level (D233).</summary>
    ChildGreeting,

    /// <summary>The line on a child's Favorites, moderated for their age (D232, D233).</summary>
    ChildFavorites,

    /// <summary>Continue Watching with nothing started (D264, mockup 13).</summary>
    EmptyContinueWatching,

    /// <summary>Playlists with none made (D264).</summary>
    EmptyPlaylists,

    /// <summary>A playlist with nothing in it (D264).</summary>
    EmptyPlaylist,

    /// <summary>Recently watched with nothing in it (D264, D263).</summary>
    EmptyRecentlyWatched,

    /// <summary>A child's Favorites with nothing on it (D264), at the child's reading level (D233).</summary>
    EmptyChildFavorites,

    /// <summary>The Calendar with nothing in the next two weeks (D264).</summary>
    EmptyCalendar,

    /// <summary>Sports with no team followed (D264).</summary>
    EmptySports,

    /// <summary>A used-up Friday stack, until Friday (D112, D264).</summary>
    StackUsedUp,
}

/// <summary>What a seat shows when there is no profile or key (DESIGN.md §14).</summary>
public enum VoiceFallback
{
    /// <summary>The line is simply not there (D36, D118, D232).</summary>
    Hide,

    /// <summary>The §7 neutral Serif italic line takes its place (D264).</summary>
    NeutralLine,
}

/// <summary>Who a seat speaks to. A child's seat is moderated to the child's age band (D233).</summary>
public enum VoiceAudience
{
    Adult,
    Child,
}
