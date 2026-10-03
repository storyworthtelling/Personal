using Storyworthtelling.Maui.State;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// The §8 segmented control: named options in one hairline-bordered row, the picked one filled in
/// the action hue.
///
/// <para>A segmented control rather than a switch wherever the choice has names. "Print" and "Pause"
/// say what will happen; a switch says on or off and leaves the owner remembering which way round it
/// was written — and on a settings screen read once a month, that is a setting they will get wrong.</para>
///
/// <para>There is no Save behind it. Each tap commits, which is why <see cref="SegmentedChoice"/>
/// reports whether anything actually changed: tapping the option already picked must not write the
/// same value again.</para>
///
/// <para>By default the control is as wide as its words. <c>fillWidth</c> gives every segment an equal share
/// of the row instead (mockup 4's <c>.seg span{flex:1}</c>), for a form whose boxes all run the full width.
/// It is additive and off, so existing callers are unchanged.</para>
/// </summary>
public sealed class PaperSegmented : Border
{
    private readonly SegmentedChoice choice;
    private readonly Grid row = new();
    private readonly List<Label> labels = [];
    private readonly bool fillWidth;

    public PaperSegmented(IEnumerable<string> options, string? picked = null, bool fillWidth = false)
    {
        this.fillWidth = fillWidth;
        choice = new SegmentedChoice(options, picked);

        Stroke = PaperResources.Colour("Hairline");
        StrokeThickness = DesignTokens.Shape.HairlineDp;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
        {
            CornerRadius = DesignTokens.Shape.RadiusCard,
        };
        Padding = 0;
        HorizontalOptions = fillWidth ? LayoutOptions.Fill : LayoutOptions.Start;
        Content = row;

        Build();
    }

    /// <summary>Raised only when the picked option actually changes.</summary>
    public event Action<string>? Picked;

    public string Value => choice.Picked;

    private void Build()
    {
        foreach (var option in choice.Options)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(fillWidth ? GridLength.Star : GridLength.Auto));

            var label = new Label
            {
                Text = option,
                Style = PaperResources.Token<Style>("SegmentLabel"),
                // A filled-width segment is wider than its word: the word sits in the middle of it.
                HorizontalTextAlignment = fillWidth ? TextAlignment.Center : TextAlignment.Start,
            };

            var cell = new Grid
            {
                // The whole segment is the target, not the word: a §7 touch target is 48dp and
                // "Pause" at Label size is not.
                MinimumHeightRequest = DesignTokens.Shape.TouchTargetDp,
                Padding = new Thickness(DesignTokens.Space.X3, DesignTokens.Space.X2),
                Children = { label },
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                if (choice.Pick(option))
                {
                    Paint();
                    Picked?.Invoke(option);
                }
            };
            cell.GestureRecognizers.Add(tap);

            Grid.SetColumn(cell, labels.Count);
            row.Children.Add(cell);
            labels.Add(label);
        }

        Paint();
    }

    private void Paint()
    {
        for (var i = 0; i < labels.Count; i++)
        {
            var isPicked = choice.IsPicked(choice.Options[i]);

            // The picked segment carries the wash and the full ink; the rest stay quiet. Not dimmed:
            // a dimmed segment means DISABLED, and every one of these is available.
            ((Grid)row.Children[i]).BackgroundColor =
                isPicked ? PaperResources.Colour("ActionWash") : Colors.Transparent;
            labels[i].TextColor = PaperResources.Colour(isPicked ? "Ink" : "InkMuted");

            // The FAMILY changes, not FontAttributes. Source Sans 3 ships Regular and SemiBold as
            // separate faces and has no bold axis — asking the platform to embolden Regular gets a
            // synthesised weight that is not the one in the type ladder.
            labels[i].FontFamily = isPicked ? "SansSemibold" : "Sans";
        }
    }
}
