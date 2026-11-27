using Android.Content;
using Watch.Domain.Dash;

namespace Dash.Wear;

/// <summary>
/// Carries a filing or a delete from the wrist to the phone, and its undo (P14 B5, W-5.1.4), on the pool and whether or
/// not the screen that made it is still there: the act's state (<see cref="WristAct"/>) is told how each hand-over went,
/// and only the drawing waits for a screen. An undo tapped while the act was still on its way goes as soon as the act has
/// landed, so it never reaches the phone first. Every step is the link's, and bounded (<see cref="PostLink"/>).
/// </summary>
internal static class ActCarrier
{
    /// <summary>The act, then any undo tapped while it was on its way.</summary>
    public static async Task ActAsync(Context context, WristAct act)
    {
        var undo = act.Told(await PostLink.Shared.TellAsync(context, act.Message));
        if (undo is not null)
        {
            await UndoAsync(context, act, undo);
        }
    }

    /// <summary>The undo a tap asked for once the phone had the act.</summary>
    public static async Task UndoAsync(Context context, WristAct act, DashUndo undo) =>
        act.UndoTold(await PostLink.Shared.TellAsync(context, undo));
}
