using Android.App;
using Android.Content;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.MealsTab;

/// <summary>
/// The errand to Plate (PBI C-6.5.1, D99). It starts Plate's recipe activity by name with the read action and carries nothing:
/// no extra, no data, nothing to open. It waits for the answer, which is three strings (the title, the ingredient lines, the step
/// lines), read here only by the contract's own extra names and judged by <see cref="ScanReturns.Decide"/>. A picture never
/// crosses in either direction. Decides nothing else: whether the answer is usable is the domain's.
/// </summary>
internal sealed class ScanRecipeLauncher(Activity host)
{
    /// <summary>The request code the answer comes back under. Health uses 7311.</summary>
    public const int RequestCode = 7412;

    /// <summary>
    /// Opens Plate's recipe read. Null when it opened; otherwise the sentence for why it did not (Plate is not on the phone, or
    /// would not take the request). The type of the failure is named, never its message.
    /// </summary>
    public string? Start()
    {
        var intent = new Intent(ScanRequest.Action);
        intent.SetClassName(ScanRequest.Package, ScanRequest.Component);
        try
        {
            host.StartActivityForResult(intent, RequestCode);
            return null;
        }
        catch (Exception e) when (e is ActivityNotFoundException or Java.Lang.SecurityException)
        {
            return ScanRequest.PlateAbsentSentence + " (" + e.GetType().Name + ")";
        }
    }

    /// <summary>What Plate's answer comes to. A result with no extras is read as a card with nothing on it.</summary>
    public static ScanReturn Read(Result resultCode, Intent? data) => ScanReturns.Decide(
        resultCode == Result.Ok,
        data?.GetStringExtra(RecipeReadContract.TitleExtra),
        data?.GetStringExtra(RecipeReadContract.IngredientsExtra),
        data?.GetStringExtra(RecipeReadContract.StepsExtra));
}
