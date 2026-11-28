using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Domain.Kit;
using Watch.Ui.Wear;

namespace Dash.Wear;

/// <summary>
/// After the call (W-5.1.5, A13; mock: After the call): once a call the watch's own Phone app carried has ended, and Post
/// has said which call it was (<see cref="PeopleActivity"/> asks, by the moment the call ended), Dash offers the note once:
/// the head, "Call with Robin", "6 min, just now", Add a note pinned and Not now under it. A caller the phone's
/// book has no name for is "A call"; the watch is never told a number, and reads none of its own.
///
/// <para>Add a note opens the system's dictation; the words come back read out under "Note on this call" (the phone's own
/// words; no mockup draws this half, so it follows Replying by voice), with Keep pinned and Keyboard and Again either
/// side. Keep tells Post the note on the pool, by the call's id and the moment it was written (<see cref="CallNoteAttempt"/>),
/// so a note kept again after "Your phone isn’t in reach." is kept once; "Keeping the note." while it goes, and when the
/// phone has it the screen simply goes (WD §6). Not now, or leaving, is the end of it: the offer is never made again.</para>
/// </summary>
[Activity(Name = WatchIdentity.DashAfterCallActivityName, Exported = false)]
public sealed class AfterCallActivity : Activity
{
    private const int DictationRequest = 1;
    private const int KeyboardRequest = 2;

    private Round round = null!;
    private Frame frame = null!;
    private readonly List<View> offer = [];
    private readonly List<View> readBack = [];
    private WatchTextView said = null!;
    private WatchPillView keep = null!;
    private DashCall call = null!;
    private string? words;
    private CallNoteAttempt? attempt;
    private bool keeping;

    /// <summary>After the call for <paramref name="call"/>, which Post named, timed as <see cref="AfterCall.LengthOf"/> says.</summary>
    public static Intent For(Context context, DashCall call, CallEnd end) =>
        new Intent(context, typeof(AfterCallActivity))
            .PutExtra(DashExtras.Call, call.Call)
            .PutExtra(DashExtras.Title, call.Name)
            .PutExtra(DashExtras.Length, AfterCall.LengthOf(call, end) is { } length ? (long)length.TotalMilliseconds : -1L)
            .PutExtra(DashExtras.Ended, end.Ended.ToUnixTimeMilliseconds());

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var id = Intent?.GetStringExtra(DashExtras.Call);
        var ended = Intent?.GetLongExtra(DashExtras.Ended, 0) ?? 0;
        if (!DashIds.IsMessage(id) || ended <= 0)
        {
            DashLog.Warn("DASH After the call was opened with no call");
            Finish();
            return;
        }
        var length = Intent?.GetLongExtra(DashExtras.Length, -1) ?? -1;
        call = new DashCall(
            DateTimeOffset.FromUnixTimeMilliseconds(ended), id, Intent?.GetStringExtra(DashExtras.Title),
            length >= 0 ? TimeSpan.FromMilliseconds(length) : null);

        round = new Round(this);
        var pill = (int)Math.Ceiling(WatchPaints.TouchTarget(this));
        frame = Screens.Build(this, round, round.Px(DashLayout.ToCentre / 2), round.Px(2 - DashLayout.SendCentre) + pill);
        BuildOffer();
        BuildReadBack();

        if (savedInstanceState?.GetString(DashExtras.Words) is { } kept)
        {
            Restore(savedInstanceState, kept);
            return;
        }
        Show(readingBack: false);
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        outState.PutString(DashExtras.Words, words);
        outState.PutLong(DashExtras.At, attempt?.At.ToUnixTimeMilliseconds() ?? 0);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        var given = resultCode != Result.Ok ? null
            : requestCode == DictationRequest ? SystemInput.Dictated(data)
            : requestCode == KeyboardRequest ? SystemInput.Typed(data)
            : null;
        if (!string.IsNullOrWhiteSpace(given))
        {
            // Backed out with nothing said: the screen is as it was, the offer or the words before.
            words = given.Trim();
            Show(readingBack: true);
        }
    }

    // mock: After the call. The head, the call, how long and when, Add a note pinned and Not now under it.
    private void BuildOffer()
    {
        var glyph = new DashGlyphView(this);
        frame.Root.AddView(glyph, new FrameLayout.LayoutParams(round.Px(DashLayout.GlyphSide), round.Px(DashLayout.GlyphSide), GravityFlags.Top | GravityFlags.CenterHorizontal)
        {
            TopMargin = round.Px(DashLayout.GlyphTop),
        });
        offer.Add(glyph);

        var heading = new WatchTextView(this) { Type = WatchType.Glance, Weight = WatchWeight.SemiBold, Label = DashWords.CallWith(call.Name) };
        offer.Add(Place(heading, DashLayout.AfterCallTitleCentre));
        var when = WhenWords.Relative(call.Ended, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        var line = new WatchTextView(this) { Type = WatchType.Small, Tone = WatchTone.Soft, Label = DashWords.CallLine(call.Length, when) };
        offer.Add(Place(line, DashLayout.AfterCallLineCentre));

        var add = Screens.Pin(this, frame, round, DashWords.AddANote, DashLayout.AfterCallPillWidth, DashLayout.AfterCallPillCentre);
        add.Click += (_, _) => Dictate();
        offer.Add(add);

        var notNow = Screens.Choice(this, DashWords.NotNow);
        notNow.Click += (_, _) => Finish();
        var target = (int)Math.Ceiling(WatchPaints.TouchTarget(this));
        frame.Root.AddView(notNow, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, target, GravityFlags.Top | GravityFlags.CenterHorizontal)
        {
            TopMargin = round.Px(DashLayout.AfterCallChoiceCentre) - target / 2,
        });
        offer.Add(notNow);
    }

    // The note read back, on Replying by voice's layout (no mockup draws it): "Note on this call" over the words, Keep
    // pinned, Keyboard and Again either side.
    private void BuildReadBack()
    {
        var about = new WatchTextView(this) { Tone = WatchTone.Soft, Weight = WatchWeight.Medium, Label = DashWords.NoteOnThisCall };
        frame.Column.AddView(about, Centred(round.Px(DashLayout.RowLeft)));
        said = new WatchTextView(this) { Body = string.Empty };
        var aside = round.Px((2 - DashLayout.WordsWidth) / 2);
        frame.Column.AddView(said, Centred(aside, round.Px(DashLayout.WordsTop - DashLayout.ToCentre) / 2));
        frame.Column.AddView(frame.Status, Centred(0));
        readBack.Add(frame.Scroll);

        keep = Screens.Pin(this, frame, round, DashWords.Keep, DashLayout.SendWidth, DashLayout.SendCentre);
        keep.Click += (_, _) => Keep();
        readBack.Add(keep);
        readBack.Add(Choose(Screens.Choice(this, DashWords.Keyboard), -DashLayout.SecondaryOffset, Type));
        readBack.Add(Choose(Screens.Choice(this, DashWords.Again), DashLayout.SecondaryOffset, Dictate));
    }

    private void Show(bool readingBack)
    {
        foreach (var view in offer)
        {
            view.Visibility = readingBack ? ViewStates.Gone : ViewStates.Visible;
        }
        foreach (var view in readBack)
        {
            view.Visibility = readingBack ? ViewStates.Visible : ViewStates.Gone;
        }
        if (!readingBack)
        {
            return;
        }

        said.Body = words ?? string.Empty;
        var check = CallNoteAttempt.Check(words);
        Screens.Say(frame.Status, check == SendCheck.TooLong ? DashWords.NoteTooLong : null);
        keep.Enabled = check == SendCheck.Fits && !keeping;
        frame.Scroll.RequestFocus();
    }

    private void Dictate()
    {
        if (SystemInput.Dictation(this, DashWords.NoteOnThisCall) is { } dictation)
        {
            StartActivityForResult(dictation, DictationRequest);
            return;
        }
        DashLog.Warn("DASH this watch has no dictation; the keyboard instead");
        Type();
    }

    private void Type()
    {
        if (SystemInput.Keyboard(this, DashWords.NoteOnThisCall) is { } keyboard)
        {
            StartActivityForResult(keyboard, KeyboardRequest);
            return;
        }
        DashLog.Warn("DASH this watch has neither dictation nor a keyboard to open");
        Show(readingBack: true);
        Screens.Say(frame.Status, DashWords.For(DashTrouble.NoInput));
    }

    // The same words go again as the same note, so Post keeps it once however many times it is told.
    private void Keep()
    {
        if (keeping || words is null || CallNoteAttempt.Check(words) != SendCheck.Fits)
        {
            return;
        }

        attempt = CallNoteAttempt.Keep(attempt, call, words, DateTimeOffset.UtcNow);
        keeping = true;
        keep.Enabled = false;
        Screens.Say(frame.Status, DashWords.Keeping);
        var context = ApplicationContext!;
        var note = attempt.Message;
        Screens.WhenTold(this, Task.Run(() => PostLink.Shared.TellAsync(context, note)), Kept);
    }

    private void Kept(DashTrouble? trouble)
    {
        keeping = false;
        if (trouble is null)
        {
            // The phone has it: success is silent, and the screen goes (WD §6).
            Finish();
            return;
        }
        Screens.Say(frame.Status, DashWords.For(trouble.Value));
        keep.Enabled = true;
    }

    private void Restore(Bundle saved, string kept)
    {
        words = kept;
        var at = saved.GetLong(DashExtras.At, 0);
        if (at > 0 && CallNoteAttempt.Check(kept) == SendCheck.Fits)
        {
            attempt = CallNoteAttempt.For(call, kept, DateTimeOffset.FromUnixTimeMilliseconds(at));
        }
        Show(readingBack: true);
    }

    // Words centred on a line the mockup draws at `centre` (a fraction of the radius from the top).
    private View Place(WatchTextView placed, double centre)
    {
        frame.Root.AddView(placed, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top)
        {
            LeftMargin = round.Px(DashLayout.RowLeft),
            RightMargin = round.Px(DashLayout.RowLeft),
            TopMargin = Math.Max(0, round.Px(centre) - placed.LineHeight / 2),
        });
        return placed;
    }

    // A quieter choice either side of the centre at the mockup's height (mock: Keyboard, Again).
    private WatchTextView Choose(WatchTextView choice, double offset, Action then)
    {
        var target = (int)Math.Ceiling(WatchPaints.TouchTarget(this));
        frame.Root.AddView(choice, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, target, GravityFlags.Top | GravityFlags.CenterHorizontal)
        {
            TopMargin = round.Px(DashLayout.SecondaryCentre) - target / 2,
            LeftMargin = offset < 0 ? 0 : round.Px(offset * 2),
            RightMargin = offset < 0 ? round.Px(-offset * 2) : 0,
        });
        choice.Click += (_, _) => then();
        return choice;
    }

    private static LinearLayout.LayoutParams Centred(int aside, int top = 0) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = aside, RightMargin = aside, TopMargin = top };
}
