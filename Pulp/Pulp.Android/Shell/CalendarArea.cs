using Android.App;
using Pulp.Domain.Shell;

namespace Pulp.Shell;

/// <summary>
/// Calendar, one tab of Pulp's binder (mockup 01, D212). The two weeks ahead are mockup 08. Until
/// then the page shows its header and one quiet status sentence from <see cref="PulpAreas"/>.
/// </summary>
public sealed class CalendarArea(Activity host) : PulpAreaPage(host, PulpArea.Calendar);
