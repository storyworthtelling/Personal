using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Authentication;

namespace DailyLoader.Android.Google;

// Receives the redirect from the Google consent page for the in-app OAuth flow.
//
// IMPORTANT: the DataScheme below must be your OAuth client's reverse client id. For a client
// id like "123456-abcd.apps.googleusercontent.com", the scheme is
// "com.googleusercontent.apps.123456-abcd". Replace the placeholder before building, and make
// sure it matches the client id you enter on the Settings page.
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = Scheme)]
public class GoogleAuthCallbackActivity : WebAuthenticatorCallbackActivity
{
    public const string Scheme = "com.googleusercontent.apps.REPLACE_WITH_YOUR_REVERSE_CLIENT_ID";
}
