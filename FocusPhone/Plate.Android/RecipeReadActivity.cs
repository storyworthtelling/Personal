using Android.App;
using Android.Content;
using Codex.Domain.Plate;

namespace Plate;

/// <summary>
/// Constitution's recipe read (PBI C-11.2.1): the same errand screen, behind its own door.
///
/// <para>It is a separate component because the camera filters on <see cref="ErrandActivity"/> must stay open to
/// every app on the phone, and Android sets a permission per component, not per filter. This one answers only
/// <see cref="ReadRequest.ReadRecipe"/> and only to a caller holding <see cref="ReadRequest.RecipePermission"/>,
/// which is declared at signature level, so only an app signed with the suite's one keystore can ask. The caller
/// names this class explicitly by <see cref="ReadRequest.RecipeActivity"/>, a registered Java name that must not
/// move, as a renamed class would be an activity-not-found on Constitution's side with nothing to say why.</para>
///
/// <para><b>It keeps nothing.</b> Pages are read by the screen the base class draws; each frame is deleted once
/// recognised, no MediaStore row is made, nothing enters the Darkroom, and the result carries three strings and no
/// image or URI. Backing out returns a cancel at once, like every errand.</para>
/// </summary>
[Activity(
    Name = ReadRequest.RecipeActivity,
    Label = "Plate",
    Exported = true,
    NoHistory = true,
    Permission = ReadRequest.RecipePermission,
    Theme = "@style/Theme.FocusPhone.Paper.AppCompat")]
[IntentFilter([ReadRequest.ReadRecipe], Categories = [Intent.CategoryDefault])]
public class RecipeReadActivity : ErrandActivity
{
}
