using Microsoft.Extensions.DependencyInjection;
using Foreword.Domain.Calendar;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Storyworthtelling.Maui;

namespace Foreword.Android.Pages;

/// <summary>
/// C1. The screen that replaces a JSON editor.
///
/// <para>Every row is a calendar, named as Google names it, with the column it feeds. Tapping one
/// opens <see cref="CalendarColumnPage"/>; the shared calendar's row says what it splits on, so the
/// rule is visible without opening it.</para>
///
/// <para>Work blocks live at the foot <b>under the calendars</b>, because that is what they are —
/// bands drawn beside them on the same sheet. They were hardcoded with no screen at all, so
/// changing a shift meant changing a default and building the app.</para>
/// </summary>
public partial class CalendarsPage : BinderPage
{
    private readonly CalendarSettings settings = new();
    private IReadOnlyDictionary<string, string> calendars = new Dictionary<string, string>();

    public CalendarsPage()
    {
        InitializeComponent();
        Wrap(ForewordArea.Calendar, "Calendars");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        settings.Reload();
        await LoadCalendars();
        Render();
    }

    /// <summary>
    /// Every calendar screen hangs off this one, so leaving it is when the edits made in them —
    /// columns, rules, words, shifts — are backed up to this account's personal vault. Not awaited:
    /// the backup is best-effort and never holds the screen, and it reports failure by returning
    /// false rather than throwing.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _ = IPlatformApplication.Current!.Services.GetService<VaultSync>()?.PublishCalendarAsync(CancellationToken.None);
    }

    /// <summary>
    /// Reads the list from Google. A failure is a status sentence, not a dialog — and the screen
    /// still renders, from the mapping already stored, so a phone with no signal can still be read.
    /// </summary>
    private async Task LoadCalendars()
    {
        StatusLabel.Text = "Reading your calendars…";
        try
        {
            var client = IPlatformApplication.Current!.Services.GetRequiredService<IGoogleCalendarClient>();
            calendars = await client.ListCalendars();
            StatusLabel.Text = string.Empty;
        }
        catch (Exception ex)
        {
            calendars = new Dictionary<string, string>();
            StatusLabel.Text = $"Could not read your calendars — {ex.Message}";
        }
    }

    private void Render()
    {
        Groups.Clear();

        var arrived = NewCalendars.Since(calendars.Keys, settings.SeenCalendarIds);
        var known = calendars.Keys.Where(id => !arrived.Contains(id)).ToList();

        // The columns come first: every calendar below is answered with one of them. They are this
        // account's own, set up here rather than written into the app.
        var columns = Row("Columns", string.Join(" · ", settings.Layout.Printed), string.Empty);
        Tap(columns, async () => await Navigation.PushAsync(new CalendarColumnsPage()));
        Groups.Add(GroupLabel("Your columns"));
        Groups.Add(Card([columns]));

        if (known.Count > 0)
        {
            Groups.Add(GroupLabel("Your calendars"));
            Groups.Add(Card(known.Select(CalendarRow)));
        }

        // Needs nothing pressed: somebody shares a calendar with you and it is simply here the next
        // morning, named, waiting to be given a column.
        if (arrived.Count > 0)
        {
            Groups.Add(GroupLabel("Arrived since you last looked", problem: true));
            Groups.Add(Card(arrived.Select(CalendarRow)));
        }

        Groups.Add(GroupLabel("Work blocks"));
        Groups.Add(Card(settings.Work.Shifts.Select(WorkRow).Append(OffDayRow())));
        Groups.Add(new Label
        {
            Text = "Drawn behind what sits inside it.",
            Style = PaperResources.Token<Style>("CardCaption"),
        });

        // Remembered only once the list has actually been shown — a screen that failed to load must
        // not quietly mark everything as seen.
        if (calendars.Count > 0)
        {
            settings.RememberSeen(calendars.Keys);
        }
    }

    // ---- rows ----

    private View CalendarRow(string id)
    {
        var row = Row(
            calendars.TryGetValue(id, out var name) ? name : id,
            settings.DescribeRule(id),
            settings.RuleName(id));

        Tap(row, async () => await Navigation.PushAsync(new CalendarColumnPage(id, Name(id))));
        return row;
    }

    private View WorkRow(WorkShift shift)
    {
        var row = Row(shift.Person.ToString(), shift.Describe(), shift.Enabled ? "On" : "Off");

        // Tapping turns the block off and on. The shift is kept either way, so turning it back on
        // does not mean typing the hours again.
        Tap(row, () =>
        {
            shift.Enabled = !shift.Enabled;
            settings.SaveWork();
            Render();
            return Task.CompletedTask;
        });

        return row;
    }

    private View OffDayRow() =>
        Row("A day off", "All-day: " + string.Join(", ", settings.Work.OffKeywords), string.Empty);

    // ---- furniture, from the shared kit ----

    private static Label GroupLabel(string text, bool problem = false) => new()
    {
        Text = text,
        Style = PaperResources.Token<Style>(problem ? "GroupLabelProblem" : "GroupLabel"),
    };

    private static Border Card(IEnumerable<View> rows)
    {
        var stack = new VerticalStackLayout();
        var first = true;

        foreach (var row in rows)
        {
            if (!first)
            {
                stack.Add(new BoxView
                {
                    HeightRequest = DesignTokens.Shape.HairlineDp,
                    Color = PaperResources.Colour("Hairline"),
                });
            }

            stack.Add(row);
            first = false;
        }

        return new Border { Style = PaperResources.Token<Style>("GroupCard"), Content = stack };
    }

    private static Grid Row(string title, string detail, string value)
    {
        var lines = new VerticalStackLayout
        {
            Children = { new Label { Text = title, Style = PaperResources.Token<Style>("CardRowTitle") } },
        };

        if (detail.Length > 0)
        {
            lines.Add(new Label { Text = detail, Style = PaperResources.Token<Style>("CardRowDescription") });
        }

        var grid = new Grid { Padding = new Thickness(16, 12), ColumnSpacing = DesignTokens.Space.X3 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(lines, 0);
        grid.Children.Add(lines);

        var right = new Label { Text = value, Style = PaperResources.Token<Style>("CardRowValue") };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return grid;
    }

    private static void Tap(View view, Func<Task> action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await action();
        view.GestureRecognizers.Add(tap);
    }

    private string Name(string id) => calendars.TryGetValue(id, out var name) ? name : id;
}
