using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// Covers two-up (DESIGN.md §7 Artwork slot), for the one place in a library where
/// the picture is how you find the thing: albums.
///
/// A list would be the wrong shape here. An album's name and its artist are what a
/// list row shows, and neither is what anyone recognises an album by — the sleeve
/// is. Two columns is what makes the sleeve big enough to be recognised rather than
/// merely present.
///
/// The title still sits under the cover rather than on it. Art is content and keeps
/// whatever colour it arrived with (§7), so laying type over it would be unreadable
/// on some covers and fine on others, which is worse than always outside.
/// </summary>
public class CoverGrid : GridView
{
    /// <summary>Big enough to recognise, two of them across a phone's well.</summary>
    public const int CoverSizeDp = 121;

    public CoverGrid(Context context, IReadOnlyList<PaperListRow> rows, Action<int> onClick,
        Action<int>? onLongClick = null)
        : base(context)
    {
        NumColumns = 2;
        SetBackgroundColor(Android.Graphics.Color.Transparent);
        CacheColorHint = Android.Graphics.Color.Transparent;
        var gap = PaperTheme.Dp(context, DesignTokens.Space.X3);
        SetHorizontalSpacing(gap);
        SetVerticalSpacing(gap);
        SetPadding(gap, gap, gap, gap);
        Adapter = new CoverAdapter(context, rows);

        ItemClick += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < rows.Count)
            {
                onClick(e.Position);
            }
        };

        if (onLongClick is not null)
        {
            ItemLongClick += (_, e) =>
            {
                if (e.Position >= 0 && e.Position < rows.Count)
                {
                    onLongClick(e.Position);
                }

                e.Handled = true;
            };
        }
    }

    private sealed class CoverAdapter(Context context, IReadOnlyList<PaperListRow> rows) : BaseAdapter
    {
        public override int Count => rows.Count;

        public override Java.Lang.Object? GetItem(int position) => null;

        public override long GetItemId(int position) => position;

        public override View GetView(int position, View? convertView, ViewGroup? parent)
        {
            var view = convertView as CellView ?? new CellView(context);
            view.Bind(rows[position]);
            return view;
        }
    }

    private sealed class CellView : LinearLayout
    {
        private readonly ArtworkWindow artwork;
        private readonly TextView title;
        private readonly TextView subtitle;

        public CellView(Context context) : base(context)
        {
            Orientation = Orientation.Vertical;

            var size = PaperTheme.Dp(context, CoverSizeDp);
            artwork = new ArtworkWindow(context);
            AddView(artwork, new LayoutParams(size, size));

            title = new TextView(context)
            {
                TextSize = DesignTokens.TypeSp.Body,
                Typeface = PaperTheme.SansRegular(context),
                Ellipsize = Android.Text.TextUtils.TruncateAt.End,
            };
            title.SetTextColor(PaperTheme.Ink);
            title.SetSingleLine(true);
            title.SetPadding(0, PaperTheme.Dp(context, DesignTokens.Space.X1), 0, 0);
            AddView(title, new LayoutParams(size, ViewGroup.LayoutParams.WrapContent));

            subtitle = new TextView(context)
            {
                TextSize = DesignTokens.TypeSp.Secondary,
                Typeface = PaperTheme.SansRegular(context),
                Ellipsize = Android.Text.TextUtils.TruncateAt.End,
            };
            subtitle.SetTextColor(PaperTheme.InkMuted);
            subtitle.SetSingleLine(true);
            AddView(subtitle, new LayoutParams(size, ViewGroup.LayoutParams.WrapContent));
        }

        public void Bind(PaperListRow row)
        {
            title.Text = row.Primary;
            subtitle.Text = row.Secondary;
            subtitle.Visibility = row.Secondary is null ? ViewStates.Gone : ViewStates.Visible;

            // Set every bind, including to null: cells are recycled, and an album
            // with no sleeve must not inherit the last one's.
            artwork.SetArtwork(CoverCache.Get(
                row.ArtworkPath, PaperTheme.Dp(Context!, CoverSizeDp)));
        }
    }
}
