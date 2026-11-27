using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Domain.Design;
using Watch.Ui.Wear;

namespace Dash.Wear;

/// <summary>
/// Filing or deleting a correspondence, undo-first (W-5.1.4; A14, WD §9, O5). A row's long press on People waiting
/// opens it on the choice, laid out as After the call is drawn (the head, the name, File pinned, Delete under it: no
/// mockup draws this screen). A choice is the act, at once and with no confirmation: it goes to the phone under the
/// watch's own name with the window the ring shows, and the screen becomes the mockup's Filed, with Undo (the glyph and
/// its emptying ring, "Lee filed", "Tap to undo"). A tap anywhere takes it back; when the ring empties, or the screen is
/// left, the act is final and the screen goes. People waiting is told whether the row has left, so it leaves at once.
/// An act or an undo that never reached the phone is said as a quiet line and nothing is claimed.
/// </summary>
[Activity(Name = WatchIdentity.DashActActivityName, Exported = false)]
public sealed class ActActivity : Activity
{
    private Round round = null!;
    private Frame frame = null!;
    private DashGlyphView glyph = null!;
    private WatchTextView heading = null!;
    private WatchPillView file = null!;
    private WatchTextView delete = null!;
    private WatchTextView under = null!;
    private UndoRingView ring = null!;
    private string key = string.Empty;
    private string name = string.Empty;
    private WristAct? act;
    private long openedAt;

    public static Intent For(Context context, string key, string name) =>
        new Intent(context, typeof(ActActivity)).PutExtra(DashExtras.Key, key).PutExtra(DashExtras.Title, name);

    /// <summary>The correspondence a finished act took off People waiting, if it did.</summary>
    public static string? Left(Intent? data) =>
        data?.GetBooleanExtra(DashExtras.Left, false) == true ? data.GetStringExtra(DashExtras.Key) : null;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        key = Intent?.GetStringExtra(DashExtras.Key) ?? string.Empty;
        name = Intent?.GetStringExtra(DashExtras.Title) ?? string.Empty;
        if (!DashIds.IsCorrespondence(key) || string.IsNullOrWhiteSpace(name) || savedInstanceState?.GetString(DashExtras.Act) is not null)
        {
            // Nothing to act on, or a screen rebuilt after its act: the act stands as the phone holds it, and People
            // waiting asks the phone again.
            key = string.Empty;
            Finish();
            return;
        }

        round = new Round(this);
        frame = Screens.Build(this, round, 0, 0);

        glyph = new DashGlyphView(this);
        frame.Root.AddView(glyph, new FrameLayout.LayoutParams(round.Px(DashLayout.GlyphSide), round.Px(DashLayout.GlyphSide), GravityFlags.Top | GravityFlags.CenterHorizontal)
        {
            TopMargin = round.Px(DashLayout.GlyphTop),
        });

        ring = new UndoRingView(this) { GlyphCentre = DashLayout.UndoGlyphCentre, Visibility = ViewStates.Gone };
        frame.Root.AddView(ring, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        heading = new WatchTextView(this) { Type = WatchType.Glance, Weight = WatchWeight.SemiBold, Label = name };
        Place(heading, DashLayout.ActTitleCentre);
        under = new WatchTextView(this) { Visibility = ViewStates.Gone };
        Place(under, DashLayout.UndoPromptCentre);

        file = Screens.Pin(this, frame, round, DashWords.File, DashLayout.ActPillWidth, DashLayout.ActPillCentre);
        file.Click += (_, _) => Act(WristActKind.File);
        delete = Screens.Choice(this, DashWords.Delete);
        var target = (int)Math.Ceiling(WatchPaints.TouchTarget(this));
        frame.Root.AddView(delete, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, target, GravityFlags.Top | GravityFlags.CenterHorizontal)
        {
            TopMargin = round.Px(DashLayout.ActChoiceCentre) - target / 2,
        });
        delete.Click += (_, _) => Act(WristActKind.Delete);
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        outState.PutString(DashExtras.Act, act?.Id);
    }

    // The choice is the act (WD §9: no confirmation): it goes at once, and the ring opens on exactly the window it carries.
    private void Act(WristActKind kind)
    {
        if (act is not null)
        {
            return;
        }

        var window = UndoRingView.WindowFor(this);
        act = kind == WristActKind.File
            ? WristAct.File(key, name, window, Guid.NewGuid())
            : WristAct.Delete(key, name, window, Guid.NewGuid());

        glyph.Visibility = ViewStates.Gone;
        file.Visibility = ViewStates.Gone;
        delete.Visibility = ViewStates.Gone;
        Move(heading, DashLayout.UndoTitleCentre);
        ring.Visibility = ViewStates.Visible;
        openedAt = SystemClock.UptimeMillis();
        ring.Open(act.Window, Undo, Closed);

        var context = ApplicationContext!;
        var acting = act;
        Screens.WhenDone(this, Task.Run(() => ActCarrier.ActAsync(context, acting)), Render);
        Render();
    }

    // A tap on the ring, inside its window: the undo goes once the phone has the act.
    private void Undo()
    {
        if (act?.Undo(TimeSpan.FromMilliseconds(SystemClock.UptimeMillis() - openedAt)) is { } undo)
        {
            var context = ApplicationContext!;
            var taking = act;
            Screens.WhenDone(this, Task.Run(() => ActCarrier.UndoAsync(context, taking, undo)), Render);
        }
        Render();
    }

    // The ring emptied, or the screen went: the act is final once the phone has it.
    private void Closed()
    {
        act?.Closed();
        Render();
    }

    // Success is the screen changing (WD §6): the ring and its words while it can be taken back, gone when it is final.
    private void Render()
    {
        if (act is null)
        {
            return;
        }

        SetResult(Result.Ok, new Intent().PutExtra(DashExtras.Key, key).PutExtra(DashExtras.Left, act.Leaves));
        if (act.Finished)
        {
            if (!IsFinishing)
            {
                Finish();
            }
            return;
        }

        heading.Label = act.Heading;
        if (act.Prompt is { } prompt)
        {
            under.Label = prompt;
            under.SetTextColor(WatchPaints.Of(WatchContrast.Accent(WatchPaints.Glass(WatchApp.Dash, WatchPaints.Now()))));
            under.Visibility = ViewStates.Visible;
        }
        else if (act.Line is { } line)
        {
            under.Tone = WatchTone.Soft;
            under.Sentence = line;
            under.Visibility = ViewStates.Visible;
        }
        else
        {
            under.Visibility = ViewStates.Gone;
        }
    }

    // Words centred on a line the mockup draws at `centre` (a fraction of the radius from the top).
    private void Place(WatchTextView words, double centre)
    {
        frame.Root.AddView(words, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top)
        {
            LeftMargin = round.Px(DashLayout.RowLeft),
            RightMargin = round.Px(DashLayout.RowLeft),
            TopMargin = Math.Max(0, round.Px(centre) - words.LineHeight / 2),
        });
    }

    private void Move(WatchTextView words, double centre)
    {
        if (words.LayoutParameters is FrameLayout.LayoutParams placed)
        {
            placed.TopMargin = Math.Max(0, round.Px(centre) - words.LineHeight / 2);
            words.LayoutParameters = placed;
        }
    }
}
