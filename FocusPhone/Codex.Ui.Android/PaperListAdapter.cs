using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

public sealed record PaperListRow(string Primary, string? Secondary = null);

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

        public RowView(Context context) : base(context)
        {
            Orientation = Orientation.Vertical;
            SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.ListRowMinHeightDp));
            SetGravity(GravityFlags.CenterVertical);
            var horizontal = PaperTheme.Dp(context, DesignTokens.Space.X4);
            var vertical = PaperTheme.Dp(context, DesignTokens.Space.X3);
            SetPadding(horizontal, vertical, horizontal, vertical);

            primary = new TextView(context)
            {
                TextSize = DesignTokens.TypeSp.Body,
                Typeface = PaperTheme.SansRegular(context),
            };
            primary.SetTextColor(PaperTheme.Ink);
            AddView(primary);

            secondary = new TextView(context)
            {
                TextSize = DesignTokens.TypeSp.Secondary,
                Typeface = PaperTheme.SansRegular(context),
            };
            secondary.SetTextColor(PaperTheme.InkMuted);
            AddView(secondary);
        }

        public void Bind(PaperListRow row)
        {
            primary.Text = row.Primary;
            secondary.Text = row.Secondary;
            secondary.Visibility = row.Secondary is null ? ViewStates.Gone : ViewStates.Visible;
        }
    }
}
