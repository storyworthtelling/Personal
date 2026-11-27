using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Ui.Wear;

namespace Dash.Wear;

/// <summary>
/// A new text (W-5.1.2, A10; P14 B6): the phone's Rolodex, searched or scrolled, and a contact picked. No mockup draws
/// it, so it follows the nearest drawn screens: the name at the top as Mail and a conversation draw theirs, the contacts
/// on the card as People waiting's rows, and the two quieter choices at the foot as Replying by voice draws Keyboard
/// and Again: Say a name (the system's dictation; "by voice" is the name said into the search, not a matching service)
/// and Keyboard (the system's input screen). The letters found are read back in the ink, with Everyone under them.
///
/// <para>The Rolodex the wrist last gathered is shown at once, and a fresh one is gathered from the phone on the pool,
/// a page at a time, each time the screen opens; "Opening your contacts." until the first. It is held in memory for the
/// session only (<see cref="RolodexBook"/>); a screen rebuilt keeps only the search. A contact picked opens the reply
/// screen on a new text to them, by their id; when it went, this screen goes too and People waiting is where the owner
/// was. The list grows as the crown scrolls, so a large book draws a little at a time.</para>
/// </summary>
[Activity(Name = WatchIdentity.DashRolodexActivityName, Exported = false)]
public sealed class RolodexActivity : Activity
{
    private const int DictationRequest = 1;
    private const int KeyboardRequest = 2;
    private const int TextRequest = 3;

    // Contacts drawn at a time; more are drawn as the scroll nears the last.
    private const int RowsAtATime = 40;

    private Round round = null!;
    private Frame frame = null!;
    private WatchTextView searched = null!;
    private WatchTextView everyone = null!;
    private LinearLayout rows = null!;
    private Rolodex? book;
    private IReadOnlyList<DashContact> found = [];
    private int drawnRows;
    private string query = string.Empty;
    private bool fetching;

    public static Intent For(Context context) => new(context, typeof(RolodexActivity));

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        query = savedInstanceState?.GetString(DashExtras.Query) ?? string.Empty;

        round = new Round(this);
        var target = (int)Math.Ceiling(WatchPaints.TouchTarget(this));
        frame = Screens.Build(this, round, round.Px(DashLayout.TitleCentre / 2), round.Px(2 - DashLayout.SecondaryCentre) + target);

        var heading = new WatchTextView(this) { Type = WatchType.Glance, Weight = WatchWeight.SemiBold, Label = DashWords.NewText };
        frame.Column.AddView(heading, Wide());
        searched = new WatchTextView(this) { Body = string.Empty, Visibility = ViewStates.Gone };
        frame.Column.AddView(searched, Wide());
        everyone = Screens.Choice(this, DashWords.Everyone);
        everyone.Visibility = ViewStates.Gone;
        everyone.Click += (_, _) => Search(string.Empty);
        frame.Column.AddView(everyone, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, target) { Gravity = GravityFlags.CenterHorizontal });
        frame.Column.AddView(frame.Status, Wide());

        rows = new LinearLayout(this) { Orientation = Orientation.Vertical };
        frame.Column.AddView(rows, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = round.Px(DashLayout.BubbleGap),
        });
        frame.Scroll.ScrollChange += (_, _) => GrowNearTheEnd();

        Choose(Screens.Choice(this, DashWords.SayAName), -DashLayout.SecondaryOffset).Click += (_, _) => Dictate();
        Choose(Screens.Choice(this, DashWords.Keyboard), DashLayout.SecondaryOffset).Click += (_, _) => Type();

        if (RolodexBook.Held.Current is { } held)
        {
            Draw(held);
        }
        Fetch();
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        // The owner's own search, and never a contact: the Rolodex is held in memory, not handed to the system to keep.
        outState.PutString(DashExtras.Query, query);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == TextRequest)
        {
            if (resultCode == Result.Ok)
            {
                // It went: success is silent, and the owner is back where they started (WD §6).
                Finish();
            }
            return;
        }

        var given = resultCode != Result.Ok ? null
            : requestCode == DictationRequest ? SystemInput.Dictated(data)
            : requestCode == KeyboardRequest ? SystemInput.Typed(data)
            : null;
        if (!string.IsNullOrWhiteSpace(given))
        {
            Search(given.Trim());
        }
    }

    private void Fetch()
    {
        if (fetching)
        {
            return;
        }
        fetching = true;
        if (book is null)
        {
            Screens.Say(frame.Status, DashWords.OpeningContacts);
        }
        var context = ApplicationContext!;
        Screens.WhenFetched(this, Task.Run(() => RolodexCarrier.FetchAsync(context)), Fetched);
    }

    private void Fetched(RolodexFetch fetch)
    {
        fetching = false;
        if (fetch.Whole is { } whole)
        {
            RolodexBook.Held.Hold(whole);
            Draw(whole);
            return;
        }
        // The contacts from the last time stay; the quiet line says why they could not be asked for again.
        Screens.Say(frame.Status, DashWords.For(fetch.Trouble ?? DashTrouble.Unreadable));
    }

    private void Search(string words)
    {
        query = words;
        if (book is not null)
        {
            Draw(book);
        }
    }

    private void Draw(Rolodex shown)
    {
        book = shown;
        found = shown.Find(query);
        var searching = query.Length > 0 ? ViewStates.Visible : ViewStates.Gone;
        searched.Body = query;
        searched.Visibility = searching;
        everyone.Visibility = searching;
        Screens.Say(frame.Status, shown.Contacts.Count == 0 ? DashWords.NoContacts : found.Count == 0 ? DashWords.NoOneByThatName : null);

        rows.RemoveAllViews();
        drawnRows = 0;
        Grow();
        frame.Scroll.ScrollTo(0, 0);
        frame.Scroll.RequestFocus();
    }

    // The next contacts, as many as are drawn at a time.
    private void Grow()
    {
        foreach (var contact in found.Skip(drawnRows).Take(RowsAtATime))
        {
            var view = new ContactRowView(this);
            view.Show(contact, round.Radius);
            view.Click += (_, _) => StartActivityForResult(ReplyActivity.For(this, contact.Id, contact.Name), TextRequest);
            rows.AddView(view, new LinearLayout.LayoutParams(round.Px(DashLayout.RowWidth), ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.CenterHorizontal,
                TopMargin = rows.ChildCount == 0 ? 0 : round.Px(DashLayout.RowPitch - DashLayout.RowHeight),
            });
            drawnRows++;
        }
    }

    // Within a screen's height of the last drawn contact, the next are drawn.
    private void GrowNearTheEnd()
    {
        if (drawnRows < found.Count && frame.Scroll.ScrollY + frame.Scroll.Height * 2 >= frame.Column.Height)
        {
            Grow();
        }
    }

    private void Dictate()
    {
        if (SystemInput.Dictation(this, DashWords.WhoTo) is { } dictation)
        {
            StartActivityForResult(dictation, DictationRequest);
            return;
        }
        DashLog.Warn("DASH this watch has no dictation; the keyboard instead");
        Type();
    }

    private void Type()
    {
        if (SystemInput.Keyboard(this, DashWords.WhoTo) is { } keyboard)
        {
            StartActivityForResult(keyboard, KeyboardRequest);
            return;
        }
        DashLog.Warn("DASH this watch has neither dictation nor a keyboard to open");
        Screens.Say(frame.Status, DashWords.For(DashTrouble.NoInput));
    }

    // A quieter choice either side of the centre at the foot, as Replying by voice draws Keyboard and Again.
    private WatchTextView Choose(WatchTextView choice, double offset)
    {
        var target = (int)Math.Ceiling(WatchPaints.TouchTarget(this));
        frame.Root.AddView(choice, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, target, GravityFlags.Top | GravityFlags.CenterHorizontal)
        {
            TopMargin = round.Px(DashLayout.SecondaryCentre) - target / 2,
            LeftMargin = offset < 0 ? 0 : round.Px(offset * 2),
            RightMargin = offset < 0 ? round.Px(-offset * 2) : 0,
        });
        return choice;
    }

    private LinearLayout.LayoutParams Wide() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = round.Px(DashLayout.RowLeft),
            RightMargin = round.Px(DashLayout.RowLeft),
        };
}
