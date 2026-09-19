using Codex.Domain.Design;
using Codex.Ui;

namespace Plate;

/// <summary>
/// Plate's four areas, in <see cref="PlateActivity"/>'s own order, for the binder Plate's
/// secondary screens (the editor) show via <see cref="BinderFrame"/>.
///
/// <para>Duplicated from each <c>PlateArea.Tab</c> rather than reused — see
/// <c>Score.ScoreShellAreas</c> for why — and kept in step with <see cref="PlateActivity"/>'s
/// own <c>areas =</c> array by the same source-scan parity guard. <c>ErrandActivity</c> never
/// uses this: DESIGN.md's own exception for it says there is deliberately no binder gutter on
/// that screen at all.</para>
/// </summary>
internal static class PlateShellAreas
{
    public const int Camera = 0;
    public const int Scan = 1;
    public const int Darkroom = 2;
    public const int Settings = 3;

    public static readonly IReadOnlyList<BinderArea> All =
    [
        new("Camera", Resource.Drawable.area_camera, PaperTheme.PlateMark, PaperTheme.PlateFill),
        new("Scan", Resource.Drawable.area_scan, PaperTheme.ReferenceMark, PaperTheme.ReferenceFill),
        new("Darkroom", Resource.Drawable.area_darkroom, PaperTheme.ProgressMark, PaperTheme.ProgressFill),
        new("Settings", Resource.Drawable.area_settings, PaperTheme.ResolvedMark, PaperTheme.ResolvedFill),
    ];
}
