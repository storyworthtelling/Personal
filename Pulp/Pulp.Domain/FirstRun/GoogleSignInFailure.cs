namespace Pulp.Domain.FirstRun;

/// <summary>
/// Tells the one Google sign-in failure that retrying can never fix from the rest.
///
/// <para>Android's AccountManager answers <c>UnregisteredOnApiConsole</c> when Google has no Android
/// OAuth client for this app's package name and signing certificate. That is a console setting, not a
/// flaky network or a cancelled picker, so "try signing in again" would just loop forever. The head
/// passes the failure's text in; Domain stays free of Android types.</para>
/// </summary>
public static class GoogleSignInFailure
{
    public const string UnregisteredMarker = "UnregisteredOnApiConsole";

    public static bool IsUnregistered(string? failureText) =>
        failureText is not null && failureText.Contains(UnregisteredMarker, StringComparison.Ordinal);
}
