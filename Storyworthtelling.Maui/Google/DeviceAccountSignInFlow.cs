using global::Android.Accounts;
using global::Android.App;
using global::Android.Content;
using Storyworthtelling.Maui.Diagnostics;

namespace Storyworthtelling.Maui.Google;

/// <summary>
/// "Sign in with Google" for a MAUI head, through the core Android AccountManager (audit 01 §10): the system picker
/// of Google accounts already on the device, then consent for the head's scopes. Picking the account also grants
/// the app visibility to it, so the token provider can mint tokens for it afterwards. No Play Services binding.
/// Index, Folio and Foreword each kept a copy differing only in the request code, the scopes and where the email
/// is kept. Every way a sign-in ends is a step on the <see cref="DeviceAccountTrail"/> (LOG-LOOP-PLAN.md §4.2).
/// </summary>
/// <param name="requestCode">The picker's request code; the head's MainActivity routes the result back by it.</param>
/// <param name="tokenType">The head's <c>oauth2:</c> token type, exactly as its token provider asks for it.</param>
/// <param name="trailLabel">How the trail names what was asked for.</param>
/// <param name="readEmail">Where the head keeps the chosen account.</param>
/// <param name="rememberEmail">Stores the chosen account once consent is given.</param>
public sealed class DeviceAccountSignInFlow(
    int requestCode, string tokenType, string trailLabel, Func<string?> readEmail, Action<string> rememberEmail)
{
    private const string GoogleAccountType = "com.google";

    private TaskCompletionSource<string>? pendingChoice;

    public int RequestCode { get; } = requestCode;

    public async Task<string> SignIn(Activity activity)
    {
        DeviceAccountTrail.Begin(trailLabel);
        try
        {
            var email = await ChooseAccount(activity);
            DeviceAccountTrail.ConsentShown();
            await EnsureConsent(activity, email, tokenType);
            DeviceAccountTrail.Token(trailLabel);
            rememberEmail(email);
            // The consent screen answered: whatever the background fetch was refused, it is settled (audit 11 §4).
            Health.MauiCredentialHealth.Store().MarkOk(Storyworthtelling.Voice.Health.SharedCredentialIds.GoogleAccount);
            return email;
        }
        catch (Exception e) when (e is not InvalidOperationException)
        {
            DeviceAccountTrail.Failed(e);
            throw;
        }
    }

    /// <summary>The picker's answer, routed here by the head's <c>OnActivityResult</c>.</summary>
    public void HandleChooserResult(Result resultCode, Intent? data)
    {
        var tcs = pendingChoice;
        if (tcs is null)
        {
            return;
        }

        pendingChoice = null;
        if (resultCode != Result.Ok || data is null)
        {
            DeviceAccountTrail.Cancelled();
            tcs.TrySetException(new InvalidOperationException("Account selection was cancelled."));
            return;
        }

        var email = data.GetStringExtra(AccountManager.KeyAccountName);
        if (string.IsNullOrEmpty(email))
        {
            DeviceAccountTrail.NoAccount();
            tcs.TrySetException(new InvalidOperationException("No account was returned from the picker."));
        }
        else
        {
            DeviceAccountTrail.Chosen();
            tcs.TrySetResult(email);
        }
    }

    /// <summary>
    /// A token for one extra scope, asked for on its own (with its own consent) rather than added to the head's
    /// token type, which would put a new consent screen in front of everything the head already does. Null when no
    /// account is signed in.
    /// </summary>
    public async Task<string?> TokenFor(Activity activity, string scope)
    {
        var email = readEmail();
        if (string.IsNullOrEmpty(email))
        {
            return null;
        }

        var extra = "oauth2:" + scope;
        await EnsureConsent(activity, email, extra);
        return await Task.Run(() => AccountManager.Get(activity)!
            .BlockingGetAuthToken(new Account(email, GoogleAccountType), extra, true));
    }

    private Task<string> ChooseAccount(Activity activity)
    {
        var intent = AccountManager.NewChooseAccountIntent(null, null, [GoogleAccountType], null, null, null, null);
        pendingChoice = new TaskCompletionSource<string>();
        activity.StartActivityForResult(intent, RequestCode);
        DeviceAccountTrail.LeftApp();
        return pendingChoice.Task;
    }

    private static Task EnsureConsent(Activity activity, string email, string type)
    {
        var manager = AccountManager.Get(activity)!;
        var tcs = new TaskCompletionSource<bool>();
        manager.GetAuthToken(new Account(email, GoogleAccountType), type, null, activity, new ConsentCallback(tcs), null);
        return tcs.Task;
    }

    private sealed class ConsentCallback(TaskCompletionSource<bool> tcs) : Java.Lang.Object, IAccountManagerCallback
    {
        public void Run(IAccountManagerFuture? future)
        {
            try
            {
                _ = future?.GetResult(60_000, Java.Util.Concurrent.TimeUnit.Milliseconds);
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }
    }
}
