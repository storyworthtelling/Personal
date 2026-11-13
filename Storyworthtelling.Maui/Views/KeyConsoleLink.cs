using Storyworthtelling.Maui.Diagnostics;
using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// The quiet "get a key" button under a key field: it opens the provider's developer console from the
/// suite's one table (<see cref="KeyConsoles"/>). A credential with no console has no button.
/// </summary>
public static class KeyConsoleLink
{
    /// <summary>The link for <paramref name="credentialId"/>, or null when it has no console.</summary>
    public static Button? For(string credentialId)
    {
        var console = KeyConsoles.For(credentialId);
        if (console is null)
        {
            return null;
        }

        var button = new Button
        {
            Text = console.Label,
            Style = PaperResources.Token<Style>("QuietButton"),
            HorizontalOptions = LayoutOptions.Start,
        };
        button.Clicked += async (_, _) => await OpenAsync(console);
        return button;
    }

    /// <summary>Puts the link for <paramref name="credentialId"/> into <paramref name="host"/>, replacing what was there.</summary>
    public static void Fill(ContentView host, string credentialId)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Content = For(credentialId);
    }

    private static async Task OpenAsync(KeyConsole console)
    {
        try
        {
            await Browser.Default.OpenAsync(console.Address, BrowserLaunchMode.External);
        }
        catch (Exception exception)
        {
            MauiLog.Current?.Write(
                "KeyConsole",
                Microsoft.Extensions.Logging.LogLevel.Warning,
                $"The {console.CredentialId} console did not open.",
                exception);
        }
    }
}
