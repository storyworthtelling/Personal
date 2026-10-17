using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Index.Domain.Design;
using Index.Domain.Model;
using Storyworthtelling.Maui;
// The index card is shared with Constitution (C-3.2.1, PC6): one token, read by both apps.
using SharedMaterials = Storyworthtelling.Paper.MaterialTokens;

namespace Index.Android.Views;

/// <summary>One line under an index card's title (mockup-02's <c>.ix-ln</c>): a left phrase (a due
/// date, a masked account number) and a right figure, both baseline-set on one of the card's ruled
/// lines. <see cref="Muted"/> is the sub-line treatment (soft ink, smaller, mono figure) mockup-02
/// uses for the masked-number/limit row under the due-date/balance row.
///
/// <para><see cref="ProblemInk"/> is mockup 7/8's "over a limit is the one alarm" (D48) — both sides
/// of the row read in the problem rose instead of their ordinary ink, the only rose Budget's cards
/// ever draw. <see cref="LeftInk"/> overrides the left side's colour outright (Budget's D49 "sent
/// back to the reMarkable" line, which reads in Snowball's own in-progress teal — a caller-supplied
/// colour rather than a new named ink, so this generic card stays free of any one screen's meaning).
/// <see cref="Meter"/> replaces the left side with a filled bar instead of text — Budget's own "spent
/// against agreed" line (mockup 7); a row carries at most one of a meter or left text.</para></summary>
public sealed record IndexCardRow(
    string Left, string Right, bool Muted = false, bool ProblemInk = false,
    Color? LeftInk = null, IndexCardMeter? Meter = null);

/// <summary>D48/D116: a category's spend against its limit — <see cref="Fraction"/> (0 to 1, clamped)
/// fills in the identity green normally, or the problem rose, full, once <see cref="Over"/>.</summary>
public sealed record IndexCardMeter(decimal Fraction, bool Over);

/// <summary>
/// D126/D129: every account on Accounts is an index card lying on the graph paper — note-card stock,
/// a red rule under the title, and pale blue rules the card's lines are written ON (never across
/// one). Built fresh per card rather than a reusable templated control: <see cref="MainPage"/> is the
/// only consumer today, and a generic "any content" card is speculative given that.
/// </summary>
public static class IndexCardView
{
    /// <summary>The card's own corner radius (mockup-02's <c>.ix{border-radius:3px}</c>) — a much
    /// tighter radius than the kit's ordinary card shape, because an index card is a flat sheet of
    /// stock, not a rounded UI panel.</summary>
    private const double CornerRadiusDp = 3;

    /// <summary><paramref name="leading"/> is a view placed in the header to the left of the title (Snowball's
    /// payoff-order number, mockup 6: "1", "2", "3" INSIDE the card), and <paramref name="titleChip"/> one
    /// placed right after it (the focus loan's "Focus" chip). <paramref name="crossedOut"/> is a paid-off
    /// loan's treatment (D12): title and figures struck through in muted ink.</summary>
    public static View Build(
        string title, IReadOnlyList<IndexCardRow> rows,
        EventHandler<TappedEventArgs>? tapped = null, string? trailingGlyph = null,
        View? leading = null, View? titleChip = null, bool crossedOut = false)
    {
        var pitch = (float)SharedMaterials.CardRulePitchDp;
        var bodyHeight = pitch * rows.Count;

        var body = new AbsoluteLayout { HeightRequest = bodyHeight };

        var ruledBackground = new GraphicsView
        {
            Drawable = new RuledLinesDrawable
            {
                RuleColour = Color.FromArgb(SharedMaterials.CardBodyRuleHex),
                PitchDp = pitch,
            },
        };
        body.Children.Add(ruledBackground);
        AbsoluteLayout.SetLayoutFlags(ruledBackground, AbsoluteLayoutFlags.SizeProportional);
        AbsoluteLayout.SetLayoutBounds(ruledBackground, new Rect(0, 0, 1, 1));

        var rowsColumn = new VerticalStackLayout();
        foreach (var row in rows)
        {
            rowsColumn.Children.Add(BuildRow(row, pitch, crossedOut));
        }
        body.Children.Add(rowsColumn);
        AbsoluteLayout.SetLayoutFlags(rowsColumn, AbsoluteLayoutFlags.WidthProportional);
        AbsoluteLayout.SetLayoutBounds(rowsColumn, new Rect(0, 0, 1, AbsoluteLayout.AutoSize));

        var headerRow = new Grid { Padding = new Thickness(0, 0, 0, 4) };
        var titleLabel = new Label
        {
            Text = title,
            Style = PaperResources.Token<Style>("HeadingLabel"),
            VerticalOptions = LayoutOptions.Center,
        };
        if (crossedOut)
        {
            titleLabel.TextDecorations = TextDecorations.Strikethrough;
            titleLabel.TextColor = PaperResources.Colour("InkMuted");
        }

        // Left to right: [leading] title [chip] ... [chevron]. With a chip the title takes only its own
        // width so the chip sits beside it; without one it fills the row, as it always has.
        void Place(View view, GridLength width)
        {
            headerRow.ColumnDefinitions.Add(new ColumnDefinition(width));
            headerRow.Children.Add(view);
            Grid.SetColumn(view, headerRow.ColumnDefinitions.Count - 1);
        }

        if (leading is not null)
        {
            Place(leading, GridLength.Auto);
        }
        Place(titleLabel, titleChip is null ? GridLength.Star : GridLength.Auto);
        if (titleChip is not null)
        {
            Place(titleChip, GridLength.Auto);
            headerRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        if (!string.IsNullOrEmpty(trailingGlyph))
        {
            Place(BuildTrailingMark(trailingGlyph), GridLength.Auto);
        }

        var header = new VerticalStackLayout
        {
            Children =
            {
                headerRow,
                new BoxView { HeightRequest = 1.5, Color = Color.FromArgb(SharedMaterials.CardHeadRuleHex) },
            },
        };

        var card = new Border
        {
            BackgroundColor = Color.FromArgb(SharedMaterials.NoteCardHex),
            Stroke = PaperResources.Colour("Hairline"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(CornerRadiusDp) },
            Padding = new Thickness(12, 7, 12, 2),
            Shadow = null, // DESIGN.md §5 — a hairline edge, never a shadow
            Content = new VerticalStackLayout { Children = { header, body } },
        };

        if (tapped is not null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += tapped;
            card.GestureRecognizers.Add(tap);
        }

        return card;
    }

    // A chevron token (CardChevron.Down / Up, or the two old text triangles) is drawn as the 14 x 14 stroke the
    // mockups' `.ix-cv` is, in the muted ink; anything else a caller passes stays the plain text it is.
    private static View BuildTrailingMark(string trailingGlyph)
    {
        if (CardChevron.Resolve(trailingGlyph) is { } chevron)
        {
            return new GraphicsView
            {
                Drawable = new ChevronDrawable(chevron, PaperResources.Colour("InkMuted")),
                WidthRequest = CardChevron.SizeDp,
                HeightRequest = CardChevron.SizeDp,
                VerticalOptions = LayoutOptions.Center,
            };
        }

        return new Label
        {
            Text = trailingGlyph,
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            VerticalOptions = LayoutOptions.Center,
        };
    }

    private static View BuildRow(IndexCardRow row, float pitch, bool crossedOut)
    {
        var grid = new Grid
        {
            HeightRequest = pitch,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };

        View leftView;
        if (row.Meter is { } meter)
        {
            leftView = BuildMeter(meter);
        }
        else
        {
            var left = new Label
            {
                Text = row.Left,
                Style = PaperResources.Token<Style>("SecondaryLabel"),
                // Soft ink, not muted: the card's secondary text sits on pale blue rules, and mockup 2 measures
                // soft ink at 4.34:1 where a rule crosses a glyph, muted at 2.82:1 — below the floor.
                TextColor = PaperResources.Colour("InkSoft"),
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
            };
            if (crossedOut)
            {
                left.TextDecorations = TextDecorations.Strikethrough;
                left.TextColor = PaperResources.Colour("InkMuted");
            }
            else if (row.ProblemInk)
            {
                left.TextColor = PaperResources.Colour("ProblemMark");
            }
            else if (row.LeftInk is { } leftInk)
            {
                left.TextColor = leftInk;
            }
            leftView = left;
        }
        grid.Children.Add(leftView);
        Grid.SetColumn(leftView, 0);

        var right = new Label
        {
            Text = row.Right,
            FontFamily = "Mono",
            FontSize = row.Muted
                ? PaperResources.Token<double>("SecondarySize")
                : PaperResources.Token<double>("BodySize"),
            TextColor = crossedOut
                ? PaperResources.Colour("InkMuted")
                : row.ProblemInk
                    ? PaperResources.Colour("ProblemMark")
                    : row.Muted ? PaperResources.Colour("InkSoft") : PaperResources.Colour("Ink"),
            VerticalOptions = LayoutOptions.Center,
        };
        if (crossedOut)
        {
            right.TextDecorations = TextDecorations.Strikethrough;
        }
        grid.Children.Add(right);
        Grid.SetColumn(right, 1);

        return grid;
    }

    // D48/D116, mockup 7's ".ixm"/".ixm i": a pale track (PaperShade — mockup's own #E9E1CF) filled
    // to Fraction in the identity green (Accounts' own mark hex, the sanctioned Positive substitute
    // per D116 — the same hue BuildSavingsSwitchFooter's toggle already reads off LedgerAreas for the
    // same reason), or, once over, the problem rose, full.
    private const double MeterHeightDp = 5;
    private const double MeterCornerRadiusDp = 2.5;

    private static View BuildMeter(IndexCardMeter meter)
    {
        var fraction = (double)Math.Clamp(meter.Over ? 1m : meter.Fraction, 0m, 1m);
        var fillColour = meter.Over
            ? PaperResources.Colour("ProblemMark")
            : Index.Android.Pages.LedgerAreas.All[(int)Index.Android.Pages.LedgerArea.Accounts].Hue;

        var fill = new Border
        {
            BackgroundColor = fillColour,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(MeterCornerRadiusDp) },
            Stroke = Colors.Transparent,
            VerticalOptions = LayoutOptions.Fill,
        };
        Grid.SetColumn(fill, 0);

        var columns = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(fraction, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1 - fraction, GridUnitType.Star)),
            },
            Children = { fill },
        };

        return new Border
        {
            BackgroundColor = PaperResources.Colour("PaperShade"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(MeterCornerRadiusDp) },
            Stroke = Colors.Transparent,
            HeightRequest = MeterHeightDp,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Content = columns,
        };
    }
}
