using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Areas;
using Constitution.Domain.Ground;
using Constitution.Domain.Scrapbook;
using Constitution.Scrapbook;

namespace Constitution;

/// <summary>
/// What the five areas share: the tab drawn from the domain's tokens, the ground decision, the
/// Overlay slot, and the loading rule. An area supplies its id and glyph, and builds its page.
/// </summary>
public abstract class ConstitutionAreaBase(Activity host, string id, int glyphResourceId) : IConstitutionArea
{
    protected Activity Host { get; } = host;

    public ConstitutionArea Definition { get; } = ConstitutionAreas.All.Single(area => area.Id == id);

    public BinderArea Tab => new(
        Definition.Name,
        glyphResourceId,
        Color.ParseColor(Definition.ActiveInk),
        Color.ParseColor(Definition.Cloth));

    public string Title => Definition.Name;

    public PageGround Ground => PageGround.For(Definition);

    /// <summary>
    /// What the page sits on, decided by the page's ground and by nothing else: crinkled exam-table
    /// paper behind the well on Today, Workouts, Meals and Body (D43), the suite's plain paper on
    /// Settings (D71). An area does not choose, so Settings cannot be given a crinkle by accident.
    /// </summary>
    public virtual IPageGroundPainter GroundPainter => groundPainter ??=
        Ground == PageGround.Crinkled
            ? new CrinkleGroundPainter(Host, CrinkleSeed.For(Definition.Id))
            : PlainGroundPainter.Instance;

    private IPageGroundPainter? groundPainter;

    public FrameLayout Overlay { get; set; } = null!;

    public event Action? Changed;

    /// <summary>While true the well shows <see cref="LoadingSentence"/> instead of the page.</summary>
    protected bool Loading { get; set; }

    protected virtual string LoadingSentence => "Reading the plan.";

    /// <summary>The page itself, built fresh on every render.</summary>
    protected abstract View BuildPage();

    /// <summary>
    /// The page on its ground. The shell no longer insets the well to the screen edge (a ground has
    /// to reach the edges of the well), so a page that is not already edge to edge is inset here.
    /// </summary>
    public View BuildView()
    {
        var page = Loading ? PageFurniture.StatusSentence(Host, LoadingSentence) : BuildPage();
        return GroundPainter.Paint(page is IFullBleedPage ? page : InsetToTheScreenEdge(page));
    }

    private View InsetToTheScreenEdge(View page)
    {
        var inset = new FrameLayout(Host);
        var edge = PaperTheme.Dp(Host, DesignTokens.Shape.ScreenEdgeDp);
        inset.SetPadding(edge, 0, edge, 0);
        inset.AddView(page, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        return inset;
    }

    public virtual View? BuildBottomBar() => null;

    public virtual void Resume()
    {
    }

    public virtual void Pause()
    {
    }

    protected void RaiseChanged() => Changed?.Invoke();
}
