using Microsoft.Maui.Controls.Shapes;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// A page that can lay a card over itself: the binder page's overlay, which spans the header, the well,
/// the strip and the bar so all of them sit back behind the haze.
/// </summary>
public interface ICardHost
{
    void ShowCard(View card);

    void HideCard();
}

/// <summary>
/// The MAUI half of <c>Codex.Ui.ActionCard</c> (audit W8.2b): a paper card centred over a PaperDeep haze,
/// which is what replaces <c>DisplayAlert</c>, <c>DisplayActionSheet</c> and <c>DisplayPromptAsync</c>.
/// Each call awaits the card the way the platform dialog was awaited, so a call site keeps its shape;
/// tapping the haze puts the card away and answers <c>null</c>, as Cancel did.
///
/// <para>What a card is not for: telling the owner something went wrong or went right. A failure is a
/// problem line where it happened, or a dog-ear notice; success is the screen changing (DESIGN.md §5,
/// §8). A card asks — for a choice, a few words — or shows something to read with what to do about it.</para>
/// </summary>
public static class PaperCards
{
    /// <summary>
    /// Asks which of several things, one tap each. Answers the option tapped, or <c>null</c> when the
    /// card was put away. <paramref name="lead"/> is drawn as the primary intent.
    /// </summary>
    public static Task<string?> Choose(
        ICardHost host, string title, string? subtitle, IReadOnlyList<string> options, string? lead = null)
    {
        var answer = new TaskCompletionSource<string?>();
        var rows = new VerticalStackLayout();
        foreach (var option in options)
        {
            var label = new Label
            {
                Text = option,
                Style = PaperResources.Token<Style>("CardRowTitle"),
                FontFamily = option == lead ? "SansSemibold" : "Sans",
                TextColor = option == lead ? PaperResources.Colour("ActionText") : PaperResources.Colour("Ink"),
                VerticalOptions = LayoutOptions.Center,
            };
            rows.Add(Rule());
            rows.Add(Row(label, () => Close(host, answer, option)));
        }

        Present(host, title, subtitle, Capped(rows), answer, padded: false);
        return answer.Task;
    }

    /// <summary>
    /// Asks for a few words. <paramref name="check"/> gets the trimmed text and returns <c>null</c> to take
    /// it or a sentence saying why not, which is shown under the field in the Problem hue while the card
    /// stays open. Answers the text taken, or <c>null</c> when the card was put away.
    /// </summary>
    public static Task<string?> AskText(
        ICardHost host,
        string title,
        string? subtitle,
        string placeholder,
        string? initial,
        string actionLabel,
        Func<string, string?>? check = null,
        Keyboard? keyboard = null,
        bool multiLine = false)
    {
        var answer = new TaskCompletionSource<string?>();
        InputView field = multiLine
            ? new Editor { Placeholder = placeholder, Text = initial, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 96 }
            : new Entry { Placeholder = placeholder, Text = initial, ReturnType = ReturnType.Done };
        if (keyboard is not null)
        {
            field.Keyboard = keyboard;
        }

        var problem = new Label { Style = PaperResources.Token<Style>("ErrorLabel"), IsVisible = false };

        void Take()
        {
            var text = (field.Text ?? string.Empty).Trim();
            var said = check?.Invoke(text) ?? (text.Length == 0 ? "Nothing was written." : null);
            if (said is not null)
            {
                problem.Text = said;
                problem.IsVisible = true;
                return;
            }

            Close(host, answer, text);
        }

        if (field is Entry entry)
        {
            entry.Completed += (_, _) => Take();
        }

        var body = new VerticalStackLayout
        {
            Spacing = DesignTokens.Space.X2,
            Children =
            {
                field,
                problem,
                Buttons(host, answer, [("Cancel", null)], (actionLabel, Take)),
            },
        };

        Present(host, title, subtitle, body, answer, padded: true);
        field.Dispatcher.Dispatch(() => field.Focus());
        return answer.Task;
    }

    /// <summary>
    /// Shows a passage to read — a log, a note — with buttons along the foot, the last being the primary.
    /// Answers the button tapped, or <c>null</c> when the card was put away.
    /// </summary>
    public static Task<string?> ShowText(ICardHost host, string title, string text, params string[] buttons)
    {
        var answer = new TaskCompletionSource<string?>();
        var passage = new Label
        {
            Text = text,
            Style = PaperResources.Token<Style>("MonoLabel"),
            FontSize = PaperResources.Token<double>("LabelSize"),
        };

        var body = new VerticalStackLayout { Spacing = DesignTokens.Space.X3, Children = { Capped(passage) } };
        if (buttons.Length > 0)
        {
            var quiet = buttons[..^1].Select(label => (label, (Action?)null)).ToArray();
            var last = buttons[^1];
            body.Add(Buttons(host, answer, quiet, (last, () => Close(host, answer, last))));
        }

        Present(host, title, null, body, answer, padded: true);
        return answer.Task;
    }

    private static void Present(
        ICardHost host, string title, string? subtitle, View body, TaskCompletionSource<string?> answer, bool padded)
    {
        var header = new VerticalStackLayout
        {
            Padding = new Thickness(DesignTokens.Space.X4, DesignTokens.Space.X4, DesignTokens.Space.X4, DesignTokens.Space.X3),
            Children =
            {
                new Label { Text = title, Style = PaperResources.Token<Style>("HeadingLabel"), LineBreakMode = LineBreakMode.TailTruncation },
            },
        };
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            header.Add(new Label { Text = subtitle, Style = PaperResources.Token<Style>("SecondaryLabel"), LineBreakMode = LineBreakMode.TailTruncation });
        }

        if (padded)
        {
            body.Margin = new Thickness(DesignTokens.Space.X4, DesignTokens.Space.X3, DesignTokens.Space.X4, DesignTokens.Space.X4);
        }

        var column = new VerticalStackLayout { Children = { header } };
        if (padded)
        {
            column.Add(Rule());
        }

        column.Add(body);

        var card = new Border
        {
            BackgroundColor = PaperResources.Colour("PaperBright"),
            Stroke = PaperResources.Colour("Hairline"),
            StrokeThickness = DesignTokens.Shape.HairlineDp,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(DesignTokens.Shape.RadiusCard) },
            Margin = new Thickness(DesignTokens.Shape.ScreenEdgeDp, 0),
            VerticalOptions = LayoutOptions.Center,
            Content = column,
        };
        // A tap on the card is not a tap outside it.
        card.GestureRecognizers.Add(new TapGestureRecognizer());

        // The haze: a dimmed PaperDeep, not a black scrim. Paper does not go dark (§5).
        var haze = new Grid
        {
            BackgroundColor = PaperResources.Colour("PaperDeep").WithAlpha(HazeAlpha),
            Children = { card },
        };
        var away = new TapGestureRecognizer();
        away.Tapped += (_, _) => Close(host, answer, null);
        haze.GestureRecognizers.Add(away);

        haze.Opacity = 0;
        host.ShowCard(haze);
        _ = haze.FadeToAsync(1, EntryMs);
    }

    private static void Close(ICardHost host, TaskCompletionSource<string?> answer, string? value)
    {
        host.HideCard();
        answer.TrySetResult(value);
    }

    private static View Row(Label label, Action tapped)
    {
        var row = new ContentView
        {
            Padding = new Thickness(DesignTokens.Space.X4, DesignTokens.Space.X3),
            MinimumHeightRequest = DesignTokens.Shape.TouchTargetDp,
            Content = label,
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => tapped();
        row.GestureRecognizers.Add(tap);
        return row;
    }

    // The quiet buttons sized to their words, then the primary taking the rest: the pinned bar's rule.
    private static View Buttons(
        ICardHost host, TaskCompletionSource<string?> answer, (string Label, Action? Press)[] quiet, (string Label, Action Press) primary)
    {
        var row = new Grid { ColumnSpacing = DesignTokens.Space.X2 };
        var column = 0;
        foreach (var (label, press) in quiet)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var button = new Button { Text = label, Style = PaperResources.Token<Style>("QuietButton") };
            var chosen = label;
            button.Clicked += (_, _) =>
            {
                if (press is null)
                {
                    Close(host, answer, chosen == "Cancel" ? null : chosen);
                }
                else
                {
                    press();
                }
            };
            row.Add(button, column++);
        }

        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var main = new Button { Text = primary.Label };
        main.Clicked += (_, _) => primary.Press();
        row.Add(main, column);
        return row;
    }

    // A long list or log scrolls inside the card rather than pushing its buttons off the screen.
    private static View Capped(View content) => new ScrollView
    {
        Content = content,
        MaximumHeightRequest = DeviceDisplay.Current.MainDisplayInfo.Height
            / Math.Max(1, DeviceDisplay.Current.MainDisplayInfo.Density) * MaxCardShare,
    };

    private static BoxView Rule() => new()
    {
        HeightRequest = DesignTokens.Shape.HairlineDp,
        Color = PaperResources.Colour("Hairline"),
    };

    // Enough to settle the page back without hiding it (the native card's 219 of 255).
    private const float HazeAlpha = 219f / 255f;

    private const double MaxCardShare = 0.6;

    // DESIGN.md §6's Standard duration: a card arrives like the dog-ear does.
    private const uint EntryMs = 200;
}
