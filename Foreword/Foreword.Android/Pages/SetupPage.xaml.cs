using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Foreword.Domain.Health;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;

namespace Foreword.Android.Pages;

/// <summary>
/// S1. Settings, on Flyleaf's group/card furniture.
///
/// <para>Two rules decide what is on it. <b>A working credential does not appear at all</b> —
/// not collapsed to a "Working" line, absent: the vault backs a key up and restores it, so a
/// healthy one has nothing to ask and nothing to show, and the row itself is the clutter. The
/// coloured <i>Needs you</i> group is built here rather than in XAML precisely because on a good
/// morning it has nothing in it and does not exist.</para>
///
/// <para><b>And there is no Save.</b> Every value commits as it changes, which is §5's inline
/// state change and the reason Score gives for its own vault field: leaving a screen never
/// blurs anything reliably, so a Save button is one that usually does nothing.</para>
/// </summary>
public partial class SetupPage : ContentPage
{
    public SetupPage()
    {
        InitializeComponent();

        // The one shared formatter, so every app's foot line reads identically.
        BuildLineLabel.Text = Storyworthtelling.Updates.BuildLine.Format(
            "Foreword", Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString,
            Microsoft.Maui.ApplicationModel.AppInfo.Current.PackageName);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Read from the worker's own constants, so the screen cannot state a delivery time the
        // schedule does not use.
        var scheduled = new DateTime(1, 1, 1, DeliveryWorker.DeliveryHour, DeliveryWorker.DeliveryMinute, 0);
        DeliveryTimeLabel.Text = scheduled.ToString("h:mm tt").ToLowerInvariant();

        DestinationLabel.Text = string.IsNullOrEmpty(CredentialStore.GoogleAccountEmail)
            ? "Your reMarkable"
            : $"Your reMarkable · {CredentialStore.GoogleAccountEmail}";

        LastDeliveryCaption.Text = CredentialStore.LastDeliverySummary;

        RemarkableStatusLabel.Text = string.IsNullOrEmpty(CredentialStore.RemarkableDeviceToken)
            ? "Not paired — documents are built and held until it is"
            : "Paired";
        RemarkableReplaceButton.Text = string.IsNullOrEmpty(CredentialStore.RemarkableDeviceToken)
            ? "Pair"
            : "Re-pair";

        GoogleStatusLabel.Text = CredentialStore.IsGoogleConfigured
            ? CredentialStore.GoogleAccountEmail
            : "Not signed in";

        VaultPassphraseEntry.Text = CredentialStore.VaultPassphrase;

        var mapped = new CalendarSettings();
        mapped.Reload();
        var count = mapped.Mapping.CalendarColumns.Count
            + mapped.Mapping.SplitCalendars.Count
            + mapped.Mapping.EveryoneCalendars.Count
            + mapped.Mapping.NotPrintedCalendars.Count
            + (string.IsNullOrEmpty(mapped.Mapping.FamilyCalendarId) ? 0 : 1);
        CalendarSummaryLabel.Text = count == 0
            ? "None set up yet — nothing will print"
            : $"{count} set up";

        RenderMaps();
        BuildNeedsYou();
    }

    // ---- Weather & drives ==================================================

    /// <summary>
    /// The Maps key field follows the same rule as every other credential in the suite: it is shown
    /// only when it is missing or a live call with it was just rejected, and a working key collapses
    /// to a line and a Replace button. A key that works is a done state, not something to keep on
    /// screen — the vault backs it up, so it never has to be read back off this page.
    /// </summary>
    private void RenderMaps()
    {
        var state = CredentialFieldState.Decide(
            !string.IsNullOrEmpty(CredentialStore.MapsApiKey),
            CredentialHealth().Get(CredentialIds.Maps));

        MapsKeyBlock.IsVisible = state.ShowEntry || replacingMapsKey;
        MapsKeyWorkingRow.IsVisible = !state.ShowEntry && !replacingMapsKey;

        if (MapsKeyBlock.IsVisible)
        {
            MapsKeyEntry.Text = CredentialStore.MapsApiKey;
        }

        HomeAddressEntry.Text = CredentialStore.HomeAddress;
        HomePointEntry.Text = CredentialStore.HomeLatitude == 0 && CredentialStore.HomeLongitude == 0
            ? string.Empty
            : $"{CredentialStore.HomeLatitude}, {CredentialStore.HomeLongitude}";
    }

    private void OnReplaceMapsKey(object sender, EventArgs e)
    {
        replacingMapsKey = true;
        RenderMaps();
        MapsKeyEntry.Focus();
    }

    /// <summary>
    /// Committed as it is typed, like every other field on this screen — there is no Save, and a
    /// value the owner typed and then navigated away from is exactly the one they meant to set.
    /// </summary>
    private void OnMapsKeyChanged(object sender, TextChangedEventArgs e) =>
        CredentialStore.MapsApiKey = (e.NewTextValue ?? string.Empty).Trim();

    private void OnHomeAddressChanged(object sender, TextChangedEventArgs e) =>
        CredentialStore.HomeAddress = (e.NewTextValue ?? string.Empty).Trim();

    /// <summary>
    /// "40.8259, -74.2090" — one field rather than two, because a point is one thing to copy out of
    /// a map and two fields is two chances to paste half of it. A value that is not a pair is
    /// ignored rather than half-applied: a latitude saved without its longitude would put the
    /// household's forecast on a meridian.
    /// </summary>
    private void OnHomePointChanged(object sender, TextChangedEventArgs e)
    {
        var parts = (e.NewTextValue ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length == 2
            && double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var latitude)
            && double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var longitude))
        {
            CredentialStore.HomeLatitude = latitude;
            CredentialStore.HomeLongitude = longitude;
        }
    }

    private bool replacingMapsKey;

    // ---- Needs you =========================================================

    /// <summary>
    /// One row per credential that is missing or was just rejected, and nothing at all when
    /// none is. <see cref="CredentialFieldState.Decide"/> already computes exactly this; what
    /// changed in P9 is that a healthy key now collapses to <i>nothing</i> rather than to a
    /// "Working ✓" line.
    /// </summary>
    private void BuildNeedsYou()
    {
        NeedsYouRows.Clear();
        var report = CredentialHealth();

        var wanted = new (string Id, string Title, string Hint, Func<string> Read, Action<string> Write)[]
        {
            (CredentialIds.NytCookie, "New York Times — session cookie",
                "Log into nytimes.com, open DevTools → Application → Cookies, copy the NYT-S value.",
                () => CredentialStore.NytSessionCookie, v => CredentialStore.NytSessionCookie = v),
            (CredentialIds.NytApiKey, "New York Times — API key",
                "From developer.nytimes.com. Used to fetch the top stories list.",
                () => CredentialStore.NytApiKey, v => CredentialStore.NytApiKey = v),
            (CredentialIds.Anthropic, "Claude — handwriting",
                "Optional. Without it, notes written on the paper are not read back.",
                () => CredentialStore.AnthropicApiKey, v => CredentialStore.AnthropicApiKey = v),
        };

        var shown = 0;
        foreach (var (id, title, hint, read, write) in wanted)
        {
            var state = CredentialFieldState.Decide(!string.IsNullOrEmpty(read()), report.Get(id));
            if (!state.ShowEntry)
            {
                continue;
            }

            if (shown > 0)
            {
                NeedsYouRows.Add(new BoxView
                {
                    HeightRequest = 1,
                    Color = LookupColor("Hairline"),
                });
            }

            NeedsYouRows.Add(BuildNeedsYouRow(title, state.StatusText, hint, read(), write));
            shown++;
        }

        NeedsYouLabel.IsVisible = shown > 0;
        NeedsYouCard.IsVisible = shown > 0;
    }

    private View BuildNeedsYouRow(
        string title, string status, string hint, string current, Action<string> write)
    {
        var entry = new Entry
        {
            Text = current,
            Placeholder = "Paste it here",
            Style = LookupStyle("MonoEntry"),
        };

        // Committed as it is typed. There is no Save on this screen, so anything waiting for
        // one would be lost by leaving — which, for a credential the owner has just pasted in,
        // is the worst possible moment to lose it.
        entry.TextChanged += (_, e) => write(e.NewTextValue?.Trim() ?? string.Empty);
        entry.Unfocused += async (_, _) => await PublishToVaultAsync();

        return new VerticalStackLayout
        {
            Padding = new Thickness(16, 12),
            Spacing = 8,
            Children =
            {
                new Label { Text = title, Style = LookupStyle("CardRowTitle"), TextColor = LookupColor("Accent") },
                new Label { Text = status, Style = LookupStyle("CardRowDescription") },
                entry,
                new Label { Text = hint, Style = LookupStyle("CardRowDescription") },
            },
        };
    }

    private static CredentialHealthReport CredentialHealth() =>
        IPlatformApplication.Current!.Services.GetRequiredService<CredentialHealthStore>().Load();

    // Styles and colours live in the app-wide merged dictionaries, which a page's own Resources
    // indexer does not search; TryGetValue on the application resources walks them.
    private static Style? LookupStyle(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as Style : null;

    private static Color LookupColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Transparent;

    // ---- Vault =============================================================

    private void OnVaultPassphraseChanged(object sender, TextChangedEventArgs e)
    {
        // Persisted as it is typed rather than on blur, exactly as Score does: leaving this
        // screen never reliably blurs the field, and the passphrase is the one secret the vault
        // cannot restore if it is lost.
        CredentialStore.VaultPassphrase = e.NewTextValue ?? string.Empty;
    }

    /// <summary>
    /// Publishes this app's section to the encrypted Drive vault, and speaks up only when a
    /// passphrase was set and the backup still did not land. A no-passphrase skip stays quiet:
    /// the app simply is not in the vault yet.
    /// </summary>
    private async Task PublishToVaultAsync()
    {
        SecretsVaultPublishResult result;
        try
        {
            var sync = IPlatformApplication.Current!.Services.GetService<VaultSync>();
            if (sync is null)
            {
                return;
            }

            result = await sync.PublishAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = SecretsVaultPublishResult.WriteFailed(ex.Message);
        }

        if (result.NeedsAttention)
        {
            await DisplayAlert("Vault backup didn't complete", result.Message, "OK");
        }
    }

    // ---- Where it goes =====================================================

    private void OnReplaceRemarkableClicked(object sender, EventArgs e)
    {
        RemarkableFields.IsVisible = !RemarkableFields.IsVisible;
        if (RemarkableFields.IsVisible)
        {
            RemarkableTokenEntry.Focus();
        }
    }

    private async void OnRegisterRemarkableClicked(object sender, EventArgs e)
    {
        var code = RemarkableTokenEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        try
        {
            // Pair through the shared library rather than an inline POST.
            var registration = IPlatformApplication.Current!.Services
                .GetRequiredService<Storyworthtelling.Remarkable.IRemarkableRegistration>();
            var token = await registration.Register(code);

            CredentialStore.RemarkableDeviceToken = token;
            // Back up the freshly paired token so a later reset does not force re-registration.
            await PublishToVaultAsync();

            // Success is the state change, not an alert: the row now says Paired.
            RemarkableFields.IsVisible = false;
            RemarkableTokenEntry.Text = string.Empty;
            RemarkableStatusLabel.Text = "Paired";
            RemarkableReplaceButton.Text = "Re-pair";
        }
        catch (HttpRequestException ex)
        {
            RemarkableStatusLabel.Text =
                $"Pairing refused ({(int?)ex.StatusCode}) — the code may have expired. Ask for a new one.";
        }
        catch (Exception ex)
        {
            RemarkableStatusLabel.Text = $"Pairing failed — {ex.Message}";
        }
    }

    private async void OnGoogleSignInClicked(object sender, EventArgs e)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            GoogleStatusLabel.Text = "Could not start sign-in — no active window.";
            return;
        }

        try
        {
            var email = await Foreword.Android.Google.DeviceAccountSignIn.SignIn(activity);
            GoogleStatusLabel.Text = email;
            DestinationLabel.Text = $"Your reMarkable · {email}";
        }
        catch (Exception ex)
        {
            var fullMessage = FlattenException(ex);
            GoogleStatusLabel.Text = "Sign-in failed — details copied to the clipboard.";
            await Clipboard.SetTextAsync(fullMessage);
        }
    }

    private static string FlattenException(Exception ex)
    {
        var parts = new System.Text.StringBuilder();
        var current = ex;
        while (current != null)
        {
            parts.AppendLine(current.Message);
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                parts.AppendLine(current.StackTrace);
            }

            current = current.InnerException;
            if (current != null)
            {
                parts.AppendLine("Caused by:");
            }
        }

        return parts.ToString().Trim();
    }

    // ---- Advanced ==========================================================

    private async void OnSendTestPageClicked(object sender, EventArgs e)
    {
        // Exists for the on-device test plan: waiting until 6:30 tomorrow to find out whether
        // pairing worked is not a test, it is a wait.
        TestPageButton.Text = "Sending…";
        try
        {
            var delivery = IPlatformApplication.Current!.Services
                .GetRequiredService<Foreword.Infrastructure.Delivery.CrosswordDeliveryService>();
            var outcome = await delivery.DeliverDailyPuzzle(DateOnly.FromDateTime(DateTime.Today));
            TestPageButton.Text = "Send";
            LastDeliveryCaption.Text = outcome;
        }
        catch (Exception ex)
        {
            TestPageButton.Text = "Send";
            LastDeliveryCaption.Text = $"Test page failed — {ex.Message}";
        }
    }

    private async void OnCalendarsClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new CalendarsPage());

    private async void OnDiagnosticsClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new DiagnosticsPage());
}
