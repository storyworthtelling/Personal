namespace Folio.Android.Pages;

/// <summary>
/// What the app shows when it cannot build its first screen.
///
/// <para>It exists because of where this app runs. The crash log is written to private storage, and
/// an app that dies on open has no Settings page to export it from — so a startup failure on the
/// owner's phone is a black hole: it stops, and there is no way to find out why without a cable and
/// a developer machine. This turns that into a sentence they can read and send on.</para>
///
/// <para>Deliberately plain: no kit tokens, no fonts, no styles, nothing that could fail for the
/// same reason the app just did. Hard-coded colours are the one correct place for them — a page
/// whose job is to survive the design system being unavailable cannot ask it for anything.</para>
/// </summary>
internal sealed class StartupFailurePage : ContentPage
{
    public StartupFailurePage(Exception failure)
    {
        BackgroundColor = Color.FromArgb("#FAF8F2");
        Padding = new Thickness(24, 48, 24, 24);

        var detail = new Editor
        {
            Text = failure.ToString(),
            IsReadOnly = true,
            AutoSize = EditorAutoSizeOption.TextChanges,
            FontSize = 12,
            TextColor = Color.FromArgb("#33322B"),
            BackgroundColor = Color.FromArgb("#FFFFFF"),
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    new Label
                    {
                        Text = "Folio could not start",
                        FontSize = 26,
                        TextColor = Color.FromArgb("#33322B"),
                    },
                    new Label
                    {
                        Text = "Your manuscripts and reports are untouched — this is the screen "
                            + "failing to build, not the library. The detail below is what went "
                            + "wrong; it can be copied and sent on.",
                        FontSize = 15,
                        TextColor = Color.FromArgb("#6B6759"),
                    },
                    detail,
                },
            },
        };
    }
}
