using Foreword.Domain.Brief;
using Storyworthtelling.Maui;

namespace Foreword.Android.Pages;

/// <summary>
/// T5. Where the brief comes from.
///
/// <para>The counts are steppers because they are the one number the owner actually tunes, and each
/// feed carries its own cap rather than a share of an overall one — so a noisy feed cannot crowd out
/// a quiet one and a quiet morning still prints everything.</para>
///
/// <para>Nothing on this screen removes a feed except the owner. A feed that keeps refusing says so
/// and stays, because one that disappeared on its own would be indistinguishable from one they
/// forgot they added.</para>
/// </summary>
public partial class SourcesPage : ContentPage
{
    private readonly BriefSettings settings = new();

    public SourcesPage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        settings.Reload();
        Render();
    }

    private void Render()
    {
        Paper.Clear();
        Feeds.Clear();

        AddStepper(Paper, "New York Times — news", "Top stories",
            settings.NewsCount, value => settings.NewsCount = value, first: true);
        AddStepper(Paper, "New York Times — opinion", "Top opinion",
            settings.OpinionCount, value => settings.OpinionCount = value, first: false);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var feeds = settings.Feeds;

        for (var i = 0; i < feeds.Count; i++)
        {
            AddFeedRow(feeds[i], today, first: i == 0);
        }

        FeedsCaption.Text = feeds.Count == 0
            ? "No feeds yet. The paper alone is a brief."
            : "A feed that keeps refusing stays on this list, marked, until you remove it.";
    }

    private void AddStepper(
        Layout into, string title, string detail, int value, Action<int> commit, bool first)
    {
        if (!first)
        {
            into.Add(Rule());
        }

        var reading = new Label
        {
            Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Style = PaperResources.Token<Style>("CardRowTitle"),
            VerticalOptions = LayoutOptions.Center,
        };

        var stepper = new Stepper { Minimum = 0, Maximum = 12, Increment = 1, Value = value };
        stepper.ValueChanged += (_, e) =>
        {
            var count = (int)e.NewValue;
            reading.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            commit(count);
        };

        into.Add(Row(title, detail, new HorizontalStackLayout
        {
            Spacing = DesignTokens.Space.X2,
            VerticalOptions = LayoutOptions.Center,
            Children = { stepper, reading },
        }));
    }

    private void AddFeedRow(Feed feed, DateOnly today, bool first)
    {
        if (!first)
        {
            Feeds.Add(Rule());
        }

        // A proposal is offered, not counted: a calendar named after a team is a good guess, not a
        // decision, so the row's action is Follow rather than a number to tune.
        View trailing = feed.IsActive
            ? Count(feed)
            : Follow(feed);

        Feeds.Add(Row(feed.Name, feed.Status(today), trailing));
    }

    private View Count(Feed feed)
    {
        var reading = new Label
        {
            Text = feed.Cap.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Style = PaperResources.Token<Style>("CardRowTitle"),
            VerticalOptions = LayoutOptions.Center,
        };

        var stepper = new Stepper { Minimum = 0, Maximum = 12, Increment = 1, Value = feed.Cap };
        stepper.ValueChanged += (_, e) =>
        {
            var cap = (int)e.NewValue;
            reading.Text = cap.ToString(System.Globalization.CultureInfo.InvariantCulture);
            settings.SetCap(feed.Url, cap);
        };

        return new HorizontalStackLayout
        {
            Spacing = DesignTokens.Space.X2,
            VerticalOptions = LayoutOptions.Center,
            Children = { stepper, reading },
        };
    }

    private View Follow(Feed feed)
    {
        var button = new Button
        {
            Text = "Follow",
            Style = PaperResources.Token<Style>("SecondaryButton"),
            VerticalOptions = LayoutOptions.Center,
        };

        button.Clicked += (_, _) =>
        {
            settings.Follow(feed.Url);
            Render();
        };

        return button;
    }

    private static View Row(string title, string detail, View trailing)
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 12),
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = DesignTokens.Space.X3,
            MinimumHeightRequest = DesignTokens.Shape.ListRowMinHeightDp,
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
                trailing,
            },
        };

        Grid.SetColumn(trailing, 1);
        return row;
    }

    private static View Rule() => new BoxView
    {
        HeightRequest = DesignTokens.Shape.HairlineDp,
        Color = PaperResources.Colour("Hairline"),
    };

    /// <summary>
    /// Adding a feed is a URL and nothing else — the cap defaults and the name comes from the feed
    /// itself on the first fetch, because asking for a title the feed already carries is asking the
    /// owner to do the app's typing.
    /// </summary>
    private async void OnAddFeedClicked(object sender, EventArgs e)
    {
        var url = await DisplayPromptAsync("Add a feed", "Its address", placeholder: "https://…");

        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        settings.AddFeed(url.Trim());
        Render();
    }
}
