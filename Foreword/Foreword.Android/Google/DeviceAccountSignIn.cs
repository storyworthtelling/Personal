using Storyworthtelling.Maui.Diagnostics;
using Android.Accounts;
using Android.App;
using Android.Content;

namespace Foreword.Android.Google;

// Native "Sign in with Google" via the core Android AccountManager: shows the system picker of
// Google accounts already on the device, then requests Calendar/Tasks consent for the chosen
// account. Picking the account in the system picker also grants this app visibility to it, so
// the token provider can mint tokens for it afterwards. No Play Services binding is needed.
public static class DeviceAccountSignIn
{
    public const int RequestCode = 9001;

    private static TaskCompletionSource<string>? pendingChoice;

    public static async Task<string> SignIn(Activity activity)
    {
        // LOG-LOOP-PLAN.md §4.2: every way this ends is a step on the trail, written to the log when it ends.
        DeviceAccountTrail.Begin("google calendar tasks drive");
        try
        {
            var email = await ChooseAccount(activity);
            DeviceAccountTrail.ConsentShown();
            await EnsureConsent(activity, email);
            DeviceAccountTrail.Token("google calendar tasks drive");
            CredentialStore.GoogleAccountEmail = email;
            return email;
        }
        catch (Exception e) when (e is not InvalidOperationException)
        {
            DeviceAccountTrail.Failed(e);
            throw;
        }
    }

    private static Task<string> ChooseAccount(Activity activity)
    {
        // Positional args (binding parameter names differ from the Java docs):
        // (selectedAccount, allowableAccounts, allowableAccountTypes, descriptionOverrideText,
        //  addAccountAuthTokenType, addAccountRequiredFeatures, addAccountOptions)
        var intent = AccountManager.NewChooseAccountIntent(
            null, null, new[] { "com.google" }, null, null, null, null);

        pendingChoice = new TaskCompletionSource<string>();
        activity.StartActivityForResult(intent, RequestCode);
        DeviceAccountTrail.LeftApp();
        return pendingChoice.Task;
    }

    // Invoked from MainActivity.OnActivityResult once the picker returns.
    public static void HandleChooserResult(Result resultCode, Intent? data)
    {
        var tcs = pendingChoice;
        if (tcs is null)
            return;
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

    // Fetches a token interactively (passing the activity) so the Calendar/Tasks consent screen
    // is shown now, rather than failing silently in the background job later.
    private static Task EnsureConsent(Activity activity, string email) =>
        EnsureConsent(activity, email, DeviceAccountTokenProvider.TokenScope);

    private static Task EnsureConsent(Activity activity, string email, string tokenType)
    {
        var manager = AccountManager.Get(activity)!;
        var account = new Account(email, "com.google");
        var tcs = new TaskCompletionSource<bool>();
        manager.GetAuthToken(account, tokenType, null, activity,
            new ConsentCallback(tcs), null);
        return tcs.Task;
    }

    /// <summary>
    /// A token for one extra scope, asked for on its own rather than added to the delivery token —
    /// adding it there would put a new consent screen in front of the calendar and the tasks, and the
    /// morning would stop until somebody answered it. Consent is shown interactively first, then the
    /// token is read the same way the delivery reads its own.
    /// </summary>
    public static async Task<string?> TokenFor(Activity activity, string scope)
    {
        var email = CredentialStore.GoogleAccountEmail;
        if (string.IsNullOrEmpty(email))
            return null;

        var tokenType = "oauth2:" + scope;
        await EnsureConsent(activity, email, tokenType);

        return await Task.Run(() => AccountManager.Get(activity)!
            .BlockingGetAuthToken(new Account(email, "com.google"), tokenType, true));
    }

    private sealed class ConsentCallback(TaskCompletionSource<bool> tcs)
        : Java.Lang.Object, IAccountManagerCallback
    {
        public void Run(IAccountManagerFuture? future)
        {
            try
            {
                // The callback only fires once the future is done, so this returns immediately;
                // reading the result surfaces any auth error (and the consent UI has already shown).
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
