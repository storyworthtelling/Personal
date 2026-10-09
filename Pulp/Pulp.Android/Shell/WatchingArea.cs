using Android.App;
using Android.Views;
using Codex.Ui;
using Pulp.Domain.Shell;

namespace Pulp.Shell;

/// <summary>
/// Continue watching — Pulp's home, the area it opens on (mockup 01, D212). The rows, the voiced
/// greeting (D118) and the child's view arrive with their own screens; for now the page says what
/// will gather here.
///
/// <para>The bottom bar is drawn as approved and never changes (D213, D263): the secondary switches
/// to a child's view, the primary opens Recently watched. Neither page exists yet, so the two
/// buttons raise events the shell does not handle until they do.</para>
/// </summary>
public sealed class WatchingArea(Activity host) : PulpAreaPage(host, PulpArea.Watching)
{
    /// <summary>"Watch as a child" was tapped (D213).</summary>
    public event Action? ChildViewRequested;

    /// <summary>"Recently watched" was tapped (D213).</summary>
    public event Action? RecentlyWatchedRequested;

    public override View? BuildBottomBar() =>
        PaperUi.BottomBar(
            Host,
            PaperUi.SecondaryButton(Host, "Watch as a child", () => ChildViewRequested?.Invoke()),
            PaperUi.PrimaryButton(Host, "Recently watched", () => RecentlyWatchedRequested?.Invoke()));
}
