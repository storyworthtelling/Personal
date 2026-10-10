using Storyworthtelling.Maui;

namespace Index.Android.Pages;

/// <summary>
/// A fault that stopped the app from opening, shown whole instead of as a crash. It is the first thing
/// on screen after a crash on an earlier launch, and the page the app falls back to when it cannot build
/// its first page. The report is an editor, so the owner can select and copy the exact text, and
/// <see cref="Continue"/> is what clears it: until the owner has seen it, it stays on the next launch too.
/// </summary>
public sealed class LaunchFaultPage : ContentPage
{
    /// <param name="report">The full report text, as <see cref="LaunchFaults"/> wrote it.</param>
    /// <param name="continueTo">Builds the page to open after the report is dismissed, or null when there is
    /// nothing to continue to (a page that could not be built has no second page to open).</param>
    public LaunchFaultPage(string report, Func<Page>? continueTo)
    {
        BackgroundColor = PaperResources.Colour("Paper");

        var heading = new Label
        {
            Text = "Index stopped unexpectedly",
            Style = PaperResources.Token<Style>("HeadingLabel"),
        };
        var copy = new Label
        {
            Text = "The report below was kept on the phone. Press and hold to select it and copy it. "
                   + "It stays here until you continue.",
            Style = PaperResources.Token<Style>("SecondaryLabel"),
        };

        var column = new VerticalStackLayout
        {
            Spacing = 14,
            Padding = new Thickness(24, 60, 24, 24),
            Children = { heading, copy, ReportView(report) },
        };

        if (continueTo is not null)
        {
            var continueButton = new Button
            {
                Text = "Continue",
                Style = PaperResources.Token<Style>("SecondaryButton"),
            };
            continueButton.Clicked += (_, _) =>
            {
                LaunchFaults.ClearPending();
                Application.Current!.Windows[0].Page = continueTo();
            };
            column.Children.Add(continueButton);
        }

        Content = new ScrollView { Content = column };
    }

    /// <summary>The report as selectable monospace text. Used on this page and wherever a fault is shown
    /// inline, so both read the same way.</summary>
    public static View ReportView(string report) => new Editor
    {
        Text = report,
        IsReadOnly = true,
        AutoSize = EditorAutoSizeOption.TextChanges,
        Style = PaperResources.Token<Style>("MonoEditor"),
    };
}
