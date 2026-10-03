using Microsoft.Maui.Controls.Shapes;
using Index.Domain.Design;
using Index.Domain.Model;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>One transaction line on the post-it (mockup 3's <c>.pi-tx</c>): merchant, a relative
/// date + category, and the mono amount (a payment carries its own minus sign — the caller decides
/// the sign, this just prints the text it is given).</summary>
public sealed record PostItTransactionRow(string Merchant, string WhenAndCategory, string AmountText);

/// <summary>
/// D127/D128: the post-it a tap sticks onto an opened account — a slight tilt, a darker adhesive
/// strip along the top, a hairline edge in a deeper shade of its own tint, and no shadow (DESIGN.md
/// §5). Carries APR and Available (never the last statement) and the last 30 days of transactions,
/// newest first. Secondary text is always SOFT ink, never muted — mockup 3's own measurement: muted
/// ink falls to ~2.9:1 on a tinted post-it, below the suite's floor.
/// </summary>
public static class PostItView
{
    private const double TiltDegrees = -1.2;
    private const double CornerRadiusDp = 2;
    private const double AdhesiveStripHeightDp = 13;

    // Mockup 3's `.pi::before{background:rgba(51,50,43,.045)}` — the adhesive strip's own wash, a
    // fixed cosmetic alpha rather than a MaterialTokens entry: it is not a contrast-bearing colour
    // (nothing is read through it), which is what the registered token files are for.
    private const float AdhesiveStripAlpha = 0.045f;

    /// <param name="aprText">Null when Plaid reports no APR (a savings account): the APR column is left out.</param>
    /// <param name="onMore">Run when the trailing "n more" line is tapped; the caller lengthens the note.</param>
    public static View Build(
        string? bankPrimaryColorHex,
        string? aprText,
        string availableText,
        IReadOnlyList<PostItTransactionRow> transactions,
        int moreCount,
        View? footer = null,
        Action? onMore = null)
    {
        var tintHex = PostItTint.From(bankPrimaryColorHex);
        var edgeHex = PostItTint.Edge(tintHex);
        var softInk = PaperResources.Colour("InkSoft");

        var content = new VerticalStackLayout { Spacing = 4 };

        content.Children.Add(BuildFacts(aprText, availableText, softInk, edgeHex));

        content.Children.Add(new Label
        {
            Text = "Last 30 days",
            Style = PaperResources.Token<Style>("FieldLabel"),
            TextColor = softInk,
            Margin = new Thickness(0, 4, 0, 0),
        });

        foreach (var transaction in transactions)
        {
            content.Children.Add(BuildTransaction(transaction, softInk, edgeHex));
        }

        if (moreCount > 0)
        {
            // Mockup 3: "17 more lengthens the note in place" — a tap shows the next page (NotePaging) and the
            // note grows; there is no navigation and no dialog. Plain text, as drawn (no chevron or underline).
            var more = new Label
            {
                Text = NotePaging.MoreLabel(moreCount),
                Style = PaperResources.Token<Style>("SecondaryLabel"),
                TextColor = softInk,
                Margin = new Thickness(0, 6, 0, 0),
            };
            if (onMore is not null)
            {
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => onMore();
                more.GestureRecognizers.Add(tap);
            }

            content.Children.Add(more);
        }

        // D159: the Savings switch, for a CONNECTED account — the one thing this post-it can offer to
        // edit, appended after the transactions rather than folded into BuildFacts, since it is an
        // action, not a fact Plaid reported.
        if (footer is not null)
        {
            content.Children.Add(footer);
        }

        return Wrap(tintHex, edgeHex, content);
    }

    /// <summary>
    /// D159: a hand-entered account's post-it — its OWN fields (Balance/APR/Payment/Every/Next due) in
    /// place of the Plaid facts row and transaction list <see cref="Build"/> draws, since a manual
    /// account has neither. Shares the same tilt/adhesive-strip/tint chrome via <see cref="Wrap"/>; the
    /// caller (MainPage) builds <paramref name="fields"/> since editing a manual account's fields is
    /// page-level orchestration, the same way MainPage already owns transaction loading for
    /// <see cref="Build"/>.
    /// </summary>
    public static View BuildManual(string? bankPrimaryColorHex, View fields)
    {
        var tintHex = PostItTint.From(bankPrimaryColorHex);
        var edgeHex = PostItTint.Edge(tintHex);
        return Wrap(tintHex, edgeHex, fields);
    }

    private static View Wrap(string tintHex, string edgeHex, View content)
    {
        var body = new Grid { Padding = new Thickness(12, 16, 12, 4) };
        body.Children.Add(content);
        body.Children.Add(new BoxView
        {
            HeightRequest = AdhesiveStripHeightDp,
            VerticalOptions = LayoutOptions.Start,
            Color = Colors.Black.WithAlpha(AdhesiveStripAlpha),
        });

        return new Border
        {
            BackgroundColor = Color.FromArgb(tintHex),
            Stroke = Color.FromArgb(edgeHex),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(CornerRadiusDp) },
            Rotation = TiltDegrees,
            Shadow = null, // DESIGN.md §5 — stuck on, not floating; no shadow
            Content = body,
        };
    }

    // PostItFacts decides which facts there are: Plaid reports no APR for a savings account, and mockup 15
    // frame 1 draws only AVAILABLE for one — so no APR column, and no gap where it would have been.
    private static View BuildFacts(string? aprText, string availableText, Color softInk, string edgeHex)
    {
        var facts = new HorizontalStackLayout { Spacing = 18 };
        foreach (var fact in PostItFacts.For(aprText, availableText))
        {
            facts.Children.Add(BuildFact(fact.Label, fact.Value, softInk));
        }

        return new VerticalStackLayout
        {
            Children =
            {
                facts,
                new BoxView { HeightRequest = 1, Color = Color.FromArgb(edgeHex), Margin = new Thickness(0, 6, 0, 0) },
            },
        };
    }

    private static View BuildFact(string label, string value, Color softInk) => new VerticalStackLayout
    {
        Children =
        {
            new Label { Text = label, Style = PaperResources.Token<Style>("FieldLabel"), TextColor = softInk },
            new Label
            {
                Text = value,
                FontFamily = "Mono",
                FontSize = PaperResources.Token<double>("SecondarySize"),
                TextColor = PaperResources.Colour("Ink"),
            },
        },
    };

    private static View BuildTransaction(PostItTransactionRow row, Color softInk, string edgeHex)
    {
        var left = new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = row.Merchant, FontSize = PaperResources.Token<double>("SecondarySize") },
                new Label { Text = row.WhenAndCategory, Style = PaperResources.Token<Style>("SecondaryLabel"), TextColor = softInk },
            },
        };

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Padding = new Thickness(0, 4),
        };
        grid.Children.Add(left);
        Grid.SetColumn(left, 0);

        var amount = new Label
        {
            Text = row.AmountText,
            FontFamily = "Mono",
            FontSize = PaperResources.Token<double>("SecondarySize"),
            VerticalOptions = LayoutOptions.Center,
        };
        grid.Children.Add(amount);
        Grid.SetColumn(amount, 1);

        return new VerticalStackLayout
        {
            Children =
            {
                grid,
                new BoxView { HeightRequest = 1, Color = Color.FromArgb(edgeHex).WithAlpha(0.4f) },
            },
        };
    }
}
