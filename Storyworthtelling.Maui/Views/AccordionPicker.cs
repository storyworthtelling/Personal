using Microsoft.Maui.Controls.Shapes;

namespace Storyworthtelling.Maui.Views;

/// <summary>One choice: what it is called, and what it means if that is not obvious.</summary>
public sealed record PickerOption(string Label, string? Detail = null);

/// <summary>
/// The picker (DESIGN.md §7): the field <b>expands in place</b> — no overlay, no dialog, no bottom
/// sheet. Collapsed it reads as a sentence with the current value in it; open, the choices appear
/// beneath it and the selected one carries §8's highlighter wash.
///
/// <para>The selected row is washed rather than dimmed. A dimmed row means <i>disabled</i>, and a
/// picker whose current value looks unavailable is worse than one with nothing marked at all.</para>
/// </summary>
public class AccordionPicker : VerticalStackLayout
{
    private readonly Border frame;
    private readonly Label value;
    private readonly VerticalStackLayout options;
    private readonly List<PickerOption> choices = [];
    private int selectedIndex;
    private bool open;

    public AccordionPicker(string prefix, IEnumerable<PickerOption> items, int selected)
    {
        choices.AddRange(items);

        // Fail here, naming the picker, rather than three lines later inside Current with a bare
        // index-out-of-range that says nothing about which screen built it. A selection that does
        // not exist is a caller bug — clamping it would silently show the wrong value as chosen,
        // which on a settings screen is a lie.
        if (choices.Count == 0)
        {
            throw new ArgumentException($"The \"{prefix}\" picker was given no choices.", nameof(items));
        }

        if (selected < 0 || selected >= choices.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selected),
                selected,
                $"The \"{prefix}\" picker has {choices.Count} choices, so {selected} is not one of them.");
        }

        selectedIndex = selected;

        value = new Label
        {
            Text = Current.Label,
            Style = PaperResources.Token<Style>("CardRowTitle"),
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center,
        };

        var header = new HorizontalStackLayout
        {
            Spacing = DesignTokens.Space.X1,
            Padding = new Thickness(DesignTokens.Space.X3, 0),
            MinimumHeightRequest = DesignTokens.Shape.TouchTargetDp,
            Children =
            {
                new Label
                {
                    Text = prefix,
                    Style = PaperResources.Token<Style>("CardRowTitle"),
                    TextColor = PaperResources.Colour("InkMuted"),
                    VerticalOptions = LayoutOptions.Center,
                },
                value,
            },
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Toggle();
        header.GestureRecognizers.Add(tap);

        options = new VerticalStackLayout
        {
            BackgroundColor = PaperResources.Colour("PaperDeep"),
            IsVisible = false,
        };

        frame = new Border
        {
            BackgroundColor = PaperResources.Colour("Paper"),
            Stroke = PaperResources.Colour("Hairline"),
            StrokeThickness = DesignTokens.Shape.HairlineDp,
            StrokeShape = new RoundRectangle { CornerRadius = DesignTokens.Shape.RadiusInput },
            Padding = 0,
            Content = new VerticalStackLayout { Children = { header, options } },
        };

        Children.Add(frame);
        Rebuild();
    }

    /// <summary>
    /// Fires with the chosen index. Choosing closes the field.
    /// </summary>
    // Named Chosen rather than Selected because a visual element already has selection-ish
    // members, and an event that reads like one of them reads as the wrong thing entirely.
    public event Action<int>? Chosen;

    public PickerOption Current => choices[selectedIndex];

    public void Toggle()
    {
        open = !open;
        options.IsVisible = open;

        // The open field takes the action hue on its edge — the only thing that says the choices
        // below belong to it rather than to the page.
        frame.Stroke = open ? PaperResources.Colour("ActionMark") : PaperResources.Colour("Hairline");
    }

    private void Rebuild()
    {
        options.Children.Clear();
        for (var i = 0; i < choices.Count; i++)
        {
            options.Children.Add(BuildOption(i));
        }
    }

    private View BuildOption(int index)
    {
        var choice = choices[index];
        var chosen = index == selectedIndex;

        var label = new Label
        {
            Text = choice.Label,
            Style = PaperResources.Token<Style>(chosen ? "FieldLabel" : "CardRowTitle"),
        };

        if (chosen)
        {
            // The chosen row reads in the full ink at Body size, in the semibold face — FieldLabel
            // carries the face but not the size or the ink, so both are stated.
            label.FontSize = PaperResources.Token<double>("BodySize");
            label.TextColor = PaperResources.Colour("Ink");
        }

        var row = new VerticalStackLayout
        {
            Padding = new Thickness(DesignTokens.Space.X3, DesignTokens.Space.X2),
            MinimumHeightRequest = DesignTokens.Shape.TouchTargetDp,

            // §8 Selected: the highlighter wash of the functional hue, behind full-strength ink —
            // never a dimmed row, which would mean disabled.
            BackgroundColor = chosen ? PaperResources.Colour("ActionWash") : Colors.Transparent,
            Children = { label },
        };

        if (!string.IsNullOrWhiteSpace(choice.Detail))
        {
            row.Children.Add(new Label { Text = choice.Detail, Style = PaperResources.Token<Style>("SecondaryLabel") });
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            selectedIndex = index;
            value.Text = choice.Label;
            Rebuild();
            Toggle();
            Chosen?.Invoke(index);
        };
        row.GestureRecognizers.Add(tap);

        return row;
    }

}
