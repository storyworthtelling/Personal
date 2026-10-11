using Android.App;
using Android.Widget;
using Codex.Ui;
using Pulp.App;
using Pulp.Domain.ChildView;
using Pulp.Domain.Shell;

namespace Pulp.Shell;

/// <summary>
/// "Watch as a child" (D213): which child, or the children together (D175, D280). With one child on
/// the roster there is nothing to ask and the view begins at once; with several, a paper card lists
/// each child and then all of them together (<see cref="ChildPickChoices"/>). With none, the card
/// says so in one sentence and offers nothing. The household is read off the UI thread.
/// </summary>
public static class ChildPicker
{
    public static void Show(Activity host, FrameLayout overlay, PulpArea from, Action<ChildViewSession> picked)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(picked);
        var services = PulpServices.For(host);

        _ = Task.Run(async () =>
        {
            try
            {
                var view = await services.HouseholdAsync(CancellationToken.None);
                var choices = ChildPickChoices.For(ChildPickChoices.ChildrenOf(view.Roster), view.NameOf);
                host.RunOnUiThread(() =>
                {
                    if (choices.Count == 1)
                    {
                        picked(new ChildViewSession(choices[0].Children, from));
                        return;
                    }

                    var actions = choices
                        .Select(choice => new CardAction(choice.Label, () => picked(new ChildViewSession(choice.Children, from))))
                        .ToArray();
                    ActionCard.Show(host, overlay, ChildViewText.WhoIsWatching, choices.Count == 0 ? ChildViewText.NoChildren : null, actions);
                });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                services.Diagnostics.Warn("child", "The household's children could not be read for Watch as a child.", exception);
            }
        });
    }
}
