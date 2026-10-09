using Android.App;
using Pulp.Domain.Shell;

namespace Pulp.Shell;

/// <summary>
/// Sports, one tab of Pulp's binder (mockup 01, D212). The leagues are mockup 07. Until then the
/// page shows its header and one quiet status sentence from <see cref="PulpAreas"/>.
/// </summary>
public sealed class SportsArea(Activity host) : PulpAreaPage(host, PulpArea.Sports);
