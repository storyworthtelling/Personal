using Android.Content;
using Android.Graphics;
using Android.Widget;
using Codex.Ui;
using Pulp.Domain.Actions;
using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;
using Pulp.HandOffs;

namespace Pulp.Components;

/// <summary>
/// Pulp's notices on the shared dog-ear (mockup 12, D260, DESIGN.md §13): one fold in the page's
/// top-right corner, no count, turning to the Waiting page. <see cref="WaitingNotices"/> writes each one
/// — what happened, what to do next, its role and at most one button — and this lays it on the fold,
/// keyed by its cause so it leaves when the cause does. Never voiced, never a toast.
/// </summary>
public static class PulpNotices
{
    /// <summary>What the page does when a notice's button is pressed.</summary>
    public sealed record Buttons(Action<TitleId>? OpenTitle = null, Action? OpenVault = null);

    /// <summary>Raises (or restates) a notice. Restating the same cause replaces it.</summary>
    public static void Raise(Context context, FrameLayout overlay, WaitingNotice notice, Buttons buttons, TitleId? about = null)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(buttons);
        DogEar.SetNotice(context, overlay, notice.Key, ToDogEar(notice, buttons, about));
    }

    /// <summary>The cause is gone: the notice leaves (mockup 12, "a notice leaves when its cause does").</summary>
    public static void Withdraw(Context context, FrameLayout overlay, string key) =>
        DogEar.SetNotice(context, overlay, key, null);

    /// <summary>
    /// The executor's report, laid on the fold: a refusal or a missing app raises the title's notice,
    /// and a start withdraws any notice the title had — the app opening is the answer.
    /// </summary>
    public static void AfterHandOff(
        Context context, FrameLayout overlay, HandOffOutcome outcome, HandOffRequest request,
        string serviceName, string titleName, DateTimeOffset at, Buttons buttons)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(request);
        var title = request.Title.Id;

        switch (outcome.Result)
        {
            case HandOffResult.Started:
                Withdraw(context, overlay, WaitingNotices.HandOffKey(title));
                break;
            case HandOffResult.NotInstalled:
                Raise(context, overlay, WaitingNotices.NotInstalled(serviceName, title, at), buttons, title);
                break;
            case HandOffResult.Refused:
                // The executor already wrote the refusal's sentence (HandOffNotice.ForRefusal).
                var sentence = outcome.Notice
                    ?? throw new InvalidOperationException($"The refused hand-off of '{titleName}' came back with no sentence.");
                Raise(context, overlay, WaitingNotices.Refused(sentence, titleName, title, at), buttons, title);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Result, "A hand-off result with no notice decided.");
        }
    }

    /// <summary>After the landing check (D122, D286): a mis-landing or silence raises; anything else withdraws.</summary>
    public static void AfterLanding(
        Context context, FrameLayout overlay, LandingVerdict verdict, TitleId title,
        string serviceName, string titleName, DateTimeOffset at, Buttons buttons)
    {
        var notice = WaitingNotices.AfterLanding(verdict.Landing, serviceName, titleName, title, at);
        if (notice is null)
        {
            Withdraw(context, overlay, WaitingNotices.HandOffKey(title));
            return;
        }

        Raise(context, overlay, notice, buttons, title);
    }

    /// <summary>The fold's colour for a role. Pulp draws no Caution (D275), so there is no arm for one.</summary>
    public static Color HueOf(NoticeRole role) => role switch
    {
        NoticeRole.Problem => DogEar.Hues.Problem,
        NoticeRole.Reference => DogEar.Hues.Reference,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "A notice role with no colour."),
    };

    private static DogEar.Notice ToDogEar(WaitingNotice notice, Buttons buttons, TitleId? about)
    {
        Action? onAction = notice.Action switch
        {
            NoticeAction.None => null,
            NoticeAction.OpenTitle => about is { } title && buttons.OpenTitle is { } open
                ? () => open(title)
                : throw new ArgumentException($"'{notice.Headline}' opens a title, but no title or handler was given."),
            NoticeAction.OpenVault => buttons.OpenVault
                ?? throw new ArgumentException($"'{notice.Headline}' opens the Vault, but no handler was given."),
            _ => throw new ArgumentOutOfRangeException(nameof(notice), notice.Action, "A notice button with no handler decided."),
        };

        return new DogEar.Notice(
            notice.Headline, notice.Detail, HueOf(notice.Role),
            onAction is null ? null : notice.ActionLabel, onAction, notice.At);
    }
}
