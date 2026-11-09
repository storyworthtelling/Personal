using Android.App;
using Android.Content;
using Codex.Domain.Plate;

namespace Plate;

/// <summary>
/// The card read (from Post's Rolodex) and the statement read (from an Index account), on the errand screen but
/// behind their own door, like <see cref="RecipeReadActivity"/>.
///
/// <para>They used to be filters on <see cref="ErrandActivity"/>, which is exported with no permission because the
/// camera filters must stay open to every app. So any app on the phone could start a statement read and be handed
/// the balance the owner had just confirmed (audit W2, 09 M3). This one answers only to a caller holding
/// <see cref="ReadRequest.PaperPermission"/>, declared at signature level, and is named by
/// <see cref="ReadRequest.PaperActivity"/>, a registered Java name the callers start explicitly.</para>
/// </summary>
[Activity(
    Name = ReadRequest.PaperActivity,
    Label = "Plate",
    Exported = true,
    NoHistory = true,
    Permission = ReadRequest.PaperPermission,
    Theme = "@style/Theme.FocusPhone.Paper.AppCompat")]
[IntentFilter([ReadRequest.ReadCard, ReadRequest.ReadStatement], Categories = [Intent.CategoryDefault])]
public class PaperReadActivity : ErrandActivity
{
}
