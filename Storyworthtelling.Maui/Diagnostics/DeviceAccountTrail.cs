using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Maui.Diagnostics;

/// <summary>
/// The Google device-account sign-in as a trail (LOG-LOOP-PLAN.md §4.2), for the MAUI heads' own account pickers
/// (each head keeps its <c>DeviceAccountSignIn</c>, which calls in here at each step). Left the app, the account
/// chosen or the picker cancelled, the consent screen, a token's scope and type (never the token), or a failure —
/// and, on the next activity resume, a sign-in that left and heard nothing back is a NoReturn. Static, because the
/// picker's answer arrives through the activity rather than the call that opened it.
/// </summary>
public static class DeviceAccountTrail
{
    private static readonly Lock Gate = new();
    private static SignInTrail? trail;

    public static void Begin(string scope)
    {
        lock (Gate)
        {
            trail = SignInTrail.Start(SignInFlowKinds.GoogleDeviceAccount, SignInTrail.NewFlowId(), DateTimeOffset.Now,
                null, null, scope.Split(' ', StringSplitOptions.RemoveEmptyEntries), SignInBrowserPaths.AccountPicker);
        }
    }

    public static void LeftApp() => Step(t => t.LeftApp(DateTimeOffset.Now));

    public static void Chosen() => Step(t => t.AccountChosen(DateTimeOffset.Now));

    public static void Cancelled() => Finish(t => t.AccountCancelled(DateTimeOffset.Now));

    public static void NoAccount() => Finish(t => t.ProviderError(DateTimeOffset.Now, "no account", "the picker returned no account name"));

    public static void ConsentShown() => Step(t => t.ConsentShown(DateTimeOffset.Now));

    public static void Token(string scope) =>
        Finish(t => t.TokenReceived(DateTimeOffset.Now, scope.Split(' ', StringSplitOptions.RemoveEmptyEntries), null, "oauth2 (AccountManager)"));

    /// <summary>The consent declined (AccountManager's OperationCanceledException) or the provider failing.</summary>
    public static void Failed(Exception e) => Finish(t => e is Android.Accounts.OperationCanceledException
        ? t.ConsentDenied(DateTimeOffset.Now, e.GetType().Name)
        : t.ProviderError(DateTimeOffset.Now, e.GetType().Name, e.Message));

    /// <summary>From an activity resume: a sign-in that left the app and heard nothing back since is a NoReturn.</summary>
    public static void CheckPending() => Finish(t => t.CheckNoReturn(DateTimeOffset.Now, resumed: true), onlyIfFinished: true);

    private static void Step(Func<SignInTrail, SignInTrail> step)
    {
        lock (Gate)
        {
            if (trail is { IsFinished: false } open)
            {
                trail = step(open);
            }
        }
    }

    private static void Finish(Func<SignInTrail, SignInTrail> step, bool onlyIfFinished = false)
    {
        SignInTrail? finished;
        lock (Gate)
        {
            if (trail is not { IsFinished: false } open)
            {
                return;
            }
            var next = step(open);
            if (onlyIfFinished && !next.IsFinished)
            {
                return;
            }
            trail = null;
            finished = next;
        }
        MauiLogLoop.SignIn(finished);
    }
}
