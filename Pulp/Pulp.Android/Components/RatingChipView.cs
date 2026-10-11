using Android.Content;
using Android.Graphics;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Ratings;

namespace Pulp.Components;

/// <summary>
/// The rating chip (D215, amended by D276): the rating's own code, filled in its step's role — kids
/// positive leaf, family resolved grey, teen <b>reference slate</b>, mature problem rose — or, unrated,
/// a hairline outline with no fill. Every step differs by label, so it reads without colour.
/// <see cref="RatingChip"/> decides the step and role; this paints the role's Fill tier behind its Text
/// tier. Mockup 02 and 11 draw the teen chip in the caution ochre; D276 overrules the drawing, and Pulp
/// draws no Caution anywhere (D275).
/// </summary>
public sealed class RatingChipView : TextView
{
    public RatingChipView(Context context, RatingChip chip) : base(context)
    {
        ArgumentNullException.ThrowIfNull(chip);

        Text = chip.Label;
        TextSize = DesignTokens.TypeSp.Label;
        Typeface = PaperTheme.SansSemibold(context);
        SetSingleLine(true);
        var horizontal = PaperTheme.Dp(context, DesignTokens.Space.X2);
        SetPadding(horizontal, 0, horizontal, 0);

        if (chip.Outlined || chip.Role is null)
        {
            SetTextColor(PaperTheme.InkMuted);
            Background = PaperUi.Outline(context, PaperTheme.Paper, PaperTheme.Hairline, DesignTokens.Shape.RadiusButton);
            return;
        }

        var (fill, ink) = ColoursOf(chip.Role.Value);
        SetTextColor(ink);
        Background = PaperUi.Fill(context, fill, DesignTokens.Shape.RadiusButton);
    }

    /// <summary>A role's chip colours: its Fill tier behind its Text tier. No arm for Caution exists to reach.</summary>
    public static (Color Fill, Color Ink) ColoursOf(ChipRole role) => role switch
    {
        ChipRole.Positive => (Parse(DesignTokens.PositiveFill), Parse(DesignTokens.PositiveText)),
        ChipRole.Resolved => (Parse(DesignTokens.ResolvedFill), Parse(DesignTokens.ResolvedText)),
        ChipRole.Reference => (Parse(DesignTokens.ReferenceFill), Parse(DesignTokens.ReferenceText)),
        ChipRole.Problem => (Parse(DesignTokens.ProblemFill), Parse(DesignTokens.ProblemText)),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "A chip role with no colours."),
    };

    private static Color Parse(string token) => Color.ParseColor(token);
}
