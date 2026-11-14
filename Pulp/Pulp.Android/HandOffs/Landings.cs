using Android.Content;
using Android.OS;
using Android.Widget;
using Pulp.Components;
using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Services;

namespace Pulp.HandOffs;

/// <summary>
/// The mis-landing check's device half (D122, D286, orphan audit §28). A started hand-off is watched; every
/// session Flyleaf relays is seen; each hand-off is settled once its verdict is final — the title seen
/// playing, or <see cref="LandingCheck.Window"/> gone by — and its notice raised or withdrawn on the page
/// that started it. Process-wide, behind a lock: the receiver and the pages are on different threads.
/// </summary>
public static class Landings
{
    private static readonly object Gate = new();
    private static readonly LandingWatch Watched = new();
    private static readonly Handler MainThread = new(Looper.MainLooper!);

    // What a settled hand-off needs to speak on the page that started it.
    private sealed record Answer(
        Context Context, FrameLayout Overlay, TitleId Title, string Service, string TitleName, PulpNotices.Buttons Buttons);

    public static void Watch(
        Context context, FrameLayout overlay, PendingHandOff handOff, string serviceName, string titleName, PulpNotices.Buttons buttons)
    {
        lock (Gate)
        {
            Watched.Watch(handOff, new Answer(context, overlay, handOff.Title, serviceName, titleName, buttons));
        }

        // The window closing is the last chance to settle; a little after it, so the boundary is past.
        MainThread.PostDelayed(Settle, (long)(LandingCheck.Window + TimeSpan.FromSeconds(5)).TotalMilliseconds);
    }

    public static void Saw(PlaybackSession session)
    {
        lock (Gate)
        {
            Watched.Saw(session);
        }
        MainThread.Post(Settle);
    }

    private static void Settle()
    {
        IReadOnlyList<SettledLanding> settled;
        lock (Gate)
        {
            settled = Watched.Settle(ServiceCatalogue.Public, DateTimeOffset.Now);
        }

        foreach (var landing in settled)
        {
            if (landing.Tag is not Answer answer)
            {
                continue;
            }
            Codex.Ui.HeadLog.Info("Pulp", $"Hand-off landing: {landing.Verdict.Landing} ({landing.Verdict.Reason}).");
            PulpNotices.AfterLanding(answer.Context, answer.Overlay, landing.Verdict, answer.Title,
                answer.Service, answer.TitleName, DateTimeOffset.Now, answer.Buttons);
        }
    }
}
