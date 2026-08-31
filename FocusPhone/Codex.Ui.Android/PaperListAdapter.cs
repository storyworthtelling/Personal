using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <param name="ArtworkPath">
/// A local cover file, or null. Present only where the art distinguishes one row
/// from the next (DESIGN.md §7 Artwork slot) — a song list, not an album's tracks.
/// </param>
/// <param name="Picked">
/// Whether this row is chosen, while the list is in selection mode. Ignored
/// otherwise — a list that is not picking has no picked rows to show.
/// </param>
public sealed record PaperListRow(
    string Primary, string? Secondary = null, string? ArtworkPath = null, bool Picked = false);

/// <summary>
/// The DESIGN.md list-row recipe, replacing the stock SimpleListItem layouts:
/// 56dp minimum height, Sans primary line in ink, Sans secondary line in
/// muted ink. Pair with Ui.StylePaperList for hairline dividers.
/// </summary>
public class PaperListAdapter : BaseAdapter
{
    private readonly Context context;
    private readonly IReadOnlyList<PaperListRow> rows;
    private readonly bool picking;

    /// <param name="picking">
    /// Selection mode: the leading slot becomes a checkbox and cover art comes off.
    /// The art is dropped rather than squeezed beside the box — at this row height
    /// both crush the title, and while you are picking, the title is the thing you
    /// are reading.
    /// </param>
    public PaperListAdapter(Context context, IReadOnlyList<PaperListRow> rows, bool picking = false)
    {
        this.context = context;
        this.rows = rows;
        this.picking = picking;
    }

    public override int Count => rows.Count;

    public override Java.Lang.Object? GetItem(int position) => null;

    public override long GetItemId(int position) => position;

    public override View GetView(int position, View? convertView, ViewGroup? parent)
    {
        var view = convertView as RowView ?? new RowView(context);
        view.Bind(rows[position], picking);
        return view;
    }

    private sealed class RowView : LinearLayout
    {
        private readonly TextView primary;
        private readonly TextView secondary;
        private readonly ArtworkWindow artwork;
        private readonly LinearLayout lines;
        private readonly Checkbox checkbox;

        public RowView(Context context) : base(context)
        {
            Orientation = Orientation.Horizontal;
            SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.ListRowMinHeightDp));
            SetGravity(GravityFlags.CenterVertical);
            var horizontal = PaperTheme.Dp(context, DesignTokens.Space.X4);
            var vertical = PaperTheme.Dp(context, DesignTokens.Space.X3);
            SetPadding(horizontal, vertical, horizontal, vertical);

            // The window is built once and hidden when a row has no art, rather than
            // added and removed — a row that changes width as you scroll past it is
            // worse than one that reserves the space.
            checkbox = new Checkbox(context) { Visibility = ViewStates.Gone };
            var box = PaperTheme.Dp(context, DesignTokens.Shape.CheckboxDp);
            var boxParams = new LayoutParams(box, box);
            boxParams.SetMargins(0, 0, PaperTheme.Dp(context, DesignTokens.Space.X3), 0);
            AddView(checkbox, boxParams);

            var size = PaperTheme.Dp(context, ArtworkWindow.RowSizeDp);
            artwork = new ArtworkWindow(context) { Visibility = ViewStates.Gone };
            var art = new LayoutParams(size, size);
            art.SetMargins(0, 0, PaperTheme.Dp(context, DesignTokens.Space.X3), 0);
            AddView(artwork, art);

            lines = new LinearLayout(context) { Orientation = Orientation.Vertical };
            AddView(lines, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

            primary = new TextView(context)
            {
                TextSize = DesignTokens.TypeSp.Body,
                Typeface = PaperTheme.SansRegular(context),
            };
            primary.SetTextColor(PaperTheme.Ink);
            lines.AddView(primary);

            secondary = new TextView(context)
            {
                TextSize = DesignTokens.TypeSp.Secondary,
                Typeface = PaperTheme.SansRegular(context),
            };
            secondary.SetTextColor(PaperTheme.InkMuted);
            lines.AddView(secondary);
        }

        public void Bind(PaperListRow row, bool picking)
        {
            primary.Text = row.Primary;
            secondary.Text = row.Secondary;
            secondary.Visibility = row.Secondary is null ? ViewStates.Gone : ViewStates.Visible;

            // Set every time, both ways: the view is recycled, so a row that is not
            // picked has to actively say so or it inherits the last one's wash.
            checkbox.Visibility = picking ? ViewStates.Visible : ViewStates.Gone;
            checkbox.SetPicked(row.Picked);
            SetBackgroundColor(picking && row.Picked
                ? PaperTheme.Wash(PaperTheme.Accent)   // §8 Selected: the highlighter wash
                : Color.Transparent);

            if (picking || row.ArtworkPath is null)
            {
                artwork.Visibility = ViewStates.Gone;
                return;
            }

            // Set every bind, including to null: this view is recycled, and a row
            // with no cover must not inherit the last one's.
            artwork.Visibility = ViewStates.Visible;
            artwork.SetArtwork(CoverCache.Get(
                row.ArtworkPath, PaperTheme.Dp(Context!, ArtworkWindow.RowSizeDp)));
        }
    }
}
