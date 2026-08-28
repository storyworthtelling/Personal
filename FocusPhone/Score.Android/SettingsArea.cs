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
/// There is also no manual rescan and no manual update check: buttons that usually
/// do nothing teach you to press them for reassurance rather than for a reason.
/// </summary>
public sealed class SettingsArea(Android.App.Activity host, MusicLibrary library) : ScoreArea(host)
{
    public override BinderArea Tab => new("Settings", Resource.Drawable.area_settings, PaperTheme.ScoreMark);

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

        Group(column, "Listening");
        column.AddView(StackSizeRow());

        Group(column, "Downloads");
        column.AddView(PaperUi.SecondaryButton(Host, "Manage downloads",
            () => Host.StartActivity(new Android.Content.Intent(Host, typeof(DownloadsActivity)))));

        Group(column, "Sound");
        column.AddView(PaperUi.SecondaryButton(Host, "Equalizer",
            () => Host.StartActivity(new Android.Content.Intent(Host, typeof(EqualizerActivity)))));

        Group(column, "Podcasts");
        column.AddView(PaperUi.SecondaryButton(Host, "Manage subscriptions",
            () => Host.StartActivity(new Android.Content.Intent(Host, typeof(ManageSubscriptionsActivity)))));

        Group(column, "Diagnostics");
        column.AddView(PaperUi.Secondary(Host,
            "There is no adb after the cutover, so failures are written here instead of to a log " +
            "nobody can reach."));
        column.AddView(PaperUi.SecondaryButton(Host, "Recent failures",
            () => Host.StartActivity(new Android.Content.Intent(Host, typeof(AudioDiagnosticsActivity)))));

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

    private View StackSizeRow()
    {
        var sizes = Enumerable
            .Range(0, ((FridayStack.Largest - FridayStack.Smallest) / 5) + 1)
            .Select(step => FridayStack.Smallest + (step * 5))
            .ToList();

        var current = sizes.IndexOf(FridayStack.Clamp(MusicSettingsStore.FridayStackSize));
        var picker = new AccordionPicker(
            Host,
            "Friday crate:",
            sizes.Select(size => new PickerOption($"{size} records", Detail(size))),
            current < 0 ? sizes.IndexOf(FridayStack.Default) : current);
        picker.Chosen += index => MusicSettingsStore.FridayStackSize = sizes[index];
        return picker;
    }

    private static string? Detail(int size) => size switch
    {
        FridayStack.Smallest => "A short run — a few things worth hearing.",
        FridayStack.Default => "The default. A fortnight's records for a week, so there is room to skip some.",
        FridayStack.Largest => "As many as the crate holds. Nothing carries over, so most of it goes unheard.",
        _ => null,
    };

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

    private void Group(LinearLayout column, string name)
    {
        var heading = PaperUi.Label(Host, name.ToUpperInvariant());
        heading.SetTextColor(PaperTheme.InkMuted);
        heading.SetPadding(0, PaperTheme.Dp(Host, DesignTokens.Space.X6), 0, PaperTheme.Dp(Host, DesignTokens.Space.X1));
        column.AddView(heading);
    }
}
