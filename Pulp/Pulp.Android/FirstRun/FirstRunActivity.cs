using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Codex.Ui.Voice;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Actions;
using Pulp.Domain.Core;
using Pulp.Domain.FirstRun;
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;
using Pulp.Domain.Shell;
using Pulp.Shell;
using Pulp.Vault;

namespace Pulp.FirstRun;

/// <summary>
/// First run (D142, D272 — built from the Paper anatomy, no mockup): the vault, the household, whose
/// phone this is. Each page is one step with a Serif header, one lead sentence and its action in the
/// pinned bar beside "Skip for now"; which step shows is <see cref="FirstRunPlan.Next"/> over the state
/// as it is, read again after every action and on every return, so leaving half-way resumes at the
/// next launch. When a household already exists in Drive the sync adopts it and the household step
/// simply never comes. No tour (D142); success is the next step appearing, and a failure folds into the
/// dog-ear.
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class FirstRunActivity : Activity
{
    private const int StepCount = 3;

    private readonly CancellationTokenSource lifetime = new();

    private PulpServices services = null!;
    private PulpVault vault = null!;
    private FrameLayout overlay = null!;
    private LinearLayout headerSlot = null!;
    private LinearLayout body = null!;
    private LinearLayout barSlot = null!;
    private TextView status = null!;
    private EditText passphrase = null!;
    private EditText name = null!;
    private HouseholdDraft draft = HouseholdDraft.Empty;
    private bool addAsChild;
    private bool busy;
    private SyncWatch syncWatch = null!;
    private int paint;

    public static Intent For(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var intent = new Intent(context, typeof(FirstRunActivity));
        return intent;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        services = PulpServices.For(this);
        vault = new PulpVault(services);
        syncWatch = new SyncWatch(this, services, () => _ = AdvanceAsync());
        SetContentView(BuildLayout());
        Restore(savedInstanceState);
    }

    private const string DraftKey = "household-draft";
    private const string TypedKey = "typed-name";
    private const string ChildKey = "typed-as-child";

    /// <summary>
    /// Android rebuilds this screen on a rotation or when the process was reclaimed while another app (the
    /// account picker) was in front; what was typed — the people added, the name half-written and its
    /// child flag — is put back, or the owner would start the household again. The vault passphrase is
    /// kept by its own store as it is typed.
    /// </summary>
    protected override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        outState.PutString(DraftKey, HouseholdDraftCodec.Encode(draft));
        outState.PutString(TypedKey, name.Text ?? string.Empty);
        outState.PutBoolean(ChildKey, addAsChild);
    }

    private void Restore(Bundle? saved)
    {
        if (saved is null)
        {
            return;
        }

        draft = HouseholdDraftCodec.Decode(saved.GetString(DraftKey));
        name.Text = saved.GetString(TypedKey) ?? string.Empty;
        addAsChild = saved.GetBoolean(ChildKey, false);
    }

    protected override void OnResume()
    {
        base.OnResume();
        syncWatch.Resume();

        // Back from the Vault page or the account picker: what is in place may have moved.
        _ = AdvanceAsync();
    }

    protected override void OnPause()
    {
        base.OnPause();
        syncWatch.Pause();
    }

    protected override void OnDestroy()
    {
        lifetime.Cancel();
        base.OnDestroy();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == VoiceGoogleAccount.RequestCode)
        {
            VoiceGoogleAccount.HandleChooserResult(resultCode, data);
        }
    }

    // ---- layout ----

    private View BuildLayout()
    {
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, PaperTheme.Dp(this, DesignTokens.Space.X6));

        headerSlot = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.AddView(headerSlot);

        status = PaperUi.Secondary(this, string.Empty);
        status.Visibility = ViewStates.Gone;
        content.AddView(status, Spaced(0, DesignTokens.Space.X4));

        body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.AddView(body);

        // Built once so what was typed survives a repaint.
        passphrase = PaperUi.MonoField(this, VaultPageText.PassphraseHint);
        passphrase.Text = VaultPassphraseStore.Passphrase;
        name = PaperUi.Field(this, FirstRunText.NameHint);

        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(content);
        overlay = PaperUi.WrapForTopInset(this, scroll);

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.AddView(overlay, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        barSlot = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.AddView(barSlot);
        return column;
    }

    // ---- the sequence ----

    /// <summary>Reads the state and shows the step it calls for; at Done, records first run finished and closes.</summary>
    private async Task AdvanceAsync()
    {
        FirstRunState state;
        try
        {
            state = await Task.Run(() => FirstRunProgress.ReadAsync(services, lifetime.Token), lifetime.Token);
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            // The household file would not open. The sync raises its own notice; first run carries on
            // as though there were no household yet, which is what this phone can see.
            services.Diagnostics.Warn("first-run", "The household could not be read for first run.", exception);
            state = new FirstRunState(
                VoiceGoogleAccount.IsSignedIn, VaultPassphraseStore.IsSet, vault.KeysPresent(), HasRoster: false,
                ThisPhoneStatus.NoRoster, FirstRunProgress.Skipped, FirstRunProgress.Finished,
                MayAddPeople: services.Rights.CanAddOrRemoveViewers);
        }

        var step = FirstRunPlan.Next(state);
        if (step == FirstRunStep.Done)
        {
            // The phone the household is shared with has no household yet to ask "This phone is" about;
            // it closes without being marked finished, so the step still comes once the household does (D166).
            if (FirstRunPlan.MayFinish(state))
            {
                FirstRunProgress.Finish();
            }

            Finish();
            return;
        }

        await PaintAsync(step, state);
    }

    private async Task PaintAsync(FirstRunStep step, FirstRunState state)
    {
        // Two reads can race (the account picker returning fires OnResume as well as its own advance);
        // only the latest paint may add to the page.
        var generation = ++paint;
        headerSlot.RemoveAllViews();
        body.RemoveAllViews();
        barSlot.RemoveAllViews();

        var (title, number) = step switch
        {
            FirstRunStep.Vault => (FirstRunText.VaultTitle, 1),
            FirstRunStep.Household => (FirstRunText.HouseholdTitle, 2),
            FirstRunStep.ThisPhone => (FirstRunText.PhoneTitle, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, "A first-run step with no page."),
        };

        headerSlot.AddView(PaperUi.ScreenHeader(this, title, AreaPalette.For(AreaHue.Identity).Rule));
        headerSlot.AddView(PaperUi.Label(this, FirstRunText.Step(number, StepCount)), Spaced(0, DesignTokens.Space.X4));

        switch (step)
        {
            case FirstRunStep.Vault:
                PaintVault(state);
                break;
            case FirstRunStep.Household:
                PaintHousehold();
                break;
            case FirstRunStep.ThisPhone:
                await PaintPhoneAsync(generation);
                break;
        }
    }

    private void PaintVault(FirstRunState state)
    {
        body.AddView(PaperUi.Body(this, FirstRunText.VaultLead), Spaced(0, DesignTokens.Space.X6));

        body.AddView(PaperUi.Heading(this, VaultPageText.AccountHeading), Spaced(0, DesignTokens.Space.X2));
        body.AddView(PaperUi.Secondary(this, state.SignedIn
            ? VaultPageText.SignedInAs(VoiceGoogleAccount.Email)
            : VaultPageText.NotSignedIn), Spaced(0, DesignTokens.Space.X2));
        body.AddView(PaperUi.SecondaryButton(this,
            state.SignedIn ? VaultPageText.SignInAgain : VaultPageText.SignIn,
            () => _ = SignInAsync()), Spaced(0, DesignTokens.Space.X6));

        // In the clear: the one secret the vault cannot keep, checked letter by letter against the other apps.
        body.AddView(PaperUi.Heading(this, VaultPageText.PassphraseHeading), Spaced(0, DesignTokens.Space.X2));
        Detach(passphrase);
        body.AddView(passphrase, Spaced(0, DesignTokens.Space.X2));
        body.AddView(PaperUi.Secondary(this, VaultPageText.PassphraseHelper), Spaced(0, DesignTokens.Space.X6));

        // The vault opened but did not hold every key: the Vault page is where the rest are added.
        if (state.SignedIn && state.HasPassphrase && state.KeysPresent < FirstRunPlan.KeyCount)
        {
            body.AddView(PaperUi.SecondaryButton(this, FirstRunText.AddKeysInVault,
                () => StartActivity(VaultActivity.For(this))), Spaced(0, DesignTokens.Space.X4));
        }

        Bar(FirstRunStep.Vault, PaperUi.PrimaryButton(this, FirstRunText.OpenVault, () => _ = OpenVaultAsync()));
    }

    private void PaintHousehold()
    {
        body.RemoveAllViews();
        body.AddView(PaperUi.Body(this, FirstRunText.HouseholdLead), Spaced(0, DesignTokens.Space.X6));

        foreach (var person in draft.People)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            var label = person.Name + (person.IsChild ? " · " + FirstRunText.ChildMark : string.Empty);
            row.AddView(PaperUi.Body(this, label), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
            var id = person.Id;
            row.AddView(PaperUi.QuietButton(this, FirstRunText.TakeOff, () =>
            {
                draft = draft.Remove(id);
                PaintHousehold();
            }));
            body.AddView(row, Spaced(0, DesignTokens.Space.X2));
        }

        Detach(name);
        body.AddView(name, Spaced(DesignTokens.Space.X4, DesignTokens.Space.X2));
        body.AddView(PaperUi.Segmented(this, [FirstRunText.Adult, FirstRunText.Child],
            index => addAsChild = index == 1, addAsChild ? 1 : 0), Spaced(0, DesignTokens.Space.X2));
        body.AddView(PaperUi.SecondaryButton(this, FirstRunText.Add, AddPerson), Spaced(0, DesignTokens.Space.X4));

        // Redrawing the step also redraws the bar, so a repaint after an add does not stack a second one.
        barSlot.RemoveAllViews();
        Bar(FirstRunStep.Household, PaperUi.PrimaryButton(this, FirstRunText.SaveHousehold, () => _ = SaveHouseholdAsync()));
    }

    private async Task PaintPhoneAsync(int generation)
    {
        Bar(FirstRunStep.ThisPhone);
        body.AddView(PaperUi.Body(this, FirstRunText.PhoneLead), Spaced(0, DesignTokens.Space.X6));

        HouseholdView household;
        try
        {
            household = await services.HouseholdAsync(lifetime.Token);
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            // Skip stays in the bar; the household's own notice says the file would not open.
            services.Diagnostics.Warn("first-run", "The roster could not be read for 'This phone is'.", exception);
            return;
        }

        if (generation != paint)
        {
            return;
        }

        foreach (var choice in PhoneChoices.For(household.Roster, household.NameOf))
        {
            var id = choice.Id;
            body.AddView(PaperUi.SecondaryButton(this, FirstRunText.ThisPhoneIs(choice.Name),
                () => _ = SetThisPhoneAsync(id)), Spaced(0, DesignTokens.Space.X2));
        }
    }

    // ---- actions ----

    private async Task SignInAsync()
    {
        try
        {
            await vault.SignInAsync(this);
            PulpNotices.Withdraw(this, overlay, VaultOutcomes.SignInKey);
            await AdvanceAsync();
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            services.Diagnostics.Warn("first-run", "Google sign-in did not complete.", exception);
            PulpNotices.Raise(this, overlay, VaultOutcomes.SignInFailed(DateTimeOffset.UtcNow), NoticeButtons());
        }
    }

    private async Task OpenVaultAsync()
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            VaultPassphraseStore.Passphrase = passphrase.Text ?? string.Empty;
            ShowStatus(VaultPageText.Opening);
            var outcome = await vault.RestoreAsync(lifetime.Token);
            var notice = VaultOutcomes.Notice(outcome, DateTimeOffset.UtcNow);
            if (notice is null)
            {
                PulpNotices.Withdraw(this, overlay, VaultOutcomes.NoticeKey);
            }
            else
            {
                PulpNotices.Raise(this, overlay, notice, NoticeButtons());
            }

            ShowStatus(VaultOutcomes.Sentence(outcome, vault.KeysPresent()));
            await AdvanceAsync();
        }
        finally
        {
            End();
        }
    }

    private void AddPerson()
    {
        var edit = draft.Add(name.Text ?? string.Empty, addAsChild);
        ShowStatus(edit.Problem);
        if (edit.Problem is null)
        {
            draft = edit.Draft;
            name.Text = string.Empty;
            addAsChild = false;
        }

        PaintHousehold();
        name.RequestFocus();
    }

    private async Task SaveHouseholdAsync()
    {
        if (draft.SaveProblem is { } problem)
        {
            ShowStatus(problem);
            return;
        }

        if (!Begin())
        {
            return;
        }

        try
        {
            ShowStatus(FirstRunText.Saving);
            var written = await services.AddPeopleAsync(draft, lifetime.Token);
            var already = written.AlreadyThere.Count == 0 ? null : FirstRunText.AlreadyThere(written.AlreadyThere);

            PulpNotices.Withdraw(this, overlay, FirstRunNotices.HouseholdKey);
            draft = HouseholdDraft.Empty;
            ShowStatus(already);
            await AdvanceAsync();
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            services.Diagnostics.Warn("first-run", "The household could not be saved.", exception);
            ShowStatus(null);
            PulpNotices.Raise(this, overlay, FirstRunNotices.HouseholdNotSaved(DateTimeOffset.UtcNow), NoticeButtons());
        }
        finally
        {
            End();
        }
    }

    private async Task SetThisPhoneAsync(ViewerId viewer)
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            await services.SetThisPhoneAsync(viewer, lifetime.Token);
            PulpNotices.Withdraw(this, overlay, FirstRunNotices.PhoneKey);
            await AdvanceAsync();
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            services.Diagnostics.Warn("first-run", $"This phone could not be recorded as {viewer}'s.", exception);
            PulpNotices.Raise(this, overlay, FirstRunNotices.PhoneNotSaved(DateTimeOffset.UtcNow), NoticeButtons());
        }
        finally
        {
            End();
        }
    }

    private void SkipStep(FirstRunStep step)
    {
        if (!FirstRunPlan.CanSkip(step))
        {
            return;
        }

        FirstRunProgress.Skip(step);
        ShowStatus(null);
        _ = AdvanceAsync();
    }

    // ---- pieces ----

    private void Bar(FirstRunStep step, params View[] actions) =>
        barSlot.AddView(PaperUi.BottomBar(this, [PaperUi.QuietButton(this, FirstRunText.Skip, () => SkipStep(step)), .. actions]));

    private PulpNotices.Buttons NoticeButtons() =>
        new(OpenVault: () => StartActivity(VaultActivity.For(this)));

    private bool Begin()
    {
        if (busy)
        {
            return false;
        }

        busy = true;
        barSlot.Enabled = false;
        return true;
    }

    private void End()
    {
        busy = false;
        barSlot.Enabled = true;
    }

    private void ShowStatus(string? sentence)
    {
        status.Text = sentence ?? string.Empty;
        status.Visibility = string.IsNullOrEmpty(sentence) ? ViewStates.Gone : ViewStates.Visible;
    }

    private static void Detach(View view) => (view.Parent as ViewGroup)?.RemoveView(view);

    private LinearLayout.LayoutParams Spaced(int topSpace, int bottomSpace) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(this, topSpace),
            BottomMargin = PaperTheme.Dp(this, bottomSpace),
        };
}
