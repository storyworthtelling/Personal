using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Codex.Ui;
using Storyworthtelling.Diagnostics;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Groceries;
using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Notices;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;
using Constitution.Domain.Today;
using Constitution.MealsTab;
using Constitution.Services;
using Constitution.Settings;
using Constitution.WorkoutsTab;
using Storyworthtelling.Materials;

namespace Constitution;

/// <summary>
/// The Meals tab (book pp.13-14): the on-page switcher between This week and the Recipe box. The area owns what the box
/// shows: the cards as the source last read them, the search, the card out of its pile, what each card's Cook this soon says,
/// and a cuisine move waiting out its undo window. Every decision is in <see cref="RecipeBoxScreenModel"/>,
/// <see cref="PileLayout"/>, <see cref="CardSelection"/>, <see cref="CardPickModel"/>, <see cref="CookSoon"/>,
/// <see cref="CuisineChoices"/> and <see cref="CuisineMoveUndo"/>; this reads, hands them over, and paints.
///
/// <para>Where there is no household file, <see cref="Source"/> answers so and the box says it in a sentence, with nothing
/// invented. The cards are read off the UI thread and the page says so in a sentence while it waits.</para>
///
/// <para>Scanning a recipe card (book p.14, D176): the pinned bar's Scan a recipe card opens the camera (Plate's on the owner's phone,
/// the system's on the partner's) through <see cref="CameraCaptureLauncher"/>, one photo per page with Add a page between. Each page is
/// shrunk and re-encoded into memory and its file deleted at once; Read the recipe sends the pages, in order, to
/// <see cref="IRecipePhotoReader"/> (Claude), whose title, ingredients and steps fill the same three editable boxes typing uses
/// (<see cref="TypedEntry.Fill"/>). Backstory, tips and page numbers are left out by the read. Keep the card goes to
/// <see cref="Keeper"/>, the only way a card is kept; Let go waits out the dog-ear's undo window. No photograph is kept.</para>
///
/// <para>Moving a card's cuisine happens on the page at once and folds the shell's one dog-ear (the shared notice board) for
/// the undo window; a turn of the fold applies the move's inverse. Cook this soon is the card changing to "Planned for next
/// week": no toast. This week reads the approved week through <see cref="WeekSource"/> and says so plainly when there is none.</para>
/// </summary>
public sealed class MealsArea : ConstitutionAreaBase, IUndoableArea
{
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly CuisineMoveUndo undo = new();
    private readonly TypedEntryUndo typedUndo = new();
    private readonly TypedEntry typed = new();
    private readonly Dictionary<RecipeId, CookSoonState> cooks = [];

    private IRecipeBoxSource source = new NoRecipeBoxSource();
    private IRecipeKeeper keeper = new NoRecipeKeeper();
    private IRecipePhotoReader photoReader;
    private CameraCaptureLauncher? capture;
    private RecipePhotoLoop? photoLoop;
    private bool reading;
    private bool keeping;
    private bool cameraBusy;
    private bool typing;
    private InstallRole role = InstallRole.Unknown;
    private RecipePickerModel? picker;
    private RecipeBoxRead? read;
    private IMealWeekSource weekSource = new NoMealWeekSource();
    private MealWeekScreenRead? weekRead;
    private MealsView view = MealsView.ThisWeek;
    private string query = string.Empty;
    private CardSelection selection = CardSelection.None;
    private RecipeBoxPage? boxPage;
    private CuisineCardView? cuisineCard;
    private string? notice;
    private int loadNumber;
    private AteSomethingElseFlow? ate;
    private KeepShareRun? share;
    private bool sharing;

    public MealsArea(Activity host)
        : base(host, "meals", TabGlyphs.Bowl)
    {
        photoReader = RecipeReaderServices.ReaderOf(host);
    }

    /// <summary>The "I ate something else" flow the bar opens on This week and whose lines sit on its cards; shared with Today.</summary>
    public AteSomethingElseFlow? Ate
    {
        get => ate;
        set
        {
            ate = value;
            if (value is not null)
            {
                value.Changed += RaiseChanged;
            }
        }
    }

    /// <summary>Builds the grocery list Share the list hands to Keep (D112); null leaves the button disabled. Set by whoever wires the household file.</summary>
    public KeepShareRun? Share
    {
        get => share;
        set => share = value;
    }

    /// <summary>Where the cards come from and where a change goes; set by whoever wires the household file.</summary>
    public IRecipeBoxSource Source
    {
        get => source;
        set => source = value;
    }

    /// <summary>Where a checked scan is kept; set by whoever wires the household file. Until then it says there is no file.</summary>
    public IRecipeKeeper Keeper
    {
        get => keeper;
        set => keeper = value;
    }

    /// <summary>
    /// Whose phone this is, which decides the scan row (Plate on the owner's, the camera on the partner's). Set by whoever reads the
    /// household file; until it is known the owner's row is offered, and Plate answers or says it is absent.
    /// </summary>
    public InstallRole Role
    {
        get => role;
        set
        {
            role = value;
            picker = null;
        }
    }

    /// <summary>Reads a recipe off its page photographs (D176); Claude over the shared key unless a test or wiring says otherwise.</summary>
    public IRecipePhotoReader PhotoReader
    {
        get => photoReader;
        set => photoReader = value;
    }

    /// <summary>Where the approved week comes from; set by whoever wires the weekly meal run's saved week.</summary>
    public IMealWeekSource WeekSource
    {
        get => weekSource;
        set => weekSource = value;
    }

    /// <summary>
    /// Raised by the pinned bar's button with the action's words. Scan a recipe card is carried out here as well; Add a recipe
    /// (the picker, C-6.5.4) listens, and until it does that button is drawn disabled.
    /// </summary>
    public event Action<string>? PrimaryActionRequested;

    // The partner's camera row is live where the phone has a camera (D105); the paste row waits on written permission (PC8, D82), so
    // the picker is built without it.
    private RecipePickerModel Picker() => picker ??= RecipePickerModel.For(role, cameraCanRead: HasCamera(), pasteAllowed: false);

    private bool HasCamera() => Host.PackageManager?.HasSystemFeature(PackageManager.FeatureCameraAny) == true;

    public override string? Subtitle => notice;

    protected override string LoadingSentence => "Reading the recipe box.";

    // ---- the page ----

    protected override View BuildPage()
    {
        boxPage = null;
        if (typing)
        {
            return new PickerTypedPage(Host, typed);
        }

        View body;
        if (view == MealsView.ThisWeek)
        {
            body = ThisWeekBody();
        }
        else
        {
            body = read switch
            {
                RecipeBoxRead.Ready ready => BoxBody(ready.Snapshot),
                RecipeBoxRead.NoHouseholdFile none => PageFurniture.Sentence(Host, none.Sentence),
                RecipeBoxRead.Unavailable unavailable => PageFurniture.Sentence(Host, unavailable.Reason),
                _ => PageFurniture.StatusSentence(Host, LoadingSentence),
            };
        }

        return new MealsPage(Host, Tab.Hue, view, OnSwitch, body);
    }

    private View ThisWeekBody()
    {
        if (weekRead is null)
        {
            return PageFurniture.StatusSentence(Host, LoadingSentence);
        }

        var model = MealsWeekModel.Build(weekRead, DateOnly.FromDateTime(DateTime.Now));
        return model.State == WeekState.Planned ? new ThisWeekPage(Host, model, AteCards.Lines(Ate, DateOnly.FromDateTime(DateTime.Now)), AteCards.Status(Ate, DateOnly.FromDateTime(DateTime.Now))) : PageFurniture.Sentence(Host, model.Status);
    }

    private View BoxBody(RecipeBoxSnapshot snapshot)
    {
        var page = new RecipeBoxPage(Host, ScreenFor(snapshot), query, OnQuery, OnTap, OnHold, OnCook, OnPutBack);
        boxPage = page;
        return page;
    }

    private BoxScreen ScreenFor(RecipeBoxSnapshot snapshot)
    {
        var cards = undo.Show(snapshot.Cards);
        var planned = PlannedNow(snapshot);
        var model = RecipeBoxScreenModel.Build(cards, query, planned);
        CardFace? face = null;
        if (selection.Picked is { } picked && cards.FirstOrDefault(card => card.Id == picked) is { } card)
        {
            face = CardPickModel.Face(card, snapshot.HistoryOf(picked), cooks.TryGetValue(picked, out var state) ? state : CookSoon.StateOf(picked, planned), DateOnly.FromDateTime(DateTime.Now));
        }

        return new BoxScreen(model, face, face is null ? null : selection.Picked, planned);
    }

    private HashSet<RecipeId> PlannedNow(RecipeBoxSnapshot snapshot)
    {
        var planned = new HashSet<RecipeId>(snapshot.PlannedNextWeek);
        foreach (var (card, state) in cooks)
        {
            if (state == CookSoonState.Planned)
            {
                planned.Add(card);
            }
        }

        return planned;
    }

    public override View? BuildBottomBar()
    {
        if (typing)
        {
            var letGo = PaperUi.SecondaryButton(Host, TypedEntry.LetGoAction, LetGoOfTyping);
            letGo.Enabled = !keeping;
            var keep = PaperUi.PrimaryButton(Host, TypedEntry.KeepAction, KeepTyped);
            keep.Enabled = !keeping;
            return PaperUi.BottomBar(Host, letGo, keep);
        }

        if (photoLoop is not null)
        {
            // One photo per page: Add a page opens the camera again, Read the recipe sends the pages to be read.
            var addPage = PaperUi.SecondaryButton(Host, photoLoop.AddPageAction, OpenCamera);
            addPage.Enabled = !cameraBusy && !reading && photoLoop.CanAddPage;
            var done = PaperUi.PrimaryButton(Host, photoLoop.DoneAction, FinishCamera);
            done.Enabled = photoLoop.CanFinish && !cameraBusy && !reading;
            return PaperUi.BottomBar(Host, addPage, done);
        }

        if (view == MealsView.ThisWeek)
        {
            return ThisWeekBar();
        }

        if (view != MealsView.RecipeBox || read is not RecipeBoxRead.Ready ready)
        {
            return null;
        }

        var action = RecipeBoxScreenModel.Build(ready.Snapshot.Cards, null).PrimaryAction;
        var button = PaperUi.PrimaryButton(Host, action, () => OnPrimary(action));
        button.Enabled = action != RecipeBoxText.ScanAction || !cameraBusy;
        if (action == RecipeBoxText.ScanAction)
        {
            // Short of ten the scan is the page's one invitation; typing is still always there beside it.
            var typeIt = PaperUi.SecondaryButton(Host, RecipePickerText.TypeInTitle, OpenTyping);
            return PaperUi.BottomBar(Host, typeIt, button);
        }

        var bar = PaperUi.BottomBar(Host, button);
        return Picker().IsOpen ? new PickerSheet(Host, Picker().Rows, OnPick, bar) : bar;
    }

    // "I ate something else" stays in the bar on this view (book p.13) and opens the same camera flow as Today's (book p.15).
    private View? ThisWeekBar()
    {
        if (weekRead is not MealWeekScreenRead.Ready)
        {
            return null;
        }

        var flow = Ate;
        var button = PaperUi.SecondaryButton(Host, MealsWeekModel.ActionLabel, () => flow?.Start());
        button.Enabled = flow is { CanStart: true };
        var shareButton = PaperUi.PrimaryButton(Host, KeepShareRun.ActionLabel, ShareList);
        shareButton.Enabled = share is not null && read is RecipeBoxRead.Ready && !sharing;
        return PaperUi.BottomBar(Host, button, shareButton);
    }

    // Share the list (D112): the reads and the build run off the UI thread, the one intent starts on it, and the answer is the page's inline line, never a toast.
    private void ShareList()
    {
        if (share is not { } run || sharing || weekRead is not MealWeekScreenRead.Ready week || read is not RecipeBoxRead.Ready box)
        {
            return;
        }

        sharing = true;
        notice = null;
        RaiseChanged();
        var kept = week.Snapshot.Week;
        var cards = box.Snapshot.Cards;
        _ = Task.Run(() =>
        {
            KeepSharePrepared prepared;
            try
            {
                prepared = run.Prepare(kept, cards);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // The type only, never the message; the failure rides in the report with its stack.
                prepared = new KeepSharePrepared.Settled(new KeepShareReport(
                    KeepShareOutcome.ListNotBuilt, "The list could not be built just now (" + e.GetType().Name + "), so it was not sent.") { Failure = e });
            }

            Host.RunOnUiThread(() => ShareDone(prepared));
        });
    }

    private void ShareDone(KeepSharePrepared prepared)
    {
        sharing = false;
        notice = prepared switch
        {
            KeepSharePrepared.Ready ready => KeepShareRun.Finish(ready, KeepIntentSender.Start(Host, ready.Decision)).Sentence,
            KeepSharePrepared.Settled settled => settled.Report.Sentence,
            _ => throw new InvalidOperationException("The list came back as neither ready nor settled."),
        };
        RaiseChanged();
    }

    private void OnSwitch(MealsView chosen)
    {
        view = chosen;
        selection = CardSelection.None;
        Picker().Close();
        RaiseChanged();
    }

    // ---- reading ----

    public override void Resume()
    {
        Ate?.LoadOnce();
        ExpireMove();
        if (read is null)
        {
            Loading = true;
        }

        Load();
        ScheduleExpiry();
    }

    public override void Pause()
    {
        handler.RemoveCallbacksAndMessages(null);
        cuisineCard?.Dismiss();
    }

    /// <summary>Reads the box again, as when the household file has just changed under it (a pass brought records home, the role became known).</summary>
    public void Reload() => Load();

    private void Load()
    {
        var number = Interlocked.Increment(ref loadNumber);
        var current = source;
        var currentWeek = weekSource;
        _ = Task.Run(() =>
        {
            var week = ReadWeek(currentWeek);
            RecipeBoxRead result;
            try
            {
                result = current.Read();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // The type only, never the message: the page keeps what it had, or says the box cannot be read.
                result = read ?? new RecipeBoxRead.Unavailable("The recipe box could not be read just now (" + e.GetType().Name + ").");
            }

            Host.RunOnUiThread(() => Adopt(result, week, number));
        });
    }

    private MealWeekScreenRead ReadWeek(IMealWeekSource current)
    {
        try
        {
            return current.Read(DateTimeOffset.Now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The type only, never the message: the page keeps the week it had, or says it cannot be read.
            return weekRead ?? new MealWeekScreenRead.Unavailable("The week could not be read just now (" + e.GetType().Name + ").");
        }
    }

    private void Adopt(RecipeBoxRead result, MealWeekScreenRead week, int number)
    {
        if (number != Volatile.Read(ref loadNumber))
        {
            return;
        }

        read = result;
        weekRead = week;
        Loading = false;
        RaiseChanged();
    }

    // ---- search, picking, putting back ----

    private void OnQuery(string text)
    {
        query = text;
        if (read is RecipeBoxRead.Ready ready)
        {
            boxPage?.Show(ScreenFor(ready.Snapshot));
        }
    }

    private void OnTap(Recipe card)
    {
        selection = selection.Tap(card.Id);
        Redraw();
    }

    private void OnPutBack()
    {
        selection = selection.PutBack();
        Redraw();
    }

    private void Redraw()
    {
        if (read is RecipeBoxRead.Ready ready && boxPage is not null)
        {
            boxPage.Show(ScreenFor(ready.Snapshot));
        }
        else
        {
            RaiseChanged();
        }
    }

    // ---- Cook this soon ----

    private void OnCook()
    {
        if (selection.Picked is not { } picked || read is not RecipeBoxRead.Ready ready)
        {
            return;
        }

        var current = cooks.TryGetValue(picked, out var state) ? state : CookSoon.StateOf(picked, PlannedNow(ready.Snapshot));
        var current2 = source;
        var cooking = TapWatch.Action(ConstitutionKeyActions.CookSoon, KeyActs.Local);
        _ = Task.Run(() =>
        {
            CookSoonState settled;
            string? sentence = null;
            try
            {
                settled = CookSoon.Press(current2, picked, current);
                cooking.Succeeded();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                settled = current;
                sentence = CookSoon.NotKeptWords + " (" + e.GetType().Name + ")";
                cooking.Failed(e.GetType().Name);
            }

            Host.RunOnUiThread(() => CookDone(picked, settled, sentence));
        });
    }

    private void CookDone(RecipeId card, CookSoonState settled, string? sentence)
    {
        cooks[card] = settled;
        notice = sentence;
        // The card now reads "Planned for next week" in place. Nothing else says so.
        Redraw();
        if (sentence is not null)
        {
            RaiseChanged();
        }
    }

    // ---- scanning a recipe card ----

    private void OnPrimary(string action)
    {
        if (action == RecipeBoxText.ScanAction)
        {
            StartScan();
        }
        else
        {
            Picker().Toggle();
            RaiseChanged();
        }

        PrimaryActionRequested?.Invoke(action);
    }

    // A row of the picker was tapped. A row that cannot be used leaves the picker open and says why in the header.
    private void OnPick(PickerSource source)
    {
        var choice = Picker().Choose(source);
        Begin(choice);
    }

    private void Begin(PickerChoice choice)
    {
        switch (choice.Started)
        {
            case PickerSource.Plate:
            case PickerSource.Camera:
                // Both phones photograph the pages; the owner's in Plate, the partner's in its own camera (D176).
                StartCamera();
                break;
            case PickerSource.TypeIn:
                OpenTyping();
                break;
            default:
                // Nothing started: the row's own sentence says why, and a row that is not there says nothing.
                notice = choice.Sentence;
                RaiseChanged();
                break;
        }
    }

    // ---- photographing the pages (C-6.5.2, D105, D176) ----

    // The scan row was tapped. The permission is asked for here, at the tap, after a sentence that says why; a phone without a
    // camera is told so. Nothing is asked at install or at launch.
    private void StartCamera()
    {
        if (cameraBusy)
        {
            return;
        }

        switch (CameraAccess.Decide(HasCamera(), CameraAllowed()))
        {
            case CameraStep.NoCamera:
                notice = CameraAccess.NoCameraSentence;
                RaiseChanged();
                break;
            case CameraStep.AskFirst:
                notice = CameraAccess.AskingSentence;
                RaiseChanged();
                AskForCamera(CameraCaptureLauncher.PermissionRequestCode);
                break;
            default:
                OpenCamera();
                break;
        }
    }

    /// <summary>
    /// Whether the camera is allowed right now. The one place the camera permission is checked, so "I ate something else" asks through
    /// here too (D105 keeps the permission to one asking place) rather than naming it again.
    /// </summary>
    public bool CameraAllowed() => Host.CheckSelfPermission(Android.Manifest.Permission.Camera) == Permission.Granted;

    /// <summary>The one place the camera permission is requested; the answer comes back to the shell under <paramref name="requestCode"/>.</summary>
    public void AskForCamera(int requestCode) => Host.RequestPermissions([Android.Manifest.Permission.Camera], requestCode);

    /// <summary>The camera permission's answer, handed over by the shell. A denial is said plainly and typing stays in the picker.</summary>
    public void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        if (requestCode != CameraCaptureLauncher.PermissionRequestCode)
        {
            return;
        }

        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
        {
            notice = null;
            OpenCamera();
            return;
        }

        notice = CameraAccess.DeniedSentence;
        photoLoop = null;
        RecordCamera(CameraEvent.PermissionDenied, null);
        RaiseChanged();
    }

    // One photo per page: opens the camera for the next page (Plate's on the owner's phone). Pages already taken are kept if the
    // camera cannot open.
    private void OpenCamera()
    {
        if (cameraBusy || reading)
        {
            return;
        }

        photoLoop ??= new RecipePhotoLoop();
        capture = new CameraCaptureLauncher(Host, CameraCaptureLauncher.RequestCode, role == InstallRole.Partner ? null : PlateCamera.Package);
        var opening = TapWatch.Action(ConstitutionKeyActions.ScanRecipe, TimeSpan.FromSeconds(15));
        if (capture.Start() is { } sentence)
        {
            opening.Failed(sentence);
            notice = sentence;
            if (!photoLoop.CanFinish)
            {
                photoLoop = null;
            }

            RaiseChanged();
            return;
        }

        opening.Succeeded();
        notice = null;
        cameraBusy = true;
    }

    // The camera has answered. A backed-out capture leaves the pages taken so far as they were and the photograph deleted.
    private void OnCameraResult(Result resultCode)
    {
        // An app the phone ended while the camera was open has lost its loop; the page just taken still counts as the first.
        photoLoop ??= new RecipePhotoLoop();
        capture ??= new CameraCaptureLauncher(Host);
        if (resultCode != Result.Ok)
        {
            capture.Discard();
            cameraBusy = false;
            notice = photoLoop.CanFinish ? photoLoop.Status : RecipeScanText.CancelledSentence;
            if (!photoLoop.CanFinish)
            {
                photoLoop = null;
            }

            RaiseChanged();
            return;
        }

        cameraBusy = true;
        notice = null;
        RaiseChanged();
        TakePhoto(capture.PhotoPath);
    }

    // The photograph is shrunk and re-encoded into memory off the UI thread, and its file deleted whatever that came to.
    private void TakePhoto(string photo)
    {
        _ = Task.Run(() =>
        {
            byte[]? page = null;
            Exception? failure = null;
            try
            {
                page = AtePhotoScaler.Read(photo);
            }
            catch (Exception e) when (e is IOException or ArgumentException or Java.Lang.Exception)
            {
                // The type only is recorded and the sentence is fixed.
                failure = e;
            }
            finally
            {
                var gone = CameraCaptureLauncher.Delete(photo);
                RecordCamera(gone ? CameraEvent.PhotoDeleted : CameraEvent.PhotoKept, null);
                Host.RunOnUiThread(() => PhotoTaken(page, failure));
            }
        });
    }

    private void PhotoTaken(byte[]? page, Exception? failure)
    {
        cameraBusy = false;
        photoLoop ??= new RecipePhotoLoop();
        if (page is null)
        {
            RecordCamera(CameraEvent.ReadFailed, failure);
            notice = RecipePhotoLoop.EmptyPhotoSentence;
        }
        else
        {
            var added = photoLoop.AddPage(page);
            RecordCamera(added ? CameraEvent.PageRead : CameraEvent.NothingRead, null);
            notice = photoLoop.Status;
        }

        if (!photoLoop.CanFinish)
        {
            photoLoop = null;
        }

        RaiseChanged();
    }

    // Read the recipe: the pages go to the reader, in order, off the UI thread; what comes back fills the three boxes to be edited.
    private void FinishCamera()
    {
        if (photoLoop is null || !photoLoop.CanFinish || cameraBusy || reading)
        {
            return;
        }

        var pages = photoLoop.Take();
        var reader = photoReader;
        reading = true;
        notice = RecipePhotoText.ReadingSentence(pages.Count);
        RaiseChanged();
        _ = Task.Run(async () =>
        {
            RecipePhotoRead? answer = null;
            Exception? failure = null;
            try
            {
                answer = await reader.ReadAsync(pages, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
            {
                failure = e;
            }
            finally
            {
                Host.RunOnUiThread(() => PagesRead(answer, failure));
            }
        });
    }

    private void PagesRead(RecipePhotoRead? answer, Exception? failure)
    {
        reading = false;
        photoLoop = null;
        switch (answer)
        {
            case RecipePhotoRead.Read read:
                RecordCamera(CameraEvent.RecipeRead, null);
                typed.Fill(read.Payload);
                typing = true;
                notice = null;
                break;
            case RecipePhotoRead.Unavailable unavailable:
                notice = unavailable.Sentence;
                break;
            case RecipePhotoRead.Unreadable unreadable:
                RecordCamera(CameraEvent.NothingRead, null);
                notice = unreadable.Sentence;
                break;
            default:
                RecordCamera(CameraEvent.ReadFailed, failure);
                notice = failure is null
                    ? RecipePhotoText.UnreadableSentence
                    : RecipeScanText.NotReadPrefix + PlainError.Describe(failure, "Claude");
                break;
        }

        RaiseChanged();
    }

    // The camera trail: the step and, for a failure, the exception's type. Never a word of the card and never a path.
    private void RecordCamera(CameraEvent step, Exception? failure) =>
        new ConstitutionVaultLog(Host.ApplicationContext ?? Host).Outcome(RecipeCameraTrail.LogPhrase(step, failure));

    // Scan a recipe card from the bar: this install's scan row, chosen without opening the picker.
    private void StartScan()
    {
        if (cameraBusy || reading)
        {
            return;
        }

        Begin(Picker().ScanChoice());
    }

    /// <summary>The camera's answer, handed over by the shell. A back-out is a sentence; a page taken is added to the loop.</summary>
    public void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == CameraCaptureLauncher.RequestCode)
        {
            OnCameraResult(resultCode);
        }
    }

    // ---- typing a card in ----

    private void OpenTyping()
    {
        notice = null;
        typing = true;
        RaiseChanged();
    }

    // Let go: the fields empty at once and the dog-ear holds the words for its window; a fold turned brings them back.
    private void LetGoOfTyping()
    {
        ConnectionNoticeBoard.With(board =>
        {
            typedUndo.Begin(board, typed, DateTimeOffset.UtcNow);
            return true;
        });
        typing = false;
        notice = null;
        RaiseChanged();
        ScheduleExpiry();
    }

    // Keep the card: the three boxes, as the owner left them, become a card and go through the keeper off the UI thread. Only a
    // kept card leaves the page; anything that stops it is said in the status line and the words stay to be fixed.
    private void KeepTyped()
    {
        if (keeping)
        {
            return;
        }

        var read = typed.Read();
        if (read.Payload is not { } payload)
        {
            notice = read.Sentence;
            RaiseChanged();
            return;
        }

        var draft = ScanConfirmModel.From(payload, null, typed.Origin);
        var target = keeper;
        var taken = ExistingIds();
        keeping = true;
        RaiseChanged();
        _ = Task.Run(() =>
        {
            ScanKeepResult result;
            try
            {
                result = draft.Keep(target, taken);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                result = new ScanKeepResult(ScanKeepOutcome.NotKept, null, ScanConfirmModel.NotKeptSentence + " (" + e.GetType().Name + ")");
            }

            Host.RunOnUiThread(() => KeepDone(result));
        });
    }

    private void KeepDone(ScanKeepResult result)
    {
        keeping = false;
        if (result.Outcome is ScanKeepOutcome.Kept or ScanKeepOutcome.AlreadyKept)
        {
            // The card is in the box now; the page goes back to the box and reads it again, so the card shows in its pile.
            typing = false;
            typed.Clear();
            notice = null;
            view = MealsView.RecipeBox;
            Load();
            return;
        }

        notice = result.Sentence;
        RaiseChanged();
    }

    private HashSet<RecipeId> ExistingIds() =>
        read is RecipeBoxRead.Ready ready ? ready.Snapshot.Cards.Select(card => card.Id).ToHashSet() : [];

    // ---- moving a card's cuisine, with undo ----

    private void OnHold(Recipe card)
    {
        if (read is not RecipeBoxRead.Ready)
        {
            return;
        }

        cuisineCard?.Dismiss();
        cuisineCard = new CuisineCardView(Host, CuisineChoices.For(card), cuisine => BeginMove(card, cuisine), () => cuisineCard = null);
    }

    private void BeginMove(Recipe card, Cuisine cuisine)
    {
        cuisineCard?.Dismiss();
        if (read is not RecipeBoxRead.Ready ready)
        {
            return;
        }

        var shown = undo.Show(ready.Snapshot.Cards);
        var move = new RecipeBox(shown).Move(card, cuisine);
        if (move is null)
        {
            return;
        }

        ConnectionNoticeBoard.With(board =>
        {
            undo.Begin(board, move, DateTimeOffset.UtcNow);
            return true;
        });
        notice = null;
        Redraw();
        KeepMove(move, CuisineMoveUndo.NotKeptSentence);
        ScheduleExpiry();
    }

    /// <summary>The shell turned the fold and the board gave back this act: the card goes back to where it was filed.</summary>
    public void UndoTurned(string actKey)
    {
        if (typedUndo.Reverse(actKey) is { } words)
        {
            // The words that were let go come back into the fields, and the page with them.
            typed.Restore(words);
            typing = true;
            handler.RemoveCallbacksAndMessages(null);
            return;
        }

        if (undo.Reverse(actKey) is { } inverse)
        {
            handler.RemoveCallbacksAndMessages(null);
            KeepMove(inverse, CuisineMoveUndo.NotUndoneSentence);
        }
    }

    // Writes a move (or its inverse) down off the UI thread, then reads the box again so the page shows what is kept.
    private void KeepMove(CuisineMove move, string sentenceIfNotKept)
    {
        var current = source;
        _ = Task.Run(() =>
        {
            string? sentence = null;
            try
            {
                current.Move(move);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                sentence = sentenceIfNotKept + " (" + e.GetType().Name + ")";
            }

            Host.RunOnUiThread(() =>
            {
                notice = sentence;
                Load();
            });
        });
    }

    private void ScheduleExpiry()
    {
        if (undo.Pending is null && typedUndo.Pending is null)
        {
            return;
        }

        handler.RemoveCallbacksAndMessages(null);
        handler.PostDelayed(() =>
        {
            ExpireMove();
            Redraw();
        }, (long)NoticeBoard.UndoWindow.TotalMilliseconds + 1);
    }

    private void ExpireMove()
    {
        var now = DateTimeOffset.UtcNow;
        undo.Expire(now);
        typedUndo.Expire(now);
    }
}
