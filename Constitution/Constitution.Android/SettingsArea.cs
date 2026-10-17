using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Codex.Ui.Voice;
using Constitution.Domain.Areas;
using Constitution.Domain.Credentials;
using Constitution.Domain.Notices;
using Constitution.Domain.Settings;
using Constitution.Services;
using Constitution.Settings;
using Storyworthtelling.Remarkable;

namespace Constitution;

/// <summary>
/// The Settings tab (book p.17), plain by decision (D71): no texture and nothing laid on the page.
/// Three inline accordion groups (Pillars, Equipment, Connections), then the vault passphrase in the
/// clear in mono, then Export diagnostics as a quiet button, then the build line, which is the only
/// way to answer "did that update land?" on a phone with no adb. The page is drawn from
/// <see cref="SettingsModel"/>; this file only paints it and writes what the owner changes.
///
/// <para>The Household group (C-8.1.2) arrives with the household file and is not drawn. NYT Cooking
/// is not built (spike D96). Success is an inline change of the text, never a toast.</para>
/// </summary>
public sealed class SettingsArea(Activity host)
    : ConstitutionAreaBase(host, "settings", Resource.Drawable.area_settings)
{
    private const string PillarsGroup = "pillars";
    private const string EquipmentGroup = "equipment";
    private const string ConnectionsGroup = "connections";

    private readonly PillarStore pillarStore = new(host);
    private readonly EquipmentStore equipmentStore = new(host);
    private readonly ConstitutionConnections connections = new(host);
    private readonly VoiceGoogleAccount googleAccount = new();

    // Which group is open, which connection row is showing its entry, and the one inline sentence
    // each act leaves behind. Held here so a rebuild does not undo them.
    private string? openGroup;
    private string? entryOpenFor;
    private string? signInStatus;
    private string? exportStatus;
    private readonly Dictionary<string, string> rowNotes = [];

    private EditText? passphraseField;
    private EditText? firstEntry;
    private EditText? secondEntry;

    /// <summary>The shell forwards the account picker's result here; see <see cref="VoiceGoogleAccount.HandleChooserResult"/>.</summary>
    public void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == VoiceGoogleAccount.RequestCode)
        {
            VoiceGoogleAccount.HandleChooserResult(resultCode, data);
        }
    }

    public override void Pause() => CommitPassphrase();

    protected override View BuildPage()
    {
        var inputs = ConnectionInputs();
        var model = SettingsModel.Build(pillarStore.Read(), equipmentStore.Read(), inputs);
        // A broken connection lights the dog-ear's notice; a mended one takes it away.
        ConnectionNoticeBoard.With(board => SettingsModel.ApplyNotices(board, inputs, DateTimeOffset.UtcNow));

        var scroll = new ScrollView(Host);
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        AddGroup(column, PillarsGroup, "Pillars", model.PillarsSummary, BuildPillars(model));
        AddGroup(column, EquipmentGroup, "Equipment", model.EquipmentSummary, BuildEquipment(model));
        AddGroup(column, ConnectionsGroup, "Connections", model.ConnectionsSummary, BuildConnections(model));

        column.AddView(Spaced(PaperUi.Label(Host, "Vault passphrase"), DesignTokens.Space.X6, DesignTokens.Space.X1));
        column.AddView(BuildPassphrase(model.Passphrase), Spaced(DesignTokens.Space.X1));
        column.AddView(PaperUi.Secondary(Host, model.Passphrase.Helper), Spaced(DesignTokens.Space.X1));
        if (!VoiceGoogleAccount.IsSignedIn)
        {
            column.AddView(PaperUi.Secondary(Host, signInStatus ?? "The vault needs a Google sign-in."), Spaced(DesignTokens.Space.X3));
            column.AddView(PaperUi.SecondaryButton(Host, "Sign in with Google", () => _ = SignInAsync()), Spaced(DesignTokens.Space.X1));
        }

        column.AddView(PaperUi.QuietButton(Host, exportStatus is null ? model.ExportLabel : "Export diagnostics again", ExportDiagnostics),
            Spaced(DesignTokens.Space.X6));
        if (exportStatus is not null)
        {
            column.AddView(PaperUi.Secondary(Host, exportStatus), Spaced(DesignTokens.Space.X1));
        }

        column.AddView(PaperUi.BuildNumber(Host, "Constitution"));

        scroll.AddView(column);
        return scroll;
    }

    // ---- groups ----

    private void AddGroup(LinearLayout column, string key, string title, string summary, View body)
    {
        var group = new SettingsGroup(Host, title, summary, openGroup == key, () =>
        {
            CommitPassphrase();
            openGroup = openGroup == key ? null : key;
            entryOpenFor = null;
            RaiseChanged();
        }, body);
        column.AddView(group, Spaced(DesignTokens.Space.X3));
    }

    private View BuildPillars(SettingsModel model)
    {
        var body = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        foreach (var row in model.PillarRows)
        {
            var line = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
            line.SetGravity(GravityFlags.CenterVertical);

            var words = new LinearLayout(Host) { Orientation = Orientation.Vertical };
            words.AddView(PaperUi.Body(Host, row.Name));
            if (row.Caveat is not null)
            {
                words.AddView(PaperUi.Secondary(Host, row.Caveat));
            }

            line.AddView(words, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
            var id = row.Id;
            line.AddView(PaperUi.Segmented(Host, ["Off", "On"], index => SetPillar(id, index == 1), row.On ? 1 : 0));
            body.AddView(line, Spaced(DesignTokens.Space.X2));
        }

        return body;
    }

    private void SetPillar(string id, bool on)
    {
        var current = pillarStore.Read();
        pillarStore.Write(id switch
        {
            "workouts" => current with { Workouts = on },
            "meals" => current with { Meals = on },
            "body" => current with { Body = on },
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "That is not one of the three pillars."),
        });
        RaiseChanged();
    }

    private View BuildEquipment(SettingsModel model)
    {
        var body = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        foreach (var row in model.EquipmentRows)
        {
            var line = new LinearLayout(Host) { Orientation = Orientation.Horizontal, Clickable = true };
            line.SetGravity(GravityFlags.CenterVertical);
            line.SetMinimumHeight(PaperTheme.Dp(Host, DesignTokens.Shape.TouchTargetDp));

            var box = new Checkbox(Host);
            box.SetPicked(row.Picked);
            var size = PaperTheme.Dp(Host, DesignTokens.Space.X6);
            line.AddView(box, new LinearLayout.LayoutParams(size, size)
            {
                MarginEnd = PaperTheme.Dp(Host, DesignTokens.Space.X3),
            });
            line.AddView(PaperUi.Body(Host, row.Name));

            var id = row.Id;
            var picked = row.Picked;
            line.Click += (_, _) =>
            {
                equipmentStore.Write(equipmentStore.Read().With(id, !picked));
                RaiseChanged();
            };
            body.AddView(line);
        }

        return body;
    }

    // ---- connections ----

    private IReadOnlyList<ConnectionInput> ConnectionInputs()
    {
        var report = ConstitutionHealthReport.Read();
        return
        [
            new ConnectionInput(SettingsModel.HealthConnectId, connections.HealthConnectChoicesJson.Length > 0, ConnectionHealth.Unknown),
            new ConnectionInput(CredentialIds.SleepNumber,
                connections.SleepNumberLogin.Length > 0 && connections.SleepNumberPassword.Length > 0,
                ConstitutionHealthReport.HealthOf(report, CredentialIds.SleepNumber)),
            new ConnectionInput(CredentialIds.Remarkable, connections.RemarkableDeviceToken.Length > 0,
                ConstitutionHealthReport.HealthOf(report, CredentialIds.Remarkable)),
        ];
    }

    private View BuildConnections(SettingsModel model)
    {
        var body = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        foreach (var row in model.Connections)
        {
            var block = new LinearLayout(Host) { Orientation = Orientation.Vertical };
            block.AddView(PaperUi.Body(Host, row.Name));
            block.AddView(PaperUi.Secondary(Host, row.Detail));

            var status = PaperUi.Secondary(Host, row.StatusText);
            if (row.IsProblem)
            {
                status.SetTextColor(Color.ParseColor(DesignTokens.ProblemText));
            }

            block.AddView(status, Spaced(DesignTokens.Space.X1));
            if (rowNotes.TryGetValue(row.CredentialId, out var note))
            {
                block.AddView(PaperUi.Secondary(Host, note));
            }

            if (row.ButtonLabel is not null)
            {
                var entering = entryOpenFor == row.CredentialId;
                if (entering)
                {
                    AddEntry(block, row.CredentialId);
                }

                var id = row.CredentialId;
                block.AddView(
                    PaperUi.SecondaryButton(Host, entering ? EntryVerb(id) : row.ButtonLabel, () => OnConnectionButton(id)),
                    Spaced(DesignTokens.Space.X2));
            }

            body.AddView(block, Spaced(DesignTokens.Space.X3));
        }

        return body;
    }

    private static string EntryVerb(string credentialId) =>
        credentialId == CredentialIds.Remarkable ? "Pair" : "Save";

    private void AddEntry(LinearLayout block, string credentialId)
    {
        if (credentialId == CredentialIds.Remarkable)
        {
            firstEntry = PaperUi.MonoField(Host, "One-time code");
            secondEntry = null;
            block.AddView(firstEntry, Spaced(DesignTokens.Space.X2));
            return;
        }

        firstEntry = PaperUi.Field(Host, "Login");
        firstEntry.Text = connections.SleepNumberLogin;
        secondEntry = PaperUi.MonoField(Host, "Password");
        // The household's bed password: masked, unlike the vault passphrase, which is read back by eye.
        secondEntry.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;
        secondEntry.Typeface = PaperTheme.Mono(Host); // setting InputType resets the typeface
        block.AddView(firstEntry, Spaced(DesignTokens.Space.X2));
        block.AddView(secondEntry, Spaced(DesignTokens.Space.X2));
    }

    private void OnConnectionButton(string credentialId)
    {
        if (entryOpenFor != credentialId)
        {
            entryOpenFor = credentialId;
            RaiseChanged();
            return;
        }

        if (credentialId == CredentialIds.Remarkable)
        {
            var code = firstEntry?.Text?.Trim() ?? string.Empty;
            if (code.Length == 0)
            {
                SetNote(credentialId, "Enter the one-time code from my.remarkable.com.");
                return;
            }

            SetNote(credentialId, "Pairing.");
            _ = PairRemarkableAsync(code);
            return;
        }

        var login = firstEntry?.Text?.Trim() ?? string.Empty;
        var password = secondEntry?.Text ?? string.Empty;
        if (login.Length == 0 || password.Length == 0)
        {
            SetNote(credentialId, "Both the login and the password are needed.");
            return;
        }

        connections.SleepNumberLogin = login;
        connections.SleepNumberPassword = password;
        ConstitutionVaultRunner.PublishAfterChange(Host);
        entryOpenFor = null;
        SetNote(credentialId, "Saved to the vault. It is checked on the next reading.");
    }

    private async Task PairRemarkableAsync(string code)
    {
        string note;
        try
        {
            using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
            var token = await new RemarkableRegistrationClient(http).Register(code, CancellationToken.None);
            connections.RemarkableDeviceToken = token;
            ConstitutionVaultRunner.PublishAfterChange(Host);
            entryOpenFor = null;
            note = "Paired and saved to the vault. It is checked on the next delivery.";
        }
        catch (HttpRequestException e)
        {
            note = $"The tablet did not accept that code ({e.StatusCode?.ToString() ?? "no reply"}).";
        }

        Host.RunOnUiThread(() => SetNote(CredentialIds.Remarkable, note));
    }

    private void SetNote(string credentialId, string note)
    {
        rowNotes[credentialId] = note;
        RaiseChanged();
    }

    // ---- passphrase, sign-in, export ----

    private View BuildPassphrase(PassphraseFieldSpec spec)
    {
        // Never masked: the suite's rule, so it can be read back and typed identically into each app.
        // The field edits the shared store and nothing else; its value is not logged or echoed anywhere.
        passphraseField = spec.Mono ? PaperUi.MonoField(Host, "Choose a passphrase") : PaperUi.Field(Host, "Choose a passphrase");
        passphraseField.Text = VaultPassphraseStore.Passphrase;
        passphraseField.FocusChange += (_, args) =>
        {
            if (!args.HasFocus)
            {
                CommitPassphrase();
            }
        };
        return passphraseField;
    }

    private void CommitPassphrase()
    {
        var typed = passphraseField?.Text?.Trim();
        if (typed is null || typed == VaultPassphraseStore.Passphrase)
        {
            return;
        }

        VaultPassphraseStore.Passphrase = typed;
        ConstitutionVaultRunner.PublishAfterChange(Host);
    }

    private async Task SignInAsync()
    {
        try
        {
            signInStatus = "Choosing an account.";
            RaiseChanged();
            var email = await googleAccount.SignIn(Host);
            signInStatus = $"Signed in as {email}.";
            ConstitutionVaultRunner.RestoreAtLaunch(Host);
        }
        catch (InvalidOperationException e)
        {
            signInStatus = $"Sign-in did not complete. {e.Message}";
        }
        catch (Java.Lang.Exception e)
        {
            signInStatus = $"Sign-in did not complete ({e.GetType().Name}).";
        }

        Host.RunOnUiThread(RaiseChanged);
    }

    private void ExportDiagnostics()
    {
        CommitPassphrase();
        try
        {
            DiagnosticsExport.Share(Host);
            exportStatus = "Diagnostics are on the share sheet.";
        }
        catch (ArgumentException e)
        {
            exportStatus = $"Diagnostics could not be built ({e.GetType().Name}).";
        }
        catch (InvalidOperationException e)
        {
            exportStatus = $"Diagnostics could not be built ({e.GetType().Name}).";
        }

        RaiseChanged();
    }

    // ---- layout ----

    private LinearLayout.LayoutParams Spaced(int bottomDp) => Spaced(0, bottomDp);

    private LinearLayout.LayoutParams Spaced(int topDp, int bottomDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(Host, topDp),
            BottomMargin = PaperTheme.Dp(Host, bottomDp),
        };

    private View Spaced(View view, int topDp, int bottomDp)
    {
        var holder = new FrameLayout(Host);
        holder.AddView(view);
        holder.LayoutParameters = Spaced(topDp, bottomDp);
        return holder;
    }
}
