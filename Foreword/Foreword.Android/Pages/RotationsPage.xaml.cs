using Foreword.Domain.Prayer;
using Storyworthtelling.Maui;

namespace Foreword.Android.Pages;

/// <summary>
/// The standing lists — a missionary list, the elders — prayed through one a day.
///
/// <para>The owner's second answer on how something reaches the prayer list. A one-off request is
/// <b>written</b> on the journal page, because the page is where a pen already is; a list that has
/// to be reordered and paused is a thing to edit, and editing belongs on a screen.</para>
///
/// <para>Not in the approved mockup set — no screen had been drawn for this, because the idea
/// arrived with the answer. Built in the grammar of the screens that were drawn.</para>
/// </summary>
public partial class RotationsPage : BinderPage
{
    private readonly PrayerSettings settings;

    public RotationsPage(PrayerSettings settings)
    {
        this.settings = settings;
        InitializeComponent();
        Wrap(ForewordArea.Settings, "Lists");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // A status sentence, not a spinner (§6). This screen waits on Drive, which the other
        // settings screens do not, so the wait is named rather than animated.
        Status.Text = "Opening the archive…";
        Card.IsVisible = false;
        AddButton.IsEnabled = false;

        await settings.Load();
        Render();
    }

    private void Render()
    {
        Rows.Clear();

        var rotations = settings.Rotations;
        for (var i = 0; i < rotations.Count; i++)
        {
            AddRow(rotations[i], first: i == 0);
        }

        Status.Text = settings.Sentence();
        Card.IsVisible = rotations.Count > 0;

        // Nothing can be added to an archive that will not open — a list typed into a screen that
        // cannot save it is worse than a screen that says so.
        AddButton.IsEnabled = settings.Status is not
            (PrayerArchiveStatus.WrongPassphrase or PrayerArchiveStatus.Corrupt);
    }

    private void AddRow(PrayerRotation rotation, bool first)
    {
        if (!first)
        {
            Rows.Add(Rule());
        }

        var row = Row(rotation.Name, Describe(rotation));

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
            await Navigation.PushAsync(new RotationEditPage(settings, rotation));

        row.GestureRecognizers.Add(tap);

        Rows.Add(row);
    }

    /// <summary>
    /// How far round it has come, and whose turn it is — the two things the owner would otherwise
    /// have to count. A paused list says so instead, because a turn it is not taking is not news.
    /// </summary>
    private static string Describe(PrayerRotation rotation)
    {
        if (rotation.Paused)
        {
            return rotation.Members.Count == 1
                ? "Paused · 1 name"
                : $"Paused · {rotation.Members.Count} names";
        }

        if (rotation.Members.Count == 0)
        {
            return "No names yet";
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        return RotationPick.For(rotation, today) is { } pick
            ? $"{pick.Position} · today"
            : $"{rotation.Members.Count} names · starts later";
    }

    private async void OnAddClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new RotationEditPage(settings, rotation: null));

    private static Grid Row(string title, string detail)
    {
        var chevron = new Label
        {
            Text = "›",
            Style = PaperResources.Token<Style>("CardRowDescription"),
            VerticalOptions = LayoutOptions.Center,
        };

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
                chevron,
            },
        };

        Grid.SetColumn(chevron, 1);
        return row;
    }

    private static View Rule() => new BoxView
    {
        HeightRequest = DesignTokens.Shape.HairlineDp,
        Color = PaperResources.Colour("Hairline"),
    };
}
