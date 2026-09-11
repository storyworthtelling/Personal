using Foreword.Domain.Interfaces;
using Foreword.Domain.Tasks;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;

namespace Foreword.Android.Pages;

/// <summary>
/// A1. Two dates and three toggles.
///
/// <para>A recurring task that fires while the owner is away prints on a page nobody is reading and
/// comes back as a backlog they did not fail to do. So a paused task is <b>not late and does not
/// pile up</b> — the mornings pass and the one after is ordinary.</para>
///
/// <para>What is not paused matters as much, and the defaults say so: the morning still goes out,
/// because a holiday is exactly when a day sheet earns its place.</para>
/// </summary>
public partial class AwayPage : ContentPage
{
    private readonly ITaskStore store = new TaskStore();

    public AwayPage()
    {
        InitializeComponent();

        var away = store.Away;
        var today = DateTime.Today;

        FromPicker.Date = away.From?.ToDateTime(TimeOnly.MinValue) ?? today;
        UntilPicker.Date = away.Until?.ToDateTime(TimeOnly.MinValue) ?? today.AddDays(7);

        RenderToggles();
        RenderMornings();
    }

    private void RenderToggles()
    {
        Toggles.Clear();

        var away = store.Away;

        AddToggle("Recurring tasks", "Bins, vitamins, the lawn",
            away.PauseRecurring,
            paused => Commit(store.Away with { PauseRecurring = paused }));

        AddToggle("Work blocks", "Both of you",
            away.PauseWorkBlocks,
            paused => Commit(store.Away with { PauseWorkBlocks = paused }));

        // Framed the other way round, because the answer here is Yes rather than Pause: the morning
        // going out is the default and the useful thing, not the exception.
        AddToggle("The morning still goes out", "Calendar, weather, the brief and prayer",
            !away.StillDeliver,
            paused => Commit(store.Away with { StillDeliver = !paused }),
            options: ["Yes", "No"]);
    }

    private void AddToggle(
        string title, string detail, bool second, Action<bool> commit, string[]? options = null)
    {
        var labels = options ?? ["Print", "Pause"];

        if (Toggles.Count > 0)
        {
            Toggles.Add(new BoxView
            {
                HeightRequest = DesignTokens.Shape.HairlineDp,
                Color = PaperResources.Colour("Hairline"),
            });
        }

        var segmented = new PaperSegmented(labels, labels[second ? 1 : 0]);
        segmented.Picked += picked => commit(picked == labels[1]);

        Toggles.Add(new Grid
        {
            Padding = new Thickness(16, 12),
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = DesignTokens.Space.X3,
            Children =
            {
                new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = title, Style = PaperResources.Token<Style>("CardRowTitle") },
                        new Label { Text = detail, Style = PaperResources.Token<Style>("CardRowDescription") },
                    },
                },
                Second(segmented),
            },
        });
    }

    private static View Second(View control)
    {
        Grid.SetColumn(control, 1);
        control.VerticalOptions = LayoutOptions.Center;
        return control;
    }

    private void OnDatesChanged(object sender, DateChangedEventArgs e) =>
        Commit(store.Away with
        {
            From = DateOnly.FromDateTime(FromPicker.Date),
            Until = DateOnly.FromDateTime(UntilPicker.Date),
        });

    /// <summary>
    /// Clearing the dates rather than a separate "away" flag, because away IS the date range — a
    /// mode and a range could disagree, and then nothing on the screen would say which one the
    /// morning was reading.
    /// </summary>
    private void OnClearClicked(object sender, EventArgs e) =>
        Commit(store.Away with { From = null, Until = null });

    private void Commit(AwayPause away)
    {
        store.Away = away;
        RenderMornings();
    }

    private void RenderMornings()
    {
        var mornings = store.Away.Mornings;

        MorningsLabel.Text = mornings switch
        {
            0 => "No trip set — nothing is paused.",
            1 => "One morning.",
            _ => $"{mornings} mornings.",
        };
    }
}
