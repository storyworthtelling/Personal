using Android.App;
using Pulp.Domain.Shell;

namespace Pulp.Shell;

/// <summary>
/// Playlists, one tab of Pulp's binder (mockup 01, D212). A playlist is mockup 04; the list of
/// playlists arrives with it. Until then the page shows its header and one quiet status sentence
/// from <see cref="PulpAreas"/>.
/// </summary>
public sealed class PlaylistsArea(Activity host) : PulpAreaPage(host, PulpArea.Playlists);
