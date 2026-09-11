using Microsoft.Extensions.DependencyInjection;
using Foreword.Domain.Tasks;
using Foreword.Infrastructure.Tasks;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;

namespace Foreword.Android.Pages;

/// <summary>
/// T7 and T8 — one screen, rendering only the rows the kind uses.
///
/// <para>A form that grows to the rule is fine; a form that shows every rule's controls to every
/// task is not. "Vitamins, every day" has no window to place itself in, so it has no window row, no
/// preferred day, no dry-hours stepper and no fallback — three rows, because that is all it has to
/// say.</para>
///
/// <para>There is no Save (D35). Every control commits as it changes, so leaving by the back arrow
/// never loses anything — and never asks.</para>
/// </summary>
public partial class TaskEditPage : ContentPage
{
    private readonly bool unsaved;
    private ForewordTask task;

    /// <param name="unsaved">
    /// True when this is a freshly parsed rule reached from the read-back's Edit button. It has
    /// never been stored, so the commit-as-you-type rule would otherwise save a rule the owner has
    /// not accepted — which is the one thing T4 exists to prevent.
    /// </param>
    public TaskEditPage(ForewordTask task, bool unsaved = false)
    {
        InitializeComponent();
        this.task = task;
        this.unsaved = unsaved;

        TitleLabel.Text = task.Title;
        Render();
    }

    private void Render()
    {
        Fields.Clear();

        AddRow("Task", new Entry
        {
            Text = task.Title,
            Style = PaperResources.Token<Style>("MonoEntry"),
        }.Committing(value => Commit(task with { Title = value })));

        switch (task.Kind)
        {
            case TaskKind.Rule:
                RenderRule();
                break;
            case TaskKind.Smart:
                RenderSmart();
                break;
            default:
                RenderOnce();
                break;
        }

        FootLabel.Text = Foot();
        RetypeButton.IsVisible = task.Kind != TaskKind.Once;
    }

    private void RenderRule()
    {
        AddRow("How often", Segmented(["Daily", "Weekly"], task.Repeat.ToString(), picked =>
            Commit(task with { Repeat = picked == "Weekly" ? Repeat.Weekly : Repeat.Daily })));

        if (task.Repeat == Repeat.Weekly)
        {
            AddRow("On", Days(task.On, day => Commit(task with { On = day })));
        }

        AddRow("Season", SeasonRow());
    }

    private void RenderSmart()
    {
        AddRow("Window", Segmented(
            ["Any day", "The weekend", "Weekdays"], WindowName(task.Window),
            picked => Commit(task with { Window = WindowOf(picked) })));

        AddRow("Prefers", Days(task.Prefers, day => Commit(task with { Prefers = day })));

        if (task.DryHoursNeeded is { } hours)
        {
            AddRow("Needs", Stepper(hours, value => Commit(task with { DryHoursNeeded = value })));
        }

        AddRow("Season", SeasonRow());

        // Not editable: it is what the engine does, stated so the owner knows when the choice is
        // made rather than discovering it on a Saturday morning.
        AddRow("Decided", Reading("When the window opens"));

        AddRow(task.DryHoursNeeded is null ? "If no day qualifies" : "If no dry day",
            Segmented(
                ["Print anyway", "Skip the week"],
                task.IfNoDay == NoDayFallback.SkipTheWeek ? "Skip the week" : "Print anyway",
                picked => Commit(task with
                {
                    IfNoDay = picked == "Skip the week" ? NoDayFallback.SkipTheWeek : NoDayFallback.PrintAnyway,
                })));
    }

    private void RenderOnce()
    {
        AddRow("Due", new DatePicker
        {
            Date = (task.Due ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue),
            HorizontalOptions = LayoutOptions.End,
        }.Committing(date => Commit(task with { Due = DateOnly.FromDateTime(date) })));
    }

    private View SeasonRow() => Segmented(
        ["All year", "April – October", "November – February"],
        task.Season.IsAllYear ? "All year"
            : task.Season.FromMonth == 4 ? "April – October"
            : task.Season.FromMonth == 11 ? "November – February"
            : "All year",
        picked => Commit(task with
        {
            Season = picked switch
            {
                "April – October" => new SeasonRange(4, 10),
                "November – February" => new SeasonRange(11, 2),
                _ => SeasonRange.AllYear,
            },
        }));

    /// <summary>
    /// What the screen says under the form. A once task says where it lives, because its title and
    /// date are Google's and an owner who did not know that would wonder why an edit here changed
    /// their phone's task list.
    /// </summary>
    private string Foot() => task.Kind switch
    {
        TaskKind.Once when task.GoogleTaskId is { Length: > 0 } =>
            "This one lives in Google Tasks. Changes here are written back there.",
        TaskKind.Rule when task.Repeat == Repeat.Daily =>
            "Printed every morning. Paused only while you are away.",
        _ => string.Empty,
    };

    private void Commit(ForewordTask updated)
    {
        task = updated;
        TitleLabel.Text = updated.Title;

        // An unaccepted rule is not stored. The read-back screen is where it becomes real, and
        // committing here would save something the owner has not agreed to.
        if (unsaved)
        {
            return;
        }

        _ = Save();
    }

    private async Task Save()
    {
        var result = await IPlatformApplication.Current!.Services
            .GetRequiredService<TaskEditor>().Save(task);

        StatusLabel.IsVisible = !result.Saved;
        StatusLabel.Text = result.Message;
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        var result = await IPlatformApplication.Current!.Services
            .GetRequiredService<TaskEditor>().Delete(task);

        if (!result.Saved)
        {
            StatusLabel.IsVisible = true;
            StatusLabel.Text = result.Message;
            return;
        }

        await Navigation.PopAsync();
    }

    private async void OnRetypeClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new NewTaskPage());

    // ---- the controls ----

    private void AddRow(string label, View control)
    {
        if (Fields.Count > 0)
        {
            Fields.Add(new BoxView
            {
                HeightRequest = DesignTokens.Shape.HairlineDp,
                Color = PaperResources.Colour("Hairline"),
            });
        }

        Fields.Add(new VerticalStackLayout
        {
            Padding = new Thickness(16, 12),
            Spacing = DesignTokens.Space.X2,
            Children =
            {
                new Label { Text = label, Style = PaperResources.Token<Style>("CardRowDescription") },
                control,
            },
        });
    }

    private static View Reading(string value) => new Label
    {
        Text = value,
        Style = PaperResources.Token<Style>("CardRowTitle"),
    };

    private static View Segmented(IEnumerable<string> options, string picked, Action<string> commit)
    {
        var control = new PaperSegmented(options, picked);
        control.Picked += commit;
        return control;
    }

    /// <summary>
    /// The seven days as one row of circles. A picker rather than a list because the answer is one
    /// of seven and a dropdown would hide six of them behind a tap.
    /// </summary>
    private static View Days(DayOfWeek? picked, Action<DayOfWeek> commit)
    {
        var row = new HorizontalStackLayout { Spacing = DesignTokens.Space.X1 };

        foreach (var day in (DayOfWeek[])[DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday])
        {
            var isPicked = day == picked;

            var circle = new Border
            {
                WidthRequest = DesignTokens.Shape.TouchTargetDp,
                HeightRequest = DesignTokens.Shape.TouchTargetDp,
                Stroke = PaperResources.Colour("Hairline"),
                StrokeThickness = DesignTokens.Shape.HairlineDp,
                BackgroundColor = isPicked
                    ? PaperResources.Colour("ActionWash")
                    : Colors.Transparent,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                {
                    CornerRadius = DesignTokens.Shape.TouchTargetDp / 2,
                },
                Content = new Label
                {
                    // One letter, which is all that fits — and enough, because the row is in order.
                    Text = day.ToString()[..1],
                    Style = PaperResources.Token<Style>(isPicked ? "ChipLabelActive" : "ChipLabel"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                },
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => commit(day);
            circle.GestureRecognizers.Add(tap);

            row.Add(circle);
        }

        return row;
    }

    private static View Stepper(int value, Action<int> commit)
    {
        var reading = new Label
        {
            Text = $"{value} dry hours",
            Style = PaperResources.Token<Style>("CardRowTitle"),
            VerticalOptions = LayoutOptions.Center,
        };

        var stepper = new Stepper
        {
            Minimum = 1,
            Maximum = 12,
            Increment = 1,
            Value = value,
        };

        stepper.ValueChanged += (_, e) =>
        {
            var hours = (int)e.NewValue;
            reading.Text = hours == 1 ? "1 dry hour" : $"{hours} dry hours";
            commit(hours);
        };

        return new HorizontalStackLayout
        {
            Spacing = DesignTokens.Space.X3,
            Children = { stepper, reading },
        };
    }

    private static string WindowName(TaskWindow window) => window switch
    {
        TaskWindow.Weekend => "The weekend",
        TaskWindow.Weekdays => "Weekdays",
        _ => "Any day",
    };

    private static TaskWindow WindowOf(string name) => name switch
    {
        "The weekend" => TaskWindow.Weekend,
        "Weekdays" => TaskWindow.Weekdays,
        _ => TaskWindow.AnyDay,
    };
}

/// <summary>
/// Commit-as-you-change, in one place. D35: there is no Save on any of these screens, and a value
/// the owner typed and then navigated away from is exactly the one they meant to set.
/// </summary>
internal static class CommittingControls
{
    public static Entry Committing(this Entry entry, Action<string> commit)
    {
        entry.TextChanged += (_, e) => commit((e.NewTextValue ?? string.Empty).Trim());
        return entry;
    }

    public static DatePicker Committing(this DatePicker picker, Action<DateTime> commit)
    {
        picker.DateSelected += (_, e) => commit(e.NewDate);
        return picker;
    }
}
