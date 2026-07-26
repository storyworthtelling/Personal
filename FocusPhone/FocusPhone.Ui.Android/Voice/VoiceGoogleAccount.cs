using global::Android.Accounts;
using global::Android.App;
using global::Android.Content;

namespace FocusPhone.Ui.Voice;

// Device-account Google sign-in + access token for reading the shared owner's-voice profile from Drive,
// for heads that don't already have their own Google account (Audio does, for streaming music). Read-only
// Drive scope; the chosen account email is stored in the head's OWN SharedPreferences (a fixed key, but
// each app's prefs are isolated), so every head keeps its own selection.
public sealed class VoiceGoogleAccount
{
    public const int RequestCode = 9201;
    private const string TokenScope = "oauth2:https://www.googleapis.com/auth/drive.readonly";
    private const string PrefsName = "voice";
    private const string EmailKey = "googleAccountEmail";

    private static TaskCompletionSource<string>? pendingChoice;
    private readonly object gate = new();
    private string? cachedToken;
    private DateTimeOffset expiresAt;

    private static ISharedPreferences Prefs() =>
        global::Android.App.Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    public static string Email => Prefs().GetString(EmailKey, string.Empty) ?? string.Empty;

    public static bool IsSignedIn => !string.IsNullOrEmpty(Email);

    private static void SetEmail(string value)
    {
        var editor = Prefs().Edit()!;
        editor.PutString(EmailKey, value);
        editor.Apply();
    }

    public Task<string> GetAccessToken()
    {
        lock (gate)
        {
            if (cachedToken is not null && DateTimeOffset.UtcNow < expiresAt)
                return Task.FromResult(cachedToken);
        }

        var email = Email;
        if (string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Sign in with Google first.");

        return Task.Run(() =>
        {
            var manager = AccountManager.Get(global::Android.App.Application.Context)!;
            var account = new Account(email, "com.google");
            lock (gate)
            {
                if (cachedToken is not null)
                    manager.InvalidateAuthToken("com.google", cachedToken);
            }

            var token = manager.BlockingGetAuthToken(account, TokenScope, true)
                ?? throw new InvalidOperationException("Could not get a Google token. Sign in again.");
            lock (gate)
            {
                cachedToken = token;
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(45);
            }
            return token;
        });
    }

    public async Task<string> SignIn(Activity activity)
    {
        var email = await ChooseAccount(activity);
        await EnsureConsent(activity, email);
        SetEmail(email);
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

    // Called from the head's Activity.OnActivityResult once the picker returns.
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
        manager.GetAuthToken(account, TokenScope, null, activity, new ConsentCallback(tcs), null);
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
