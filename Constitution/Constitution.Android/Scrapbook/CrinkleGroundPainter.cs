using Android.Content;
using Android.Views;

namespace Constitution.Scrapbook;

/// <summary>
/// Puts the crinkled ground behind a page's well (C-3.2.2, D43). Today, Workouts, Meals and Body
/// use it; Settings never does (D71), which is decided where the painter is chosen, by the page's
/// <c>PageGround</c>, not here.
/// </summary>
public sealed class CrinkleGroundPainter(Context context, int seed) : IPageGroundPainter
{
    public View Paint(View well) => new CrinkleGroundView(context, seed, well);
}
