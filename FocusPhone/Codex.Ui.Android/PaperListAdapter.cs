using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <param name="ArtworkPath">
/// A local cover file, or null. Present only where the art distinguishes one row
/// from the next (DESIGN.md §7 Artwork slot) — a song list, not an album's tracks.
/// </param>
public sealed record PaperListRow(string Primary, string? Secondary = null, string? ArtworkPath = null);

/// <summary>
/// The DESIGN.md list-row recipe, replacing the stock SimpleListItem layouts:
/// 56dp minimum height, Sans primary line in ink, Sans secondary line in
/// muted ink. Pair with Ui.StylePaperList for hairline dividers.
/// </summary>
public class PaperListAdapter : BaseAdapter
{
    private readonly Context context;
    private readonly IReadOnlyList<PaperListRow> rows;

    public PaperListAdapter(Context context, IReadOnlyList<PaperListRow> rows)
    {
        this.context = context;
        this.rows = rows;
    }

    public override int Count => rows.Count;

    public override Java.Lang.Object? GetItem(int position) => null;

    public override long GetItemId(int position) => position;

    public override View GetView(int position, View? convertView, ViewGroup? parent)
    {
        var view = convertView as RowView ?? new RowView(context);
        view.Bind(rows[position]);
        return view;
    }

    private sealed class RowView : LinearLayout
    {
        private readonly TextView primary;
        private readonly TextView secondary;
        private readonly ArtworkWindow artwork;
        private readonly LinearLayout lines;

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

        public void Bind(PaperListRow row)
        {
            primary.Text = row.Primary;
            secondary.Text = row.Secondary;
            secondary.Visibility = row.Secondary is null ? ViewStates.Gone : ViewStates.Visible;

            if (row.ArtworkPath is null)
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
