using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Plate;
using Codex.Ui;

namespace Plate;

/// <summary>
/// "It is someone else": a paper card over the editor with a name field and the address-book people it
/// narrows to (<see cref="FacePicker"/>). Choosing one is the correction; tapping outside leaves the face
/// as it was. It is a picker, not a confirm dialog: the tap that chooses is the act.
/// </summary>
internal static class FaceNameChooser
{
    private const string HostTag = "face-name-chooser";
    private const int HazeAlpha = 0xB3;

    public static void Show(
        Context context, FrameLayout host, IReadOnlyList<PickablePerson> people, string? wronglySuggested,
        Action<PickablePerson> chose)
    {
        Dismiss(host);

        var scrim = new FrameLayout(context) { Tag = HostTag };
        scrim.SetBackgroundColor(new Color(
            PaperTheme.PaperDeep.R, PaperTheme.PaperDeep.G, PaperTheme.PaperDeep.B, HazeAlpha));
        scrim.Click += (_, _) => Dismiss(host);

        var pad = PaperTheme.Dp(context, DesignTokens.Space.X4);
        var card = new LinearLayout(context) { Orientation = Orientation.Vertical };
        card.Background = PaperUi.Outline(
            context, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        card.Click += (_, _) => { };
        card.SetPadding(pad, pad, pad, PaperTheme.Dp(context, DesignTokens.Space.X2));

        card.AddView(PaperUi.Heading(context, "Who is it?"));
        if (people.Count == 0)
        {
            card.AddView(PaperUi.Secondary(context,
                "Plate cannot read the address book, so there is nobody to choose. Allow Contacts for Plate, then try again."));
        }

        var field = PaperUi.Field(context, "Type a name");
        card.AddView(field);

        var rows = new LinearLayout(context) { Orientation = Orientation.Vertical };
        card.AddView(rows);

        void Fill(string typed)
        {
            rows.RemoveAllViews();
            foreach (var person in FacePicker.Choose(people, typed, wronglySuggested))
            {
                var row = PaperUi.Body(context, person.Name);
                var vertical = PaperTheme.Dp(context, DesignTokens.Space.X3);
                row.SetPadding(0, vertical, 0, vertical);
                row.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));
                row.Clickable = true;
                row.Click += (_, _) =>
                {
                    Dismiss(host);
                    chose(person);
                };
                rows.AddView(row);
            }
        }

        field.TextChanged += (_, _) => Fill(field.Text ?? string.Empty);
        Fill(string.Empty);

        var edge = PaperTheme.Dp(context, DesignTokens.Shape.ScreenEdgeDp);
        var layout = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center);
        layout.SetMargins(edge, 0, edge, 0);
        scrim.AddView(card, layout);
        host.AddView(scrim, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    public static void Dismiss(FrameLayout host)
    {
        var existing = host.FindViewWithTag(HostTag);
        if (existing is not null)
        {
            host.RemoveView(existing);
        }
    }
}
