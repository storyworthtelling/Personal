namespace Codex.Ui;

/// <summary>
/// A view that handles its own touches and draws its controls on a canvas (Flyleaf's bookshelf) is not Clickable,
/// so the tap watch cannot tell a tap on one of them from a tap on nothing. Such a view implements this and the
/// watch asks it, in the view's own coordinates, whether a control is drawn under the finger.
/// </summary>
public interface IDrawnControls
{
    bool HasControlAt(float x, float y);
}
