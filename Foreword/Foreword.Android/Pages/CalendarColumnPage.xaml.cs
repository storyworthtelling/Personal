using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Storyworthtelling.Maui;

namespace Foreword.Android.Pages;

/// <summary>
/// C2. Which column one calendar feeds, as a list of choices with the current one marked.
///
/// <para>Every choice is exclusive and writes immediately — there is no Save. The marked row
/// carries §8's highlighter wash rather than being dimmed: a dimmed row means <i>disabled</i>, and
/// a setting whose current value looks unavailable is worse than one with nothing marked.</para>
/// </summary>
public partial class CalendarColumnPage : BinderPage
{
    private readonly CalendarSettings settings = new();
    private readonly string calendarId;

    public CalendarColumnPage(string calendarId, string name)
    {
        InitializeComponent();
        this.calendarId = calendarId;
        Wrap(ForewordArea.Calendar, name);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        settings.Reload();
        Render();
    }

    private void Render()
    {
        Choices.Clear();
        SplitRow.Clear();

        var current = settings.RuleName(calendarId);

        // This account's own columns, as set up on the Columns screen.
        foreach (var person in settings.Layout.People)
        {
            Add(Choices, person.ToString(), current == person.ToString(),
                () => settings.SetRule(calendarId, person, false, false, false));
        }

        // Watch is the last column — games, fixtures and anything you follow rather than attend.
        Add(Choices, PersonColumn.Watch.ToString(), current == PersonColumn.Watch.ToString(),
            () => settings.SetRule(calendarId, PersonColumn.Watch, false, false, false));

        Add(Choices, "Everyone", current == "Everyone",
            () => settings.SetRule(calendarId, null, everyone: true, false, false));

        Add(Choices, "Not printed", current == "Not printed",
            () => settings.SetRule(calendarId, null, false, notPrinted: true, false));

        Add(SplitRow, "Split by name", settings.IsSplit(calendarId),
            () => settings.SetRule(calendarId, null, false, false, split: true),
            detail: "Route each event by a word in its title, the way Family does",
            opensSplit: true);
    }

    private void Add(
        Layout host, string label, bool chosen, Action choose,
        string? detail = null, bool opensSplit = false)
    {
        if (host.Count > 0)
        {
            host.Add(new BoxView
            {
                HeightRequest = DesignTokens.Shape.HairlineDp,
                Color = PaperResources.Colour("Hairline"),
            });
        }

        var lines = new VerticalStackLayout
        {
            Children = { new Label { Text = label, Style = PaperResources.Token<Style>("CardRowTitle") } },
        };

        if (detail is not null)
        {
            lines.Add(new Label { Text = detail, Style = PaperResources.Token<Style>("CardRowDescription") });
        }

        var row = new Grid
        {
            Padding = new Thickness(16, 12),
            MinimumHeightRequest = DesignTokens.Shape.TouchTargetDp,

            // §8 Selected: the highlighter wash behind full-strength ink, never a dimmed row.
            BackgroundColor = chosen ? PaperResources.Colour("ActionWash") : Colors.Transparent,
        };
        row.Children.Add(lines);

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            choose();
            settings.Reload();

            if (opensSplit)
            {
                await Navigation.PushAsync(new CalendarSplitPage(calendarId, HeaderText));
                return;
            }

            Render();
        };
        row.GestureRecognizers.Add(tap);

        host.Add(row);
    }
}
