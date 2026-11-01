using Codex.Domain.Diagnostics;
using Constitution.Domain.Diagnostics;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Codex.Ui.Voice;
using Constitution.Body;
using Constitution.Domain.Areas;
using Constitution.Domain.Calendar;
using Constitution.Domain.Credentials;
using Constitution.Domain.Household;
using Constitution.Domain.Notices;
using Constitution.Domain.Settings;
using Constitution.Health;
using Constitution.Household;
using Constitution.Infrastructure.Household;
using Constitution.Infrastructure.SleepNumber;
using Constitution.Infrastructure.Vault;
using Constitution.Services;
using Constitution.Settings;
using Constitution.WorkoutsTab;
using Storyworthtelling.Remarkable;

namespace Constitution;

/// <summary>
/// The Settings tab (book p.17), plain by decision (D71): no texture and nothing laid on the page.
/// Three inline accordion groups (Pillars, Equipment, Connections), then the vault passphrase in the
/// clear in mono, then Export diagnostics as a quiet button, then the build line, which is the only
/// way to answer "did that update land?" on a phone with no adb. The page is drawn from
/// <see cref="SettingsModel"/>; this file only paints it and writes what the owner changes.
///
/// <para>The Household group (C-8.1.2) is drawn from <see cref="HouseholdGroupModel"/>; with no household file wired to this head it says so
/// and edits the planning-setup file the weekly run reads. NYT Cooking
/// is not built (spike D96). Success is an inline change of the text, never a toast.</para>
/// </summary>
public sealed class SettingsArea(Activity host)
    : ConstitutionAreaBase(host, "settings", Resource.Drawable.area_settings), IUndoableArea
{
    private const string PillarsGroup = "pillars";
    private const string EquipmentGroup = "equipment";
    private const string HouseholdGroup = "household";
    private const string ConnectionsGroup = "connections";

    private readonly PillarStore pillarStore = new(host);
    private readonly EquipmentStore equipmentStore = new(host);
    private readonly HouseholdSetupStore householdStore = new(host);
    private readonly ConstitutionConnections connections = new(host);
    private readonly VoiceGoogleAccount googleAccount = ConstitutionGoogle.Create();

    // Which group is open, which connection row is showing its entry, and the one inline sentence
    // each act leaves behind. Held here so a rebuild does not undo them.
    private string? openGroup;
    private string? entryOpenFor;
    private string? signInStatus;
    private string? exportStatus;
    private string? householdNote;
    private readonly Dictionary<string, string> rowNotes = [];

    // The person sheet (opened from a person in the Household list): which person, what was typed, the one sentence it carries, and the
    // model its pinned bar was built from. A removal is written at once and undone from the shared dog-ear within its window.
    private readonly PersonRemovalUndo removalUndo = new();
    private readonly Android.OS.Handler handler = new(Android.OS.Looper.MainLooper!);
    private PersonId? openPerson;
    private PersonSheetInput? sheetInput;
    private PersonSheetModel? sheetModel;
    private string? sheetStatus;
    private bool sheetBusy;

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

    public override View? BuildBottomBar() =>
        openPerson is not null && sheetModel is not null ? PersonSheetView.BuildBar(Host, sheetModel, OnSheetAction, sheetBusy) : null;

    protected override View BuildPage()
    {
        sheetModel = null;
        if (openPerson is { } personId)
        {
            if (BuildPersonSheet(personId) is { } sheet)
            {
                return sheet;
            }

            // The person is no longer on the roster (a removal landed, or the file changed): back to the list.
            CloseSheet();
        }

        var inputs = ConnectionInputs();
        var model = SettingsModel.Build(pillarStore.Read(), equipmentStore.Read(), inputs);
        // A broken connection lights the dog-ear's notice; a mended one takes it away.
        ConnectionNoticeBoard.With(board => SettingsModel.ApplyNotices(board, inputs, DateTimeOffset.UtcNow));

        var scroll = new ScrollView(Host);
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        AddGroup(column, PillarsGroup, "Pillars", model.PillarsSummary, BuildPillars(model));
        AddGroup(column, EquipmentGroup, "Equipment", model.EquipmentSummary, BuildEquipment(model));
        // The roster is the household file's, as the phone's copy last read it; with none (the file has not opened, or this phone has
        // not met one) the group says so and the choices stay editable in the setup file. What the file is doing is said beneath.
        var householdFile = ConstitutionHousehold.For(Host).Service;
        var household = HouseholdGroupModel.Build(householdFile.Roster, householdFile.RosterRole, householdStore.Read());
        var householdChoices = householdStore.Read() is HouseholdSetupRead.Configured saved ? HouseholdChoices.From(saved.Setup) : new HouseholdChoices();
        AddGroup(column, HouseholdGroup, "Household", household.Summary, HouseholdGroupView.Build(Host, household, householdChoices, householdStore, householdNote ?? HouseholdStandingNote(householdFile.Standing), note =>
        {
            householdNote = note;
            RaiseChanged();
        }, OpenSheet));
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

        // The log loop's screen: the logs as they stand, and Send logs now (LOG-LOOP-PLAN.md §3).
        column.AddView(PaperUi.QuietButton(Host, "Logs", () => LoopDiagnosticsActivity.Open(Host)), Spaced(DesignTokens.Space.X1));

        column.AddView(PaperUi.BuildNumber(Host, "Constitution"));

        scroll.AddView(column);
        return scroll;
    }

    // ---- the person sheet ----

    private View? BuildPersonSheet(PersonId id)
    {
        var service = ConstitutionHousehold.For(Host).Service;
        if (service.Roster is not { } roster || roster.Entries.FirstOrDefault(entry => entry.Id == id) is not { } entry)
        {
            return null;
        }

        sheetModel = PersonSheetModel.Build(roster, id, service.RosterRole);
        if (sheetModel is null)
        {
            return null;
        }

        sheetInput ??= PersonSheetInput.From(entry);
        return PersonSheetView.Build(Host, sheetModel, sheetInput, sheetStatus);
    }

    private void OpenSheet(PersonId id)
    {
        openPerson = id;
        sheetInput = null;
        sheetStatus = null;
        sheetBusy = false;
        RaiseChanged();
    }

    private void CloseSheet()
    {
        openPerson = null;
        sheetInput = null;
        sheetModel = null;
        sheetStatus = null;
        sheetBusy = false;
    }

    private void OnSheetAction(PersonSheetAction action)
    {
        if (openPerson is not { } id || sheetInput is not { } input || sheetBusy)
        {
            return;
        }

        var changes = new HouseholdRosterChanges(ConstitutionHousehold.For(Host).Service);
        switch (action)
        {
            case PersonSheetAction.Back:
                CloseSheet();
                RaiseChanged();
                break;
            case PersonSheetAction.Save:
                RunSheetChange($"Saving {input.Name.Trim()}.", () => changes.SaveAsync(id, input.Name, input.OnPickup, input.Address, CancellationToken.None),
                    _ => "Saved.");
                break;
            case PersonSheetAction.Remove:
                // One way back at a time (D164): the first removal is already written, so a second one waits until its window closes or it is undone.
                if (removalUndo.RefusalFor(DateTimeOffset.UtcNow) is { } refusal)
                {
                    sheetStatus = refusal;
                    RaiseChanged();
                    break;
                }

                RunSheetChange($"Removing {sheetModel?.Title}.", () => changes.RemoveAsync(id, CancellationToken.None), BeginRemovalUndo);
                break;
        }
    }

    // The loading rule: a status sentence while the change is written off the UI thread, then the outcome in a sentence.
    private void RunSheetChange(string working, Func<Task<RosterChangeResult>> change, Func<RosterChangeResult, string> onDone)
    {
        sheetBusy = true;
        sheetStatus = working;
        RaiseChanged();
        _ = Task.Run(async () =>
        {
            var result = await change();
            Host.RunOnUiThread(() =>
            {
                sheetBusy = false;
                if (result.Done)
                {
                    sheetStatus = onDone(result);
                }
                else
                {
                    sheetStatus = result.Sentence;
                }

                RaiseChanged();
            });
        });
    }

    // The removal is already written. The corner folds for the undo window and the list says so; the sheet closes onto the list.
    private string BeginRemovalUndo(RosterChangeResult result)
    {
        var removed = result.Removed!;
        ConnectionNoticeBoard.With(board =>
        {
            removalUndo.Begin(board, removed, DateTimeOffset.UtcNow);
            return true;
        });
        householdNote = PersonRemovalUndo.RemovedSentence(removed);
        openGroup = HouseholdGroup;
        CloseSheet();
        handler.RemoveCallbacksAndMessages(null);
        handler.PostDelayed(() => StandRemoval(removed), (long)NoticeBoard.UndoWindow.TotalMilliseconds + 1);
        return string.Empty;
    }

    // The window closed: the removal stands, and the list stops promising a way back.
    private void StandRemoval(RemovedPerson removed)
    {
        if (!removalUndo.Expire(DateTimeOffset.UtcNow))
        {
            return;
        }

        householdNote = PersonRemovalUndo.StandsSentence(removed);
        RaiseChanged();
    }

    /// <summary>The shell turned the fold and the board gave back this act: the person is written back, newer than the removal.</summary>
    public void UndoTurned(string actKey)
    {
        if (removalUndo.Reverse(actKey, DateTimeOffset.UtcNow) is not { } removed)
        {
            return;
        }

        handler.RemoveCallbacksAndMessages(null);
        householdNote = $"Bringing {removed.Name} back.";
        var changes = new HouseholdRosterChanges(ConstitutionHousehold.For(Host).Service);
        _ = Task.Run(async () =>
        {
            var result = await changes.RestoreAsync(removed, CancellationToken.None);
            Host.RunOnUiThread(() =>
            {
                householdNote = result.Done ? PersonRemovalUndo.RestoredSentence(removed) : result.Sentence;
                RaiseChanged();
            });
        });
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
        var saves = new ConstitutionCredentialSaves(Host);
        return
        [
            new ConnectionInput(SettingsModel.HealthConnectId, connections.HealthConnectChoicesJson.Length > 0, ConnectionHealth.Unknown),
            new ConnectionInput(CredentialIds.SleepNumber,
                connections.SleepNumberLogin.Length > 0 && connections.SleepNumberPassword.Length > 0,
                ConstitutionHealthReport.HealthOf(report, CredentialIds.SleepNumber, saves.SavedAt(CredentialIds.SleepNumber))),
            new ConnectionInput(CredentialIds.Remarkable, connections.RemarkableDeviceToken.Length > 0,
                ConstitutionHealthReport.HealthOf(report, CredentialIds.Remarkable, saves.SavedAt(CredentialIds.Remarkable))),
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

            if (row.CredentialId == SettingsModel.HealthConnectId)
            {
                // Review S10: the overnight permission can only be given from Health Connect's own page.
                var overnight = model.OvernightReads;
                block.AddView(PaperUi.Body(Host, overnight.Name), Spaced(DesignTokens.Space.X2));
                block.AddView(PaperUi.Secondary(Host, overnight.Sentence));
                block.AddView(
                    PaperUi.SecondaryButton(Host, overnight.ButtonLabel, OpenOvernightSettings),
                    Spaced(DesignTokens.Space.X2));
            }

            body.AddView(block, Spaced(DesignTokens.Space.X3));
        }

        // Review S7: the household shares one bed login, so which side is mine is chosen here, once a login is set.
        var loginSet = connections.SleepNumberLogin.Length > 0 && connections.SleepNumberPassword.Length > 0;
        if (BedSideRow.Build(loginSet, connections.SleeperSide) is { } side)
        {
            var block = new LinearLayout(Host) { Orientation = Orientation.Vertical };
            block.AddView(PaperUi.Body(Host, side.Name));
            block.AddView(PaperUi.Secondary(Host, side.Caption));
            block.AddView(PaperUi.Segmented(Host, side.Labels, SetBedSide, side.Selected), Spaced(DesignTokens.Space.X2));
            body.AddView(block, Spaced(DesignTokens.Space.X3));
        }

        return body;
    }

    // Names the page the platform has, or says so in the trail when it has none (by exception type only).
    private void OpenOvernightSettings() =>
        new ActivityHealthPermissionRequester(Host).OpenHealthConnectSettings(new BodyLog(Host));

    // The side is written on this phone first, then published to the personal vault (never the shared one).
    private void SetBedSide(int index)
    {
        var stored = BedSideRow.StoredFor(index);
        new SleeperChoice(Host).Side = stored switch
        {
            "left" => BedSide.Left,
            "right" => BedSide.Right,
            _ => null,
        };
        _ = ConstitutionVaults.Create(Host).PublishAsync([VaultField.SleeperSide], CancellationToken.None);
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
        // A new login has not been judged yet: the old one's failure is not held against it.
        ConstitutionHealthReport.CredentialReplaced(Host, CredentialIds.SleepNumber);
        entryOpenFor = null;
        SetNote(credentialId, "Saved on this phone. Saving to the vault.");
        _ = SaveToVaultAsync(
            credentialId, VaultNoteSubject.SleepNumber, [VaultField.SleepNumberLogin, VaultField.SleepNumberPassword],
            TapWatch.Action(ConstitutionKeyActions.SleepNumberLogin, TimeSpan.FromMinutes(1)));
    }

    // The note says what the vault actually did. The runner never throws: every ending is a result.
    private async Task SaveToVaultAsync(
        string credentialId, VaultNoteSubject subject, IReadOnlyCollection<VaultField> changed, KeyAction? watched = null)
    {
        var result = await ConstitutionVaults.Create(Host).PublishAsync(changed, CancellationToken.None);
        // The runner never throws, so the vault's answer is the action's: its outcome word, never a value.
        watched?.Succeeded();
        HeadLog.Info("Settings", $"vault publish after a login save: {result.Status}");
        var note = VaultNote.For(result, subject);
        Host.RunOnUiThread(() => SetNote(credentialId, note));
    }

    // Whatever stops the pairing, the row ends on a sentence: a failure of a kind we expect is worded by
    // ConnectionFailure; one nobody expected is recorded by type name and propagates with its stack, and
    // the finally still returns the row to a defined state.
    private async Task PairRemarkableAsync(string code)
    {
        var settled = false;
        try
        {
            string token;
            try
            {
                using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
                token = await new RemarkableRegistrationClient(http).Register(code, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new FormatException("The tablet's cloud replied with no token.");
                }
            }
            catch (Exception e) when (ConnectionTrail.Expecting(Host, ConnectionAct.PairRemarkable, e,
                e is HttpRequestException or OperationCanceledException or TimeoutException or JsonException
                    or FormatException or IOException or Java.Lang.Exception))
            {
                Settle(ConnectionFailure.Sentence(ConnectionAct.PairRemarkable, e, callerCancelled: false));
                settled = true;
                return;
            }

            connections.RemarkableDeviceToken = token;
            // A new pairing has not been judged yet: the old token's failure is not held against it.
            ConstitutionHealthReport.CredentialReplaced(Host, CredentialIds.Remarkable);
            entryOpenFor = null;
            Settle("Paired and saved on this phone. Saving to the vault.");
            var result = await ConstitutionVaults.Create(Host).PublishAsync([VaultField.RemarkableDeviceToken], CancellationToken.None);
            Settle(VaultNote.For(result, VaultNoteSubject.Remarkable));
            settled = true;
        }
        finally
        {
            if (!settled)
            {
                Settle("Pairing stopped unexpectedly. Try again.");
            }
        }
    }

    private void Settle(string note) => Host.RunOnUiThread(() => SetNote(CredentialIds.Remarkable, note));

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

    // The household file's standing as one sentence beneath the group; nothing is said while it is in step.
    private static string? HouseholdStandingNote(HouseholdStanding standing) =>
        standing.State is HouseholdFileState.Synced or HouseholdFileState.NotCompared ? null : standing.Sentence;

    private void CommitPassphrase()
    {
        var typed = passphraseField?.Text?.Trim();
        if (typed is null || typed == VaultPassphraseStore.Passphrase)
        {
            return;
        }

        VaultPassphraseStore.Passphrase = typed;
        _ = RestoreThenPublishAsync();
    }

    // On a phone just reset the passphrase is typed last, so it is the moment the vault can be read:
    // bring the household's connections back first, then say what this install has to add. The runner
    // never throws; a restore that filled something is shown at once.
    private async Task RestoreThenPublishAsync()
    {
        var result = await ConstitutionVaults.Create(Host).RestoreThenPublishAsync([], CancellationToken.None);

        // A new passphrase or sign-in is also the moment the household file can be compared with Drive.
        ConstitutionHousehold.For(Host).AskNow();
        if (result.Restore is { Filled.Count: > 0 })
        {
            Host.RunOnUiThread(RaiseChanged);
        }
    }

    private async Task SignInAsync()
    {
        var signing = TapWatch.Action(ConstitutionKeyActions.GoogleSignIn, TimeSpan.FromMinutes(5));
        var settled = false;
        try
        {
            signInStatus = "Choosing an account.";
            RaiseChanged();
            try
            {
                var email = await googleAccount.SignIn(Host);
                signing.Succeeded();
                signInStatus = $"Signed in as {email}.";
                settled = true;
            }
            catch (Exception e) when (ConnectionTrail.Expecting(Host, ConnectionAct.SignIn, e,
                e is InvalidOperationException or OperationCanceledException or TimeoutException or IOException
                    or Java.Lang.Exception))
            {
                signing.Failed(e.GetType().Name);
                signInStatus = ConnectionFailure.Sentence(ConnectionAct.SignIn, e, callerCancelled: false);
                settled = true;
                return;
            }

            // Signing in can be the last step on a reset phone (passphrase first), so it brings the vault back too.
            await RestoreThenPublishAsync();
        }
        finally
        {
            if (!settled)
            {
                signInStatus = "Sign-in stopped unexpectedly. Try again.";
            }

            Host.RunOnUiThread(RaiseChanged);
        }
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
