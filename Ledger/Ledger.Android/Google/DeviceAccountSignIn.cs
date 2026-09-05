using global::Android.Accounts;
using global::Android.App;
using global::Android.Content;

namespace Ledger.Android.Google;

// Native "Sign in with Google" via the core Android AccountManager: shows the system picker of Google
// accounts already on the device, then requests read-only Drive consent for the chosen account so Ledger
// can read the shared owner's-voice profile. Picking the account also grants this app visibility to it.
public static class DeviceAccountSignIn
{
    public const int RequestCode = 9101;

    private static TaskCompletionSource<string>? pendingChoice;

    public static async Task<string> SignIn(Activity activity)
    {
        var email = await ChooseAccount(activity);
        await EnsureConsent(activity, email);
        SettingsStore.GoogleAccountEmail = email;
        return email;
    }

    private static Task<string> ChooseAccount(Activity activity)
    {
        var intent = AccountManager.NewChooseAccountIntent(
            null, null, new[] { "com.google" }, null, null, null, null);

        pendingChoice = new TaskCompletionSource<string>();
        activity.StartActivityForResult(intent, RequestCode);
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
            tcs.TrySetException(new InvalidOperationException("Account selection was cancelled."));
            return;
        }

        var email = data.GetStringExtra(AccountManager.KeyAccountName);
        if (string.IsNullOrEmpty(email))
            tcs.TrySetException(new InvalidOperationException("No account was returned from the picker."));
        else
            tcs.TrySetResult(email);
    }

    private static Task EnsureConsent(Activity activity, string email)
    {
        var manager = AccountManager.Get(activity)!;
        var account = new Account(email, "com.google");
        var tcs = new TaskCompletionSource<bool>();
        manager.GetAuthToken(account, DeviceAccountTokenProvider.TokenScope, null, activity,
            new ConsentCallback(tcs), null);
        return tcs.Task;
    }

    private sealed class ConsentCallback(TaskCompletionSource<bool> tcs)
        : Java.Lang.Object, IAccountManagerCallback
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
