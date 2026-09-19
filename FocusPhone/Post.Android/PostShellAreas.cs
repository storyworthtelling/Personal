using Codex.Domain.Design;
using Codex.Ui;

namespace Post;

/// <summary>
/// Post's four areas, in <see cref="PostActivity"/>'s own order, for the binder Post's
/// secondary screens (an open email, the diagnostics log, and so on) show via
/// <see cref="BinderFrame"/>.
///
/// <para>Duplicated from each <c>PostArea.Tab</c> rather than reused — see
/// <c>Score.ScoreShellAreas</c> for why — and kept in step with <see cref="PostActivity"/>'s
/// own <c>areas =</c> array by the same source-scan parity guard.</para>
/// </summary>
internal static class PostShellAreas
{
    public const int Mailbox = 0;
    public const int Filed = 1;
    public const int Rolodex = 2;
    public const int Settings = 3;

    public static readonly IReadOnlyList<BinderArea> All =
    [
        new("Mailbox", Resource.Drawable.area_mailbox, PaperTheme.PostMark, PaperTheme.PostFill),
        new("Filed", Resource.Drawable.area_filed, PaperTheme.CautionMark, PaperTheme.CautionFill),
        new("Rolodex", Resource.Drawable.area_rolodex, PaperTheme.PositiveMark, PaperTheme.PositiveFill),
        new("Settings", Resource.Drawable.area_settings, PaperTheme.ResolvedMark, PaperTheme.ResolvedFill),
    ];
}
