using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.MealsTab;

/// <summary>
/// A card picked out of its pile (book p.13): laid on top, tilted, with its history in the brief's words, who loved it, its
/// facts, and Cook this soon. Success is the card itself changing to "Planned for next week" (section 7): there is no toast and
/// nothing to dismiss. No tape. A tap on the card, or on the pile's gap, puts it back.
/// </summary>
internal sealed class PickedCardView : LinearLayout
{
    private const float Tilt = -1.5f;

    public PickedCardView(Context context, CardFace face, Action onCook, Action onPutBack) : base(context)
    {
        Orientation = Orientation.Vertical;
        Background = PaperUi.Outline(context, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X4);
        SetPadding(pad, pad, pad, pad);
        Rotation = Tilt;
        Click += (_, _) => onPutBack();

        AddView(PaperUi.Label(context, face.Header));
        AddView(PaperUi.Heading(context, face.Title));
        AddView(PaperUi.Body(context, face.Headline), Spaced(context));
        AddLine(context, face.Counted);
        AddLine(context, face.Reactions);
        AddLine(context, face.Facts);

        if (CookSoon.OffersAButton(face.Cook))
        {
            if (face.Cook == CookSoonState.Unavailable)
            {
                AddView(PaperUi.Secondary(context, face.CookWords), Spaced(context));
            }

            AddView(PaperUi.SecondaryButton(context, CookSoon.OfferWords, onCook), Spaced(context));
        }
        else
        {
            AddView(PaperUi.Body(context, face.CookWords), Spaced(context));
        }

        AddView(PaperUi.Secondary(context, face.PutBackHint), Spaced(context));
    }

    private void AddLine(Context context, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            AddView(PaperUi.Secondary(context, text));
        }
    }

    private static LayoutParams Spaced(Context context)
    {
        var parameters = new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        parameters.TopMargin = PaperTheme.Dp(context, DesignTokens.Space.X2);
        return parameters;
    }
}
