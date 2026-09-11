using Microsoft.Extensions.DependencyInjection;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Tasks;
using Storyworthtelling.Maui;

namespace Foreword.Android.Pages;

/// <summary>
/// T3. Everything that could print, in one list, with the kind marked.
///
/// <para>Foreword's own rules and Google's tasks are merged before they get here, so "one list, not
/// three" is a fact about the data rather than an arrangement on a screen.</para>
///
/// <para>There is no tick. Only the reMarkable closes a task, which is why one closed in Google's
/// own app is shown struck through rather than removed — the owner can see <i>why</i> it will not
/// print instead of wondering where it went.</para>
/// </summary>
public partial class TasksPage : ContentPage
{
    private readonly ITaskStore store = new TaskStore();

    public TasksPage() => InitializeComponent();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Render();
    }

    private async Task Render()
    {
        Rows.Clear();

        var away = store.Away;
        AwayLabel.IsVisible = away.Mornings > 0;
        AwayLabel.Text = away.Mornings > 0
            ? $"Recurring tasks {(away.PauseRecurring ? "paused" : "still printing")} "
              + $"{away.From:d MMMM}–{away.Until:d MMMM}, while away."
            : string.Empty;

        var tasks = GoogleTaskMirror.Merge(store.All(), await ReadGoogle());

        EmptyLabel.IsVisible = tasks.Count == 0;

        foreach (var task in tasks)
        {
            AddRow(task, Rows.Count > 0);
        }
    }

    /// <summary>
    /// Google's half, best-effort. Foreword's own rules are on the phone and must still be listed
    /// when the network is not — a screen that showed nothing because a sync failed would look like
    /// a screen that had lost everything.
    /// </summary>
    private async Task<IReadOnlyList<Domain.Models.TodoItem>> ReadGoogle()
    {
        try
        {
            return await IPlatformApplication.Current!.Services
                .GetRequiredService<IGoogleTasksClient>().GetMyTasks();
        }
        catch
        {
            return [];
        }
    }

    private void AddRow(ForewordTask task, bool needsRule)
    {
        if (needsRule)
        {
            Rows.Add(new BoxView
            {
                HeightRequest = DesignTokens.Shape.HairlineDp,
                Color = PaperResources.Colour("Hairline"),
            });
        }

        var title = new Label
        {
            Text = task.Title,
            Style = PaperResources.Token<Style>("CardRowTitle"),
        };

        // Struck through rather than removed: the owner can see why it will not print.
        if (task.CompletedInGoogle)
        {
            title.TextDecorations = TextDecorations.Strikethrough;
            title.TextColor = PaperResources.Colour("InkMuted");
        }

        var row = new Grid
        {
            Padding = new Thickness(16, 12),
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            MinimumHeightRequest = DesignTokens.Shape.TouchTargetDp,
            Children =
            {
                new VerticalStackLayout
                {
                    Children =
                    {
                        title,
                        new Label
                        {
                            Text = Describe(task),
                            Style = PaperResources.Token<Style>("CardRowDescription"),
                        },
                    },
                },
                Chip(task),
            },
        };

        Grid.SetColumn(row.Children[1], 1);

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Navigation.PushAsync(new TaskEditPage(task));
        row.GestureRecognizers.Add(tap);

        Rows.Add(row);
    }

    /// <summary>
    /// The rule in words, under the title — the same summary the edit form renders, so the list and
    /// the form can never describe one task two ways.
    /// </summary>
    private static string Describe(ForewordTask task)
    {
        if (task.CompletedInGoogle)
        {
            return "Done in Google Tasks — will not print";
        }

        if (task.Unparsed)
        {
            return "Not read yet — kept as you wrote it";
        }

        return string.Join(" · ", TaskSummary.Rows(task).Select(row => row.Value));
    }

    private static View Chip(ForewordTask task) => new Border
    {
        Style = PaperResources.Token<Style>("PhaseChip"),
        VerticalOptions = LayoutOptions.Center,
        Content = new Label
        {
            Text = TaskBands.Kind(task),
            Style = PaperResources.Token<Style>("ChipLabel"),
        },
    };

    private async void OnAddClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new NewTaskPage());

    private async void OnAwayClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new AwayPage());
}
