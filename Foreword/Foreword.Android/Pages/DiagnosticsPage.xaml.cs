namespace Foreword.Android.Pages;

/// <summary>
/// S2. The rolling record of what went wrong, in Score's shape.
///
/// <para>Both logs are shown as one block, oldest first, because the owner asking "what happened
/// this morning?" does not know or care whether the answer was a handled failure or a crash —
/// and reading two screens to find out is how a fault gets missed. They are kept as separate
/// files (the crash log is written by the crash handler, which cannot depend on anything) and
/// merged here for reading.</para>
///
/// <para>It is deliberately a <b>failure</b> log and nothing else. The run figures the on-device
/// plan asks for — minutes taken, pages built, articles returned of those asked for — do not
/// belong here: a line per morning would evict the failures this file exists for within a month,
/// which is the split Post made for the same reason.</para>
/// </summary>
public partial class DiagnosticsPage : ContentPage
{
    private static string CrashLogPath =>
        System.IO.Path.Combine(FileSystem.AppDataDirectory, "crash.log");

    private static string DeliveryLogPath =>
        System.IO.Path.Combine(FileSystem.AppDataDirectory, "delivery.log");

    public DiagnosticsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Render();
    }

    private void Render()
    {
        var log = ReadLog();
        var hasEntries = log.Length > 0;

        LogCard.IsVisible = hasEntries;
        LogLabel.Text = log;

        // The empty state is a sentence, not a blank card: "nothing here" and "this does not
        // work" look identical otherwise, and after the cutover there is no second opinion.
        IntroLabel.Text = hasEntries
            ? "Recent problems captured on this device, oldest first. Use Share to send this to "
              + "whoever is helping fix the app."
            : "No problems have been captured. Anything that goes wrong — including quietly, in "
              + "the background — will be recorded here.";

        CopyButton.IsVisible = hasEntries;
        ShareButton.IsVisible = hasEntries;
        ClearButton.IsVisible = hasEntries;
    }

    private static string ReadLog()
    {
        var parts = new List<string>();
        foreach (var path in new[] { DeliveryLogPath, CrashLogPath })
        {
            if (!System.IO.File.Exists(path))
            {
                continue;
            }

            var text = System.IO.File.ReadAllText(path).TrimEnd();
            if (text.Length > 0)
            {
                parts.Add(text);
            }
        }

        return string.Join("\n\n", parts);
    }

    private async void OnCopyClicked(object sender, EventArgs e)
    {
        await Clipboard.SetTextAsync(ReadLog());
        // Success is an inline state change (DESIGN.md §5), so the button says what it did
        // rather than a toast saying it. It goes back on the next visit.
        CopyButton.Text = "Copied";
    }

    private async void OnShareClicked(object sender, EventArgs e)
    {
        // Shared as files rather than as the merged text, so the timestamps and stack traces
        // arrive exactly as they were written.
        var files = new[] { DeliveryLogPath, CrashLogPath }
            .Where(System.IO.File.Exists)
            .Select(path => new ShareFile(path))
            .ToList();

        if (files.Count == 0)
        {
            return;
        }

        await Share.RequestAsync(new ShareMultipleFilesRequest
        {
            Title = "Foreword diagnostics",
            Files = files,
        });
    }

    private async void OnClearClicked(object sender, EventArgs e)
    {
        // Destructive, so it asks — this is the one place §5's undo-first rule cannot apply,
        // because the thing being deleted is the only record of what went wrong and there is
        // nowhere to hold it while an undo window runs.
        var confirmed = await DisplayAlert(
            "Clear diagnostics?",
            "The record of what has gone wrong on this phone is deleted. There is no adb after "
            + "the cutover, so this is the only copy.",
            "Clear", "Keep");

        if (!confirmed)
        {
            return;
        }

        foreach (var path in new[] { DeliveryLogPath, CrashLogPath })
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }

        Render();
    }
}
