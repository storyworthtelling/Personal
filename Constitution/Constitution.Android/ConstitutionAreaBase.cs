using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Ui;
using Constitution.Domain.Areas;
using Constitution.Domain.Ground;

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

    public virtual IPageGroundPainter GroundPainter => PlainGroundPainter.Instance;

    public FrameLayout Overlay { get; set; } = null!;

    public event Action? Changed;

    /// <summary>While true the well shows <see cref="LoadingSentence"/> instead of the page.</summary>
    protected bool Loading { get; set; }

    protected virtual string LoadingSentence => "Reading the plan.";

    /// <summary>The page itself, built fresh on every render.</summary>
    protected abstract View BuildPage();

    public View BuildView() =>
        GroundPainter.Paint(Loading ? PageFurniture.StatusSentence(Host, LoadingSentence) : BuildPage());

    public virtual View? BuildBottomBar() => null;

    public virtual void Resume()
    {
    }

    public virtual void Pause()
    {
    }

    protected void RaiseChanged() => Changed?.Invoke();
}
