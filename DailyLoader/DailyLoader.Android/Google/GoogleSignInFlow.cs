using Android.App;
using Android.Content;
using Android.Gms.Auth.Api.SignIn;
using Android.Gms.Common.Api;

namespace DailyLoader.Android.Google;

// Native "Sign in with Google": shows the account picker for the Google accounts already on the
// device and requests the Calendar + Tasks scopes in one step. The chosen account email is saved
// so PlayServicesGoogleTokenProvider can mint API tokens for it.
public static class GoogleSignInFlow
{
    public const int RequestCode = 9001;

    private const string CalendarScope = "https://www.googleapis.com/auth/calendar";
    private const string TasksScope = "https://www.googleapis.com/auth/tasks";

    private static TaskCompletionSource<string>? pending;

    public static Task<string> SignIn(Activity activity)
    {
        var options = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
            .RequestEmail()
            .RequestScopes(new Scope(CalendarScope), new Scope(TasksScope))
            .Build();

        var client = GoogleSignIn.GetClient(activity, options);

        pending = new TaskCompletionSource<string>();
        activity.StartActivityForResult(client.SignInIntent, RequestCode);
        return pending.Task;
    }

    // Invoked from MainActivity.OnActivityResult once the picker returns.
    public static void HandleResult(Intent? data)
    {
        var tcs = pending;
        if (tcs is null)
            return;
        pending = null;

        try
        {
            var task = GoogleSignIn.GetSignedInAccountFromIntent(data);
            if (!task.IsSuccessful)
                throw task.Exception ?? new InvalidOperationException("Google sign-in was cancelled or failed.");

            var account = task.Result as GoogleSignInAccount
                ?? throw new InvalidOperationException("Google sign-in returned no account.");
            var email = account.Email
                ?? throw new InvalidOperationException("Google sign-in returned no account email.");

            CredentialStore.GoogleAccountEmail = email;
            tcs.TrySetResult(email);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }
}
