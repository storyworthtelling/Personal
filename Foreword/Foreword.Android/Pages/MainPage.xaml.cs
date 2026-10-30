using Foreword.Domain.Calendar;
using Microsoft.Extensions.DependencyInjection;
using Foreword.Infrastructure.Delivery;
using Storyworthtelling.Maui.State;
using Storyworthtelling.Maui.Views;
using Storyworthtelling.Voice;

namespace Foreword.Android.Pages;

public partial class MainPage : BinderPage
{
    private static string CrashLogPath =>
        System.IO.Path.Combine(FileSystem.AppDataDirectory, "crash.log");

    private static string DeliveryLogPath =>
        System.IO.Path.Combine(FileSystem.AppDataDirectory, "delivery.log");

    private readonly DriveVoiceOptions voiceOptions;
    private readonly VoicedMessenger voice;

    private readonly Button deliverNow = new() { Text = "Deliver now" };

    private readonly DogEarState dogEarState = new();
    private readonly DogEar dogEar;

    public MainPage(DriveVoiceOptions voiceOptions, VoicedMessenger voice)
    {
        InitializeComponent();
        this.voiceOptions = voiceOptions;
        this.voice = voice;

        dogEar = new DogEar(dogEarState);
        dogEar.NoticesOpened += OnNoticesOpened;
        NoticeCorner.Content = dogEar;

        // Today is the app's root, so its tab is the one lit when nothing else is. The bar carries
        // the one thing this screen is for; Tasks and Settings used to sit here as quiet buttons,
        // and they are the binder's job now.
        Wrap(ForewordArea.Today, "Today");
        deliverNow.Clicked += OnDeliverNowClicked;
        SetBar(deliverNow);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StatusLabel.Text = CredentialStore.LastDeliverySummary;
        RaiseUnreadableCalendarNotice();
        BuildFirstRun();
        DeliveryLogButton.IsVisible = System.IO.File.Exists(DeliveryLogPath) &&
                                      new System.IO.FileInfo(DeliveryLogPath).Length > 0;
        CrashLogButton.IsVisible = System.IO.File.Exists(CrashLogPath);

        var batteryOptimized = IsBatteryOptimized();
        BatteryWarningLabel.IsVisible = batteryOptimized;
        BatterySettingsButton.IsVisible = batteryOptimized;

        _ = ShowWelcomeAsync();
    }

    // The notice is the stored list of calendars the last morning skipped. It is raised from the store
    // every time the page appears, so a morning that reads everything clears it without any other step.
    private void RaiseUnreadableCalendarNotice()
    {
        var detail = UnreadableCalendarNotice.Detail(UnreadableCalendarNotice.Names(CredentialStore.UnreadableCalendars));
        dogEarState.SetNotice(
            UnreadableCalendarNotice.Key,
            detail is null ? null : new DogEarNotice("A calendar was skipped", detail, DogEarHue.Caution));
        NoticeCorner.IsVisible = detail is not null;
        dogEar.Refresh();
    }

    // Opening the dog-ear turns the page to the notices the kit collects; the same state the corner shows.
    private void OnNoticesOpened(IReadOnlyList<DogEarNotice> opened) =>
        _ = Navigation.PushAsync(new WaitingPage(dogEarState));

    // A greeting composed fresh in the owner's voice, shown only once a shared voice profile exists. Pulls
    // the Claude key from the shared Drive config first (RefreshAsync); everything degrades to a hidden
    // label when the profile, key, or Drive access isn't there — never blocks or crashes the page.
    private async Task ShowWelcomeAsync()
    {
        if (!CredentialStore.IsGoogleConfigured)
        {
            WelcomeLabel.IsVisible = false;
            return;
        }

        string message;
        try
        {
            await voiceOptions.RefreshAsync(CancellationToken.None);
            message = await voice.ComposeAsync(
                new MessageRequest("welcome", "the owner is checking their morning brief", HighVisibility: true),
                fallback: string.Empty, CancellationToken.None);
        }
        catch
        {
            message = string.Empty;
        }

        WelcomeLabel.Text = message;
        WelcomeLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private static bool IsBatteryOptimized()
    {
        var pm = global::Android.App.Application.Context
            .GetSystemService(global::Android.Content.Context.PowerService)
            as global::Android.OS.PowerManager;
        return pm != null &&
               !pm.IsIgnoringBatteryOptimizations(global::Android.App.Application.Context.PackageName);
    }

    private void OnBatterySettingsClicked(object sender, EventArgs e)
    {
        var packageName = global::Android.App.Application.Context.PackageName;
        var intent = new global::Android.Content.Intent(
            global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
        intent.SetData(global::Android.Net.Uri.Parse($"package:{packageName}"));
        Platform.CurrentActivity!.StartActivity(intent);
    }

    private async void OnDeliverNowClicked(object sender, EventArgs e)
    {
        if (!CredentialStore.IsConfigured)
        {
            // The first-run card on screen already says what is missing; keep quiet here.
            BuildFirstRun();
            return;
        }

        DeliveryFileLogger.Clear();
        deliverNow.IsEnabled = false;
        // Loading is words (DESIGN.md §5).
        StatusLabel.Text = "Delivering — fetching today's brief…";

        try
        {
            await Task.Run(async () =>
            {
                using var scope = IPlatformApplication.Current!.Services.CreateScope();
                var delivery = scope.ServiceProvider.GetRequiredService<CrosswordDeliveryService>();
                var today = DateOnly.FromDateTime(DateTime.Today);
                var newsStatus = await delivery.DeliverDailyPuzzle(today);

                if (CrosswordDeliveryService.IsFirstSundayOfMonth(today))
                    await delivery.DeliverBonusPuzzle(today);

                if (CredentialStore.IsGoogleConfigured)
                {
                    var daily = scope.ServiceProvider.GetRequiredService<DailyDeliveryService>();
                    await daily.EnsureColumnCalendars();
                    var unreadable = await daily.DeliverDailyDocuments(today);
                    CredentialStore.UnreadableCalendars = UnreadableCalendarNotice.Store(unreadable);
                }

                CredentialStore.LastDeliveryOutcome = $"Delivered ({newsStatus})";
                CredentialStore.LastDeliveryAtUtc = DateTime.UtcNow;
            });

            // Success is the state change: the status line is the record.
            StatusLabel.Text = CredentialStore.LastDeliverySummary;
            DeliveryLogButton.IsVisible = true;
        }
        catch (Exception ex)
        {
            var fullMessage = FlattenException(ex);
            CredentialStore.LastDeliveryOutcome = $"Failed: {ex.Message}";
            CredentialStore.LastDeliveryAtUtc = DateTime.UtcNow;
            StatusLabel.Text = CredentialStore.LastDeliverySummary;
            DeliveryLogButton.IsVisible = true;
            await Clipboard.SetTextAsync(fullMessage);
            await DisplayAlert("Delivery Failed (copied to clipboard)", fullMessage, "OK");
        }
        finally
        {
            deliverNow.IsEnabled = true;
        }
    }

    /// <summary>
    /// F1. What Foreword has, and what it is still waiting for.
    ///
    /// <para>There is no wizard and no blocking step, because the app is <b>useful part-way</b>:
    /// with Google alone it can already build the day from the calendar and tasks, an unpaired
    /// reMarkable does not stop the morning (the documents are built and held), and no New York
    /// Times key means a thinner brief rather than a failure. So this lists state and gets out
    /// of the way — it disappears entirely once there is nothing left to say.</para>
    /// </summary>
    private void BuildFirstRun()
    {
        var rows = new (bool Done, string Title, string Detail)[]
        {
            (CredentialStore.IsGoogleConfigured, "Google",
                CredentialStore.IsGoogleConfigured
                    ? CredentialStore.GoogleAccountEmail
                    : "Your calendar and tasks — the day cannot be built without it"),
            (!string.IsNullOrEmpty(CredentialStore.VaultPassphrase), "The vault",
                string.IsNullOrEmpty(CredentialStore.VaultPassphrase)
                    ? "One passphrase, and the rest of the keys restore themselves"
                    : "Restoring this phone's keys from Drive"),
            (!string.IsNullOrEmpty(CredentialStore.RemarkableDeviceToken), "Your reMarkable",
                string.IsNullOrEmpty(CredentialStore.RemarkableDeviceToken)
                    ? "Not paired — documents are built and held until it is"
                    : "Paired"),
            (!string.IsNullOrEmpty(CredentialStore.NytApiKey), "The New York Times",
                string.IsNullOrEmpty(CredentialStore.NytApiKey)
                    ? "Optional — feeds alone still make a brief"
                    : "Top stories"),
        };

        var done = rows.Count(r => r.Done);
        FirstRunSection.IsVisible = done < rows.Length;
        if (!FirstRunSection.IsVisible)
        {
            return;
        }

        FirstRunIntro.Text =
            "Foreword builds the morning and puts it on your reMarkable. It needs four things, "
            + "and keeps working as each one arrives.";

        FirstRunRows.Clear();
        var first = true;
        foreach (var (rowDone, title, detail) in rows)
        {
            if (!first)
            {
                FirstRunRows.Add(new BoxView { HeightRequest = 1, Color = LookupColor("Hairline") });
            }

            first = false;

            var row = new Grid
            {
                Padding = new Thickness(16, 12),
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(24)),
                    new ColumnDefinition(GridLength.Star),
                },
            };

            // A tick or nothing. An unfinished row is not a failure and must not wear a cross:
            // three of these four are optional and the app runs without them.
            var mark = new Label
            {
                Text = rowDone ? "\u2713" : string.Empty,
                Style = LookupStyle("CardRowTitle"),
                TextColor = LookupColor("Accent"),
            };
            Grid.SetColumn(mark, 0);
            row.Add(mark);

            var text = new VerticalStackLayout
            {
                Children =
                {
                    new Label { Text = title, Style = LookupStyle("CardRowTitle") },
                    new Label { Text = detail, Style = LookupStyle("CardRowDescription") },
                },
            };
            Grid.SetColumn(text, 1);
            row.Add(text);

            FirstRunRows.Add(row);
        }

        FirstRunCaption.Text = $"{done} of {rows.Length}.";
    }

    private static Style? LookupStyle(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as Style : null;

    private static Color LookupColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Transparent;

    private static string FlattenException(Exception ex)
    {
        var parts = new System.Text.StringBuilder();
        var current = ex;
        while (current != null)
        {
            parts.AppendLine(current.Message);
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
                parts.AppendLine(current.StackTrace);
            current = current.InnerException;
            if (current != null) parts.AppendLine("Caused by:");
        }
        return parts.ToString().Trim();
    }

    private async void OnDeliveryLogClicked(object sender, EventArgs e)
    {
        if (!System.IO.File.Exists(DeliveryLogPath))
        {
            await DisplayAlert("Delivery Log", "No delivery log found.", "OK");
            return;
        }

        var log = System.IO.File.ReadAllText(DeliveryLogPath);
        if (string.IsNullOrWhiteSpace(log))
        {
            await DisplayAlert("Delivery Log", "Log is empty.", "OK");
            return;
        }

        var action = await DisplayActionSheet("Delivery Log", "Cancel", null, "View & Copy", "Share");
        if (action == "View & Copy")
        {
            var display = log.Length > 3000 ? "(truncated — use Share for full log)\n\n" + log[^3000..] : log;
            await Clipboard.SetTextAsync(log);
            await DisplayAlert("Delivery Log (copied to clipboard)", display, "OK");
        }
        else if (action == "Share")
        {
            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Foreword Delivery Log",
                File = new ShareFile(DeliveryLogPath)
            });
        }
    }

    private async void OnCrashLogClicked(object sender, EventArgs e)
    {
        if (!System.IO.File.Exists(CrashLogPath))
        {
            await DisplayAlert("Crash Log", "No crash log found.", "OK");
            return;
        }

        var log = System.IO.File.ReadAllText(CrashLogPath);

        var action = await DisplayActionSheet("Crash Log", "Cancel", null, "View & Copy", "Share", "Clear");
        if (action == "View & Copy")
        {
            var display = log.Length > 3000 ? "(truncated — use Share for full log)\n\n" + log[^3000..] : log;
            await Clipboard.SetTextAsync(log);
            await DisplayAlert("Crash Log (copied to clipboard)", display, "OK");
        }
        else if (action == "Share")
        {
            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Foreword Crash Log",
                File = new ShareFile(CrashLogPath)
            });
        }
        else if (action == "Clear")
        {
            System.IO.File.Delete(CrashLogPath);
            CrashLogButton.IsVisible = false;
        }
    }
}
