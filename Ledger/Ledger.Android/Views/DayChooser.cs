using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// The "Next due" control (mockup 4/15): "Choose a day" until a day has been picked, then that day. Tapping
/// opens the system date dialog, and a day is chosen only when the owner presses OK in it.
///
/// <para>It replaces a MAUI <see cref="DatePicker"/> on both pages. A <c>DatePicker</c> always holds a date,
/// so an unset due date had to be shown as today — and then written as today by the next save (R-G15) — and
/// choosing today itself raised no <c>DateSelected</c> because nothing changed. A native dialog's OK is
/// the one signal that says "this day, on purpose". The mockup's in-place flip picker (§7) is still not
/// built: the kit has no such component, so this is the same substitute the Add form already made, now
/// reporting its choice honestly.</para>
/// </summary>
public sealed class DayChooser : ContentView
{
    private const string DayFormat = "ddd, MMM d";

    private readonly Label label = new() { Padding = new Thickness(0, 8) };
    private DateOnly? day;

    public DayChooser(DateOnly? initial)
    {
        day = initial;
        Content = label;
        Show();

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OpenDialog();
        GestureRecognizers.Add(tap);
    }

    /// <summary>The chosen day, or null while still "Choose a day".</summary>
    public DateOnly? Day => day;

    /// <summary>Raised only when the owner confirms a day in the dialog — never by opening it.</summary>
    public event Action<DateOnly>? DayChosen;

    private void Show()
    {
        if (day is { } chosen)
        {
            label.Text = chosen.ToString(DayFormat, CultureInfo.InvariantCulture);
            label.Style = null;
            label.TextColor = PaperResources.Colour("Ink");
        }
        else
        {
            label.Text = "Choose a day";
            label.Style = PaperResources.Token<Style>("SecondaryLabel");
        }
    }

    private void OpenDialog()
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        var start = day ?? DateOnly.FromDateTime(DateTime.Today);
        var dialog = new global::Android.App.DatePickerDialog(
            activity,
            (_, args) =>
            {
                var picked = DateOnly.FromDateTime(args.Date);
                day = picked;
                Show();
                DayChosen?.Invoke(picked);
            },
            start.Year, start.Month - 1, start.Day);
        dialog.Show();
    }
}
