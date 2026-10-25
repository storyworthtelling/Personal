using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Codex.Ui;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Notices;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;
using Constitution.Domain.Today;
using Constitution.MealsTab;
using Constitution.Settings;
using Constitution.WorkoutsTab;

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
/// <para>Scanning a recipe card (book p.14, PBI C-6.5.3): the pinned bar's Scan a recipe card starts Plate's recipe read through
/// <see cref="ScanRecipeLauncher"/>; the words come back to <see cref="OnActivityResult"/>, become a <see cref="ScanConfirmModel"/>
/// and take the whole page until the owner says Looks right (the card goes to <see cref="Keeper"/>, the only way it is kept) or
/// scans again (the old draft waits out the dog-ear's undo window, then its words are forgotten). Plate returns text only (D99), so
/// nothing about a picture exists here; <see cref="WordJudge"/> is the seam for p.14's doubted words and offers none until built.</para>
///
/// <para>Moving a card's cuisine happens on the page at once and folds the shell's one dog-ear (the shared notice board) for
/// the undo window; a turn of the fold applies the move's inverse. Cook this soon is the card changing to "Planned for next
/// week": no toast. This week reads the approved week through <see cref="WeekSource"/> and says so plainly when there is none.</para>
/// </summary>
public sealed class MealsArea : ConstitutionAreaBase, IUndoableArea
{
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly CuisineMoveUndo undo = new();
    private readonly ScanDiscardUndo scanUndo = new();
    private readonly TypedEntryUndo typedUndo = new();
    private readonly TypedEntry typed = new();
    private readonly ScanRecipeLauncher scanLauncher;
    private readonly Dictionary<RecipeId, CookSoonState> cooks = [];

    private IRecipeBoxSource source = new NoRecipeBoxSource();
    private IRecipeKeeper keeper = new NoRecipeKeeper();
    private IRecipeWordJudge wordJudge = new NoRecipeWordJudge();
    private ScanConfirmModel? scan;
    private ScanReadPage? scanPage;
    private bool scanBusy;
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

    public MealsArea(Activity host)
        : base(host, "meals", Resource.Drawable.areameals)
    {
        scanLauncher = new ScanRecipeLauncher(host);
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

    /// <summary>The second reading of a scan's words, for p.14's doubted words; none is built, so none are offered.</summary>
    public IRecipeWordJudge WordJudge
    {
        get => wordJudge;
        set => wordJudge = value;
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

    // No phone can read a photograph of a card yet (C-6.5.2 waits on a dependency decision), and the paste row waits on written
    // permission (PC8, D82), so the picker is built without either and says so in its rows.
    private RecipePickerModel Picker() => picker ??= RecipePickerModel.For(role, cameraCanRead: false, pasteAllowed: false);

    public override string? Subtitle => notice;

    protected override string LoadingSentence => "Reading the recipe box.";

    // ---- the page ----

    protected override View BuildPage()
    {
        boxPage = null;
        scanPage = null;
        if (typing)
        {
            return new PickerTypedPage(Host, typed);
        }

        if (scan is not null)
        {
            scanPage = new ScanReadPage(Host, scan, OnChoose, OnOther, OnTitle);
            return scanPage;
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
        return model.State == WeekState.Planned ? new ThisWeekPage(Host, model) : PageFurniture.Sentence(Host, model.Status);
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
            var check = PaperUi.PrimaryButton(Host, TypedEntry.CheckAction, CheckTyping);
            return PaperUi.BottomBar(Host, letGo, check);
        }

        if (scan is not null)
        {
            var again = PaperUi.SecondaryButton(Host, scan.ScanAgainAction, ScanAgain);
            again.Enabled = !scanBusy;
            var looksRight = PaperUi.PrimaryButton(Host, scan.KeepAction, KeepScan);
            looksRight.Enabled = !scanBusy;
            return PaperUi.BottomBar(Host, again, looksRight);
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
        button.Enabled = action != RecipeBoxText.ScanAction || !scanBusy;
        if (action == RecipeBoxText.ScanAction)
        {
            // Short of ten the scan is the page's one invitation; typing is still always there beside it.
            var typeIt = PaperUi.SecondaryButton(Host, RecipePickerText.TypeInTitle, OpenTyping);
            return PaperUi.BottomBar(Host, typeIt, button);
        }

        var bar = PaperUi.BottomBar(Host, button);
        return Picker().IsOpen ? new PickerSheet(Host, Picker().Rows, OnPick, bar) : bar;
    }

    // "I ate something else" stays in the bar on this view (book p.13). It is built with F-6.6; until then it is drawn disabled, as on Today.
    private View? ThisWeekBar()
    {
        if (weekRead is not MealWeekScreenRead.Ready)
        {
            return null;
        }

        var button = PaperUi.SecondaryButton(Host, MealsWeekModel.ActionLabel, () => { });
        button.Enabled = TodayModel.ActionIsLive;
        return PaperUi.BottomBar(Host, button);
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
        _ = Task.Run(() =>
        {
            CookSoonState settled;
            string? sentence = null;
            try
            {
                settled = CookSoon.Press(current2, picked, current);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                settled = current;
                sentence = CookSoon.NotKeptWords + " (" + e.GetType().Name + ")";
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

    // Scan again: a scanned draft asks Plate once more; a typed one reopens the three fields with its words still in them.
    private void ScanAgain()
    {
        if (scan?.Origin == ScanOrigin.Typed)
        {
            OpenTyping();
            return;
        }

        StartScan();
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
                LaunchPlate();
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

    // Check the card: the three fields become the same words a scan gives and take the same checking step.
    private void CheckTyping()
    {
        var read = typed.Read();
        if (read.Payload is not { } payload)
        {
            notice = read.Sentence;
            RaiseChanged();
            return;
        }

        typing = false;
        TakeDraft(ScanConfirmModel.From(payload, null, ScanOrigin.Typed), null);
    }

    // Opens Plate. A draft already on the page stays until a new read replaces it, so backing out of Plate loses nothing.
    private void StartScan()
    {
        if (scanBusy)
        {
            return;
        }

        Begin(Picker().ScanChoice());
    }

    private void LaunchPlate()
    {
        if (scanBusy)
        {
            return;
        }

        if (scanLauncher.Start() is { } sentence)
        {
            notice = sentence;
            RaiseChanged();
            return;
        }

        notice = null;
        scanBusy = true;
    }

    /// <summary>The answer from Plate, handed over by the shell. A back-out is quiet; an unusable answer is a sentence.</summary>
    public void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != ScanRecipeLauncher.RequestCode)
        {
            return;
        }

        scanBusy = false;
        switch (ScanRecipeLauncher.Read(resultCode, data))
        {
            case ScanReturn.Read read:
                BeginDraft(read.Payload);
                break;
            case ScanReturn.Refused refused:
                notice = refused.Sentence;
                RaiseChanged();
                break;
            case ScanReturn.Cancelled:
                // Said in a sentence, so a button that seemed to do nothing does not look broken. A draft on the page is untouched.
                notice = ScanReturns.CancelledSentence(scan is not null);
                RaiseChanged();
                break;
        }
    }

    // The second reading, if one is ever built, may be slow, so the draft is made off the UI thread.
    private void BeginDraft(RecipeReadPayload payload)
    {
        var judge = wordJudge;
        _ = Task.Run(() =>
        {
            ScanConfirmModel draft;
            string? sentence = null;
            try
            {
                draft = ScanConfirmModel.From(payload, judge);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // The type only, never the message: the card is shown without doubts rather than not at all.
                draft = ScanConfirmModel.From(payload);
                sentence = "The words could not be double-checked (" + e.GetType().Name + ").";
            }

            Host.RunOnUiThread(() => TakeDraft(draft, sentence));
        });
    }

    private void TakeDraft(ScanConfirmModel draft, string? sentence)
    {
        if (scan is { } earlier)
        {
            ConnectionNoticeBoard.With(board =>
            {
                scanUndo.Begin(board, earlier, DateTimeOffset.UtcNow);
                return true;
            });
        }

        scan = draft;
        notice = sentence;
        RaiseChanged();
        ScheduleExpiry();
    }

    private void OnChoose(int question, int option)
    {
        scan?.Choose(question, option);
        ShowScan();
    }

    private void OnOther(int question, string text)
    {
        if (scan?.ChooseOther(question, text) == true)
        {
            ShowScan();
        }
    }

    private void OnTitle(string text) => scan?.SetTitle(text);

    private void ShowScan()
    {
        if (scan is not null)
        {
            scanPage?.Show(scan);
        }
    }

    // Looks right. The card goes through the keeper, off the UI thread; only a kept card leaves the page.
    private void KeepScan()
    {
        if (scan is not { } draft || scanBusy)
        {
            return;
        }

        var target = keeper;
        var taken = ExistingIds();
        scanBusy = true;
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

            Host.RunOnUiThread(() => KeepDone(draft, result));
        });
    }

    private void KeepDone(ScanConfirmModel draft, ScanKeepResult result)
    {
        scanBusy = false;
        if (!ReferenceEquals(scan, draft))
        {
            return;
        }

        if (result.Outcome is ScanKeepOutcome.Kept or ScanKeepOutcome.AlreadyKept or ScanKeepOutcome.NothingToKeep)
        {
            // The card is in the box now; the page goes back to the box and reads it again, so the card shows in its pile.
            scan = null;
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
            typed.Title = words.Title;
            typed.IngredientsText = words.IngredientsText;
            typed.StepsText = words.StepsText;
            typing = true;
            handler.RemoveCallbacksAndMessages(null);
            return;
        }

        if (scanUndo.Reverse(actKey) is { } brought)
        {
            // The draft the owner scanned over comes back; the read that replaced it is let go.
            var replaced = scan;
            scan = brought;
            replaced?.Discard();
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
        if (undo.Pending is null && scanUndo.Pending is null && typedUndo.Pending is null)
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
        scanUndo.Expire(now);
        typedUndo.Expire(now);
    }
}
