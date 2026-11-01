using Codex.Domain.Diagnostics;
using System.Globalization;
using Android.Text;
using Android.Views;
using Codex.Domain.Design;
using Codex.Domain.Music;
using Codex.Ui;
using Codex.Ui.Voice;

namespace Score;

/// <summary>
/// Settings, gathered from the four screens they were scattered across.
///
/// Built on one rule: <b>a working thing takes almost no space</b>. A signed-in
/// account is a line, not a form; a chosen folder is a line, not a picker. What is
/// broken or unset is what gets room. That is what makes a settings screen readable
/// at a glance instead of a wall of switches you have to check.
///
/// There is deliberately no "whose install is this" setting. The bound Google account
/// <i>is</i> the identity — the Drive folder, the taste profile and the vault section
/// are already keyed to it — so a stored flag could only ever agree with it or be
/// wrong. Deleting the row deleted the worst screen in the design with it.
///
/// There is also no manual update check: a button that usually does nothing teaches
/// you to press it for reassurance rather than for a reason. Rescan is the one
/// exception — it lives here rather than in the Music tab because it is a library
/// operation, not a browsing one, and moving it here freed the Music header for
/// browsing controls only.
/// </summary>
public sealed class SettingsArea : ScoreArea
{
    private readonly MusicLibrary library;

    public SettingsArea(Android.App.Activity host, MusicLibrary library) : base(host)
    {
        this.library = library;

        // So the status line updates live if a scan is already running (or one this
        // screen starts) — the same wiring MusicArea uses.
        library.Changed += Invalidate;
    }

    public override BinderArea Tab => new("Settings", Resource.Drawable.area_settings, PaperTheme.ResolvedMark, PaperTheme.ResolvedFill);

    public override View BuildView()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        Group(column, "Account");
        column.AddView(AccountRow());
        column.AddView(FolderRow());

        Group(column, "Vault");
        column.AddView(PaperUi.Secondary(Host,
            "The one secret that isn't in the vault — set the same passphrase in every app and each " +
            "restores its own keys. Shown in the clear on purpose, so it can be read back and copied."));
        var passphrase = PaperUi.MonoField(Host, "Vault passphrase");
        passphrase.Text = VaultPassphraseStore.Passphrase;
        // Persisted as it is typed rather than on blur: there is no Save on this
        // screen, and leaving by a binder tab never blurs a field.
        passphrase.TextChanged += (_, _) =>
            VaultPassphraseStore.Passphrase = passphrase.Text ?? string.Empty;
        column.AddView(passphrase);

        Group(column, "Library");
        column.AddView(LibraryRow());

        Group(column, "Listening");
        column.AddView(StackSizeRow());

        Group(column, "TIDAL");
        column.AddView(TidalRow());

        Group(column, "Downloads");
        column.AddView(PaperUi.SecondaryButton(Host, "Manage downloads",
            () => Host.StartActivity(new Android.Content.Intent(Host, typeof(DownloadsActivity)))));

        // Only where there is one. A phone with no equalizer had a Sound group whose
        // single button led to a screen that said so — a row spent telling you it is
        // useless, on the screen whose rule is that a working thing takes almost no space.
        if (AudioEffects.HasEqualizer)
        {
            Group(column, "Sound");
            column.AddView(PaperUi.SecondaryButton(Host, "Equalizer",
                () => Host.StartActivity(new Android.Content.Intent(Host, typeof(EqualizerActivity)))));
        }

        Group(column, "Podcasts");
        column.AddView(PaperUi.SecondaryButton(Host, "Manage subscriptions",
            () => Host.StartActivity(new Android.Content.Intent(Host, typeof(ManageSubscriptionsActivity)))));

        Group(column, "Diagnostics");
        column.AddView(PaperUi.Secondary(Host,
            "There is no adb after the cutover, so failures are written here instead of to a log " +
            "nobody can reach."));
        column.AddView(PaperUi.SecondaryButton(Host, "Recent failures",
            () => Host.StartActivity(new Android.Content.Intent(Host, typeof(AudioDiagnosticsActivity)))));

        column.AddView(PaperUi.BuildNumber(Host, "Score"), BuildLineParams());

        var scroll = new ScrollView(Host);
        scroll.AddView(column);
        return scroll;
    }

    // A settled account is a sentence. Only an unset one gets a button.
    private View AccountRow()
    {
        if (MusicSettingsStore.GoogleAccountEmail is { Length: > 0 } email)
        {
            return Settled($"Signed in as {email}", "Sign in again", () => _ = library.SignInAsync());
        }

        return PaperUi.PrimaryButton(Host, "Sign in with Google", () => _ = library.SignInAsync());
    }

    private View FolderRow()
    {
        if (!library.HasFolder)
        {
            return PaperUi.Secondary(Host, "No music folder chosen yet — the Music tab will ask.");
        }

        return Settled("Music folder chosen", "Change", () =>
        {
            MusicSettingsStore.MusicFolderId = null;
            Invalidate();
        });
    }

    // Signing in/out both happen on TidalPairingActivity — sign-out is offered there
    // too (SignOut needs no confirmation dialog per §7, but the browser round trip
    // genuinely needs its own screen). This row only ever needs to say which state
    // that screen last left things in — including, per the credential-health seam
    // every other app's Settings already uses, whether the vault actually has a
    // TIDAL client id/secret to sign in with at all.
    private View TidalRow()
    {
        var health = new CredentialHealthStore().Load().Get(Codex.Domain.Credentials.CredentialIds.Tidal);

        if (new TidalAccountStore(Host).Load() is null)
        {
            var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
            column.AddView(PaperUi.PrimaryButton(Host, "Sign in to TIDAL",
                () => Host.StartActivity(new Android.Content.Intent(Host, typeof(TidalPairingActivity)))));
            if (health.Status == Storyworthtelling.Voice.Health.CredentialStatus.Failing)
            {
                column.AddView(PaperUi.Secondary(Host, health.Message));
            }

            return column;
        }

        return Settled("Signed in to TIDAL", "Manage",
            () => Host.StartActivity(new Android.Content.Intent(Host, typeof(TidalPairingActivity))));
    }

    // Idle shows the button; a running scan shows its own status instead — pressing
    // Rescan while one is already going would just enqueue-with-Keep and do nothing,
    // which reads worse than not offering the button at all. A large library can take
    // several chained WorkManager runs to finish (see MusicScanWorker); the status line
    // tracks that automatically, the same way the Music tab used to.
    private View LibraryRow()
    {
        if (!library.HasFolder)
        {
            return PaperUi.Secondary(Host, "Choose a music folder in the Music tab first.");
        }

        return string.IsNullOrEmpty(library.Status)
            ? PaperUi.SecondaryButton(Host, "Rescan", () => KeyActs.Run(
                TapWatch.Action(ScoreKeyActions.Rescan, KeyActs.Local), () => _ = library.ScanAsync()))
            : PaperUi.Secondary(Host, library.Status);
    }

    // Two counts, typed in rather than picked from a ladder: the owner knows the number
    // they want, and a list of rungs only ever offered a handful of them. Each is saved as
    // it is typed, clamped on the way in, for the same reason the vault passphrase is —
    // there is no Save on this screen, and leaving by a binder tab never blurs a field.
    private View StackSizeRow()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.AddView(CountRow("Songs and albums",
            MusicSettingsStore.FridayMusicCount, count => MusicSettingsStore.FridayMusicCount = count));
        column.AddView(CountRow("Podcast shows",
            MusicSettingsStore.FridayPodcastCount, count => MusicSettingsStore.FridayPodcastCount = count));
        column.AddView(PaperUi.Secondary(Host,
            $"How many of each the Friday crate holds, from {FridayStack.Smallest} to {FridayStack.Largest}. Zero leaves that kind out."));
        return column;
    }

    private View CountRow(string label, int inForce, Action<int> save)
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetMinimumHeight(PaperTheme.Dp(Host, DesignTokens.Shape.TouchTargetDp));
        row.AddView(PaperUi.Body(Host, label),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var field = PaperUi.Field(Host, "0");
        field.InputType = InputTypes.ClassNumber;
        field.Text = inForce.ToString(CultureInfo.InvariantCulture);
        // A half-typed or cleared field is not a value: only a number that parses is saved,
        // and anything outside the range is held to it rather than refused.
        field.TextChanged += (_, _) =>
        {
            if (int.TryParse(field.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                save(FridayStack.Clamp(count));
            }
        };
        row.AddView(field, new LinearLayout.LayoutParams(PaperTheme.Dp(Host, CountFieldDp), ViewGroup.LayoutParams.WrapContent));
        return row;
    }

    private const int CountFieldDp = 96;

    // The shape a working thing takes: what is true, and the way to change it.
    private View Settled(string state, string action, Action onAction)
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetMinimumHeight(PaperTheme.Dp(Host, DesignTokens.Shape.TouchTargetDp));

        var text = PaperUi.Body(Host, state);
        text.SetSingleLine(true);
        text.Ellipsize = Android.Text.TextUtils.TruncateAt.Middle;
        row.AddView(text, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(PaperUi.QuietButton(Host, action, onAction));
        return row;
    }

    // The quiet foot line: which build is actually on the phone. The channel installs
    // silently and most updates change nothing visible, so without it "did that land?" has
    // no answer on the device.
    private LinearLayout.LayoutParams BuildLineParams()
    {
        var lp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lp.TopMargin = PaperTheme.Dp(Host, DesignTokens.Space.X8);
        return lp;
    }

    private void Group(LinearLayout column, string name)
    {
        var heading = PaperUi.Label(Host, name.ToUpperInvariant());
        heading.SetTextColor(PaperTheme.InkMuted);
        heading.SetPadding(0, PaperTheme.Dp(Host, DesignTokens.Space.X6), 0, PaperTheme.Dp(Host, DesignTokens.Space.X1));
        column.AddView(heading);
    }
}
