using Android.Content;
using Android.Widget;
using Storyworthtelling.Voice.Health;

namespace Codex.Ui;

/// <summary>
/// The quiet "get a key" button under a key field: it opens the provider's developer console from the
/// suite's one table (<see cref="KeyConsoles"/>). A credential with no console has no button.
/// </summary>
public static class KeyConsoleLink
{
    /// <summary>The link for <paramref name="credentialId"/>, or null when it has no console.</summary>
    public static Button? For(Context context, string credentialId)
    {
        ArgumentNullException.ThrowIfNull(context);
        var console = KeyConsoles.For(credentialId);
        return console is null ? null : PaperUi.QuietButton(context, console.Label, () => Open(context, console));
    }

    /// <summary>Adds the link to <paramref name="parent"/> when the credential has a console.</summary>
    public static void AddTo(LinearLayout parent, string credentialId)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var link = For(parent.Context!, credentialId);
        if (link is not null)
        {
            parent.AddView(link, new LinearLayout.LayoutParams(
                Android.Views.ViewGroup.LayoutParams.WrapContent, Android.Views.ViewGroup.LayoutParams.WrapContent));
        }
    }

    private static void Open(Context context, KeyConsole console)
    {
        // The phone's web handler answers: on the locked phone that is Flyleaf's restricted browser.
        var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(console.Address.AbsoluteUri));
        try
        {
            context.StartActivity(intent);
        }
        catch (ActivityNotFoundException exception)
        {
            HeadLog.Warn("KeyConsole", $"Nothing on this phone opens the {console.CredentialId} console.", exception);
        }
    }
}
