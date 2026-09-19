using Codex.Domain.Design;
using Codex.Ui;

namespace Score;

/// <summary>
/// Score's six areas, in <see cref="ScoreActivity"/>'s own order, for the binder Score's
/// secondary screens (Equalizer, Diagnostics, and so on) show via <see cref="BinderFrame"/>.
///
/// <para>Duplicated from each <c>ScoreArea.Tab</c> rather than reused, because those live on
/// area instances that carry a whole library/playback state a standalone Activity has no
/// reason to build. A source-scan parity guard (<c>Codex.Tests.Guard.BinderFrameCoverageTests</c>)
/// keeps this list matching <see cref="ScoreActivity"/>'s own <c>areas =</c> array, so the two
/// cannot silently drift.</para>
/// </summary>
internal static class ScoreShellAreas
{
    public const int Music = 0;
    public const int Podcasts = 1;
    public const int Playing = 2;
    public const int Discover = 3;
    public const int Workout = 4;
    public const int Settings = 5;

    public static readonly IReadOnlyList<BinderArea> All =
    [
        new("Music", Resource.Drawable.area_music, PaperTheme.ScoreMark, PaperTheme.ScoreFill),
        new("Podcasts", Resource.Drawable.area_podcasts, PaperTheme.ReferenceMark, PaperTheme.ReferenceFill),
        new("Playing", Resource.Drawable.area_playing, PaperTheme.ProgressMark, PaperTheme.ProgressFill),
        new("Discover", Resource.Drawable.area_discover, PaperTheme.PositiveMark, PaperTheme.PositiveFill),
        new("Workout", Resource.Drawable.area_workout, PaperTheme.CautionMark, PaperTheme.CautionFill),
        new("Settings", Resource.Drawable.area_settings, PaperTheme.ResolvedMark, PaperTheme.ResolvedFill),
    ];
}
