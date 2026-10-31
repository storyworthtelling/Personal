using Android.Content;
using Android.Views;
using Android.Widget;
using Constitution.Domain.Workouts.Screens;
using Constitution.Scrapbook;

namespace Constitution.WorkoutsTab;

/// <summary>
/// The Workouts tab (book page 12): today's large clipping, then the next seven days, on the crinkled ground. It paints a
/// <see cref="WorkoutsTabModel"/> and decides nothing. A tap opens nothing; a long-press on a day that can be acted on
/// hands that day to the area, which raises the card.
/// </summary>
internal sealed class WorkoutsTabPage : LinearLayout, IFullBleedPage
{
    public WorkoutsTabPage(Context context, WorkoutsTabModel model, DateOnly? pressedDay, Action<DayClipping> onHold) : base(context)
    {
        Orientation = Orientation.Vertical;
        var stack = new DayClippingStack(context);
        var items = new List<ClippingItem>();
        if (model.Today is { } today)
        {
            items.Add(Item(context, today, true, pressedDay, onHold));
        }

        items.AddRange(model.Days.Select(day => Item(context, day, false, pressedDay, onHold)));
        stack.SetClippings(items);

        var scroll = new ScrollView(context);
        scroll.AddView(stack, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        AddView(scroll, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
    }

    private static ClippingItem Item(Context context, DayClipping day, bool large, DateOnly? pressedDay, Action<DayClipping> onHold) =>
        new(
            "workouts-" + day.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            large,
            large ? ClippingContent.Today(context, day) : ClippingContent.Row(context, day),
            large ? TodayFolio(day) : null,
            pressedDay == day.Day,
            day.CanPress ? () => onHold(day) : null);

    private static string TodayFolio(DayClipping day) => Constitution.Domain.Today.TodayModel.Folio(day.Day);
}
