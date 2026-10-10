using Android.App;
using Android.Views;
using Pulp.Domain.Shell;
using Pulp.Playlists;

namespace Pulp.Shell;

/// <summary>
/// Playlists, one tab of Pulp's binder (mockup 01, D212): the viewer's playlists, a playlist's page
/// (mockup 04, D225) and the made-playlist flow (D104, D272). The area keeps the frame's contract —
/// header, Back, Resume — and hands everything else to <see cref="PlaylistsScreen"/>.
/// </summary>
public sealed class PlaylistsArea : PulpAreaPage
{
    private readonly PlaylistsScreen screen;

    public PlaylistsArea(Activity host) : base(host, PulpArea.Playlists)
    {
        screen = new PlaylistsScreen(host, () => Overlay, Invalidate);
    }

    /// <summary>A playlist's own name is its page's header (mockup 04); the list and the flow name themselves.</summary>
    public override string Title => screen.Title ?? Spec.Title;

    public override View BuildView() => screen.Build();

    public override bool GoBack() => screen.GoBack();

    public override void Resume() => screen.Reload();
}
