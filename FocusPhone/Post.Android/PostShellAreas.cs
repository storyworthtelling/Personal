using Codex.Domain.Design;
using Codex.Ui;
using Storyworthtelling.Materials;

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
        new("Mailbox", TabGlyphs.Mailbox, PaperTheme.PostMark, PaperTheme.PostFill),
        new("Filed", TabGlyphs.Folder, PaperTheme.CautionMark, PaperTheme.CautionFill),
        new("Rolodex", TabGlyphs.Rolodex, PaperTheme.PositiveMark, PaperTheme.PositiveFill),
        new("Settings", TabGlyphs.Settings, PaperTheme.ResolvedMark, PaperTheme.ResolvedFill),
    ];
}
