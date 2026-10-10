using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Core;

namespace Pulp.Favorites;

/// <summary>One poster on a child's page: the title, where it plays, and the small line saying why it is there.</summary>
public sealed record PosterCell(TitleId Id, string Name, string? Where, string? Source, string? PosterUrl);

/// <summary>
/// Big posters, two across (mockup 06): a child picks by picture. Each cell is the poster in an
/// artwork window at the 2:3 a poster is drawn at, the title in Sans, where it plays in the muted
/// secondary line, and the source line in the label face — the line the parent reads over a
/// shoulder (D230). A tap is the cell's own action; a long press opens the child's card.
/// </summary>
public static class PosterGrid
{
    /// <summary>A poster is taller than wide by half again (mockup 06's 2/3 aspect).</summary>
    private const int PosterTallNumerator = 3;
    private const int PosterTallDenominator = 2;

    public static View Build(
        Context context,
        IReadOnlyList<PosterCell> cells,
        Action<PosterCell> onTap,
        Action<PosterCell> onLongPress,
        Action<ArtworkWindow, string, int> wantsPoster)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cells);
        var gap = PaperTheme.Dp(context, DesignTokens.Space.X3);
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        column.SetPadding(0, 0, 0, PaperTheme.Dp(context, DesignTokens.Space.X4));

        for (var index = 0; index < cells.Count; index += 2)
        {
            var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            row.AddView(Cell(context, cells[index], onTap, onLongPress, wantsPoster), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1, MarginEnd = gap });
            if (index + 1 < cells.Count)
            {
                row.AddView(Cell(context, cells[index + 1], onTap, onLongPress, wantsPoster), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
            }
            else
            {
                // An odd poster keeps its column's width rather than stretching across the page.
                row.AddView(new Space(context), new LinearLayout.LayoutParams(0, 0) { Weight = 1 });
            }

            column.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = gap });
        }

        var scroll = new ScrollView(context) { FillViewport = true };
        scroll.AddView(column);
        return scroll;
    }

    private static View Cell(
        Context context,
        PosterCell cell,
        Action<PosterCell> onTap,
        Action<PosterCell> onLongPress,
        Action<ArtworkWindow, string, int> wantsPoster)
    {
        var view = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var poster = new PosterFrame(context);
        view.AddView(poster, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        if (cell.PosterUrl is { } url)
        {
            poster.Measured += width => wantsPoster(poster.Window, url, width);
        }

        var name = PaperUi.Body(context, cell.Name);
        name.SetPadding(0, PaperTheme.Dp(context, DesignTokens.Space.X1), 0, 0);
        view.AddView(name);
        if (!string.IsNullOrWhiteSpace(cell.Where))
        {
            view.AddView(PaperUi.Secondary(context, cell.Where));
        }

        if (!string.IsNullOrWhiteSpace(cell.Source))
        {
            view.AddView(PaperUi.Label(context, cell.Source));
        }

        view.Clickable = true;
        view.LongClickable = true;
        view.Click += (_, _) => onTap(cell);
        view.LongClick += (_, args) =>
        {
            args.Handled = true;
            onLongPress(cell);
        };
        return view;
    }

    /// <summary>An artwork window held at the poster's 2:3, its width the column's; says its width once known.</summary>
    private sealed class PosterFrame : FrameLayout
    {
        private bool told;

        public PosterFrame(Context context) : base(context)
        {
            Window = new ArtworkWindow(context);
            AddView(Window, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        }

        public ArtworkWindow Window { get; }

        public event Action<int>? Measured;

        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
        {
            var width = MeasureSpec.GetSize(widthMeasureSpec);
            var height = width * PosterTallNumerator / PosterTallDenominator;
            base.OnMeasure(widthMeasureSpec, MeasureSpec.MakeMeasureSpec(height, MeasureSpecMode.Exactly));
            if (!told && width > 0)
            {
                told = true;
                Measured?.Invoke(width);
            }
        }
    }
}
