using Android.App;
using Pulp.Domain.Shell;

namespace Pulp.Shell;

/// <summary>
/// Discover, one tab of Pulp's binder (mockup 01, D212). The two Friday piles and the search field
/// are mockup 03. Until then the page shows its header and one quiet status sentence from <see
/// cref="PulpAreas"/>.
/// </summary>
public sealed class DiscoverArea(Activity host) : PulpAreaPage(host, PulpArea.Discover);
