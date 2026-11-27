using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Domain.Design;
using Watch.Domain.Kit;
using Watch.Ui.Wear;

namespace Dash.Wear;

/// <summary>
/// One conversation (W-5.1.1, A10, A14; mock: A conversation): its name at the top, a group's members under it, the
/// last messages oldest first (the owner's on the right in Dash's quieter glass, everyone else's on the card, a group's
/// senders named), and Reply, the one action, pinned at the foot. Once drawn it is read on the phone too, through the
/// newest message the wrist drew. A reply sent from here shows at once and gives way to Post's copy when it comes.
///
/// <para>A photo (W-5.1.3; mock: A group text with a photo) is drawn in the mockup's picture with who sent it and when
/// under it, and asked of the phone on its own, one at a time, newest first; the photos come back held for the process
/// (<see cref="PhotoBook"/>), so a conversation opened again shows them at once. A group's Reply to all sits where the
/// mockup draws it, under the caption.</para>
/// </summary>
[Activity(Name = WatchIdentity.DashConversationActivityName, Exported = false)]
public sealed class ConversationActivity : Activity
{
    private const int ReplyRequest = 1;

    private Round round = null!;
    private Frame frame = null!;
    private WatchTextView heading = null!;
    private WatchTextView members = null!;
    private LinearLayout bubbles = null!;
    private WatchPillView reply = null!;
    private string key = string.Empty;
    private string title = string.Empty;
    private bool group;
    private DashThread? thread;
    private DateTimeOffset? toldThrough;
    private IReadOnlyList<Bubble> drawn = [];
    private readonly Dictionary<string, PhotoView> photos = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DashTrouble> unfetched = new(StringComparer.Ordinal);
    private bool fetching;

    public static Intent For(Context context, string key, string title, bool group) =>
        new Intent(context, typeof(ConversationActivity))
            .PutExtra(DashExtras.Key, key)
            .PutExtra(DashExtras.Title, title)
            .PutExtra(DashExtras.Group, group);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        key = Intent?.GetStringExtra(DashExtras.Key) ?? string.Empty;
        title = Intent?.GetStringExtra(DashExtras.Title) ?? string.Empty;
        group = Intent?.GetBooleanExtra(DashExtras.Group, false) ?? false;
        if (!DashIds.IsCorrespondence(key))
        {
            DashLog.Warn("DASH a conversation was opened with no correspondence");
            key = string.Empty;
            Finish();
            return;
        }

        round = new Round(this);
        var pill = (int)Math.Ceiling(WatchPaints.TouchTarget(this));
        var replyCentre = group ? DashLayout.ReplyToAllCentre : DashLayout.ReplyCentre;
        frame = Screens.Build(this, round, round.Px(DashLayout.TitleCentre / 2), round.Px(2 - replyCentre) + pill);

        heading = new WatchTextView(this) { Type = WatchType.Glance, Weight = WatchWeight.SemiBold, Label = title };
        frame.Column.AddView(heading, Wide());
        members = new WatchTextView(this) { Type = WatchType.Small, Tone = WatchTone.Soft, Visibility = ViewStates.Gone };
        frame.Column.AddView(members, Wide());
        frame.Column.AddView(frame.Status, Wide());

        bubbles = new LinearLayout(this) { Orientation = Orientation.Vertical };
        frame.Column.AddView(bubbles, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = round.Px(DashLayout.BubblesTop - DashLayout.TitleCentre),
        });

        var label = group ? DashWords.ReplyToAll : DashWords.Reply;
        reply = Screens.Pin(this, frame, round, label, group ? DashLayout.ReplyToAllWidth : DashLayout.ReplyWidth, replyCentre);
        reply.Click += (_, _) => StartActivityForResult(ReplyActivity.For(this, key, title), ReplyRequest);
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (key.Length > 0)
        {
            Ask();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != ReplyRequest || resultCode != Result.Ok || ReplyActivity.SentFrom(data) is not { } sent)
        {
            return;
        }
        // Sent: the conversation simply has it now (WD §6), until Post's own copy comes in a later answer.
        SentHereBook.Add(key, sent);
        if (thread is not null)
        {
            Draw(thread);
        }
    }

    private void Ask()
    {
        if (thread is null)
        {
            Screens.Say(frame.Status, DashWords.Opening);
        }
        var context = ApplicationContext!;
        var ask = new DashThreadAsk(key);
        Screens.WhenAnswered(this, Task.Run(() => PostLink.Shared.AskAsync(context, ask)), Show);
    }

    private void Show(DashReply answer)
    {
        if (answer.Answer is not DashThread shown)
        {
            Screens.Say(frame.Status, DashWords.For(answer.Trouble ?? DashTrouble.Unreadable));
            return;
        }

        thread = shown;
        // A fresh answer is a fresh chance for a photo that did not come last time.
        unfetched.Clear();
        SentHereBook.Keep(key, Conversation.StillOwed(shown, SentHereBook.For(key)));
        Screens.Say(frame.Status, null);
        Draw(shown);
        Read(shown);
    }

    private void Draw(DashThread shown)
    {
        heading.Label = shown.Title;
        if (shown.Group && shown.Members.Count > 0)
        {
            members.Label = string.Join(", ", shown.Members);
            members.Visibility = ViewStates.Visible;
        }
        reply.Label = Conversation.ReplyLabel(shown);

        bubbles.RemoveAllViews();
        photos.Clear();
        drawn = Conversation.Bubbles(shown, SentHereBook.For(key));
        foreach (var bubble in drawn)
        {
            if (bubble.Photos.Count > 0)
            {
                // The picture names who sent it and when under it (mock), so its words, if any, need no name over them.
                Pictures(bubble);
                if (bubble.Said)
                {
                    bubbles.AddView(Words(bubble), Beside(bubble.Mine, round.Px(DashLayout.BubbleGap)));
                }
                continue;
            }
            if (bubble.From is { } from)
            {
                var sender = new WatchTextView(this) { Type = WatchType.Small, Tone = WatchTone.Soft, Label = from, Gravity = GravityFlags.Start };
                bubbles.AddView(sender, Beside(bubble.Mine, round.Px(DashLayout.BubbleGap)));
            }
            bubbles.AddView(Words(bubble), Beside(bubble.Mine, bubble.From is null ? round.Px(DashLayout.BubbleGap) : 0));
        }
        // The newest is by the Reply pill.
        frame.Scroll.Post(() => frame.Scroll.FullScroll(FocusSearchDirection.Down));
        frame.Scroll.RequestFocus();
        FetchPhotos();
    }

    // Each photo in the mockup's picture, centred, and under the last its caption: who sent it in a group, and when.
    private void Pictures(Bubble bubble)
    {
        var when = WhenWords.Relative(bubble.At, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        foreach (var id in bubble.Photos)
        {
            var view = new PhotoView(this);
            view.Describe(bubble.From, when, round.Radius);
            if (PhotoBook.Held.Get(id) is { } jpeg)
            {
                view.Show(jpeg);
            }
            else if (unfetched.TryGetValue(id, out var trouble))
            {
                view.Failed(trouble);
            }
            photos[id] = view;
            bubbles.AddView(view, new LinearLayout.LayoutParams(round.Px(DashLayout.PhotoWidth), round.Px(DashLayout.PhotoHeight))
            {
                Gravity = GravityFlags.CenterHorizontal,
                TopMargin = round.Px(DashLayout.BubbleGap),
            });
        }

        var caption = new WatchTextView(this) { Type = WatchType.Small, Tone = WatchTone.Soft, Label = DashWords.Caption(bubble.From, when) };
        caption.ImportantForAccessibility = ImportantForAccessibility.No;
        bubbles.AddView(caption, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Math.Max(0, round.Px(DashLayout.CaptionCentre) - caption.LineHeight / 2),
        });
    }

    // One photo at a time, newest first, so the phone fetches what the owner is looking at first; the rest follow.
    private void FetchPhotos()
    {
        if (fetching || Conversation.PhotosToAsk(drawn, id => PhotoBook.Held.Has(id) || unfetched.ContainsKey(id)).FirstOrDefault() is not { } next)
        {
            return;
        }

        fetching = true;
        var context = ApplicationContext!;
        Screens.WhenAnswered(this, Task.Run(() => PostLink.Shared.AskAsync(context, new DashPhotoAsk(next))), reply => Fetched(next, reply));
    }

    private void Fetched(string id, DashReply reply)
    {
        fetching = false;
        if (reply.Answer is DashPhoto photo)
        {
            PhotoBook.Held.Hold(id, photo.Jpeg);
            if (photos.TryGetValue(id, out var shown))
            {
                shown.Show(photo.Jpeg);
            }
            FetchPhotos();
            return;
        }

        var trouble = reply.Trouble ?? DashTrouble.Unreadable;
        unfetched[id] = trouble;
        photos.GetValueOrDefault(id)?.Failed(trouble);
        if (trouble == DashTrouble.NoPhone)
        {
            // The phone is out of reach: every photo still waiting says so, and none is asked for until the next answer.
            foreach (var waiting in Conversation.PhotosToAsk(drawn, held => PhotoBook.Held.Has(held) || unfetched.ContainsKey(held)))
            {
                unfetched[waiting] = trouble;
                photos.GetValueOrDefault(waiting)?.Failed(trouble);
            }
            return;
        }
        FetchPhotos();
    }

    // Read on the wrist is read on the phone (A14), through the newest message drawn, and told once for each newer one.
    private void Read(DashThread shown)
    {
        if (Conversation.ReadFor(shown) is not { } read || (toldThrough is { } told && read.Through <= told))
        {
            return;
        }
        toldThrough = read.Through;
        var context = ApplicationContext!;
        Screens.Told(Task.Run(() => PostLink.Shared.TellAsync(context, read)));
    }

    private WatchTextView Words(Bubble bubble)
    {
        var words = new WatchTextView(this) { Gravity = GravityFlags.Start, Body = bubble.Words };
        var corner = round.Px(DashLayout.BubbleCorner);
        words.Background = bubble.Mine
            ? WatchPaints.Rounded(WatchPaints.Glass(WatchApp.Dash, WatchPaints.Now()), corner, WatchTokens.Light.Mine)
            : WatchPaints.Rounded(WatchTokens.Neutral.Card, corner);
        var pad = round.Px(DashLayout.BubblePadding);
        words.SetPadding(pad, pad, pad, pad);
        words.SetMinHeight(round.Px(DashLayout.BubbleMinHeight));
        return words;
    }

    // A bubble on its own side: theirs from the left, the owner's from the right, each the mockup's width at most.
    private LinearLayout.LayoutParams Beside(bool mine, int top) =>
        new(round.Px(DashLayout.BubbleWidth), ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = mine ? GravityFlags.End : GravityFlags.Start,
            LeftMargin = mine ? 0 : round.Px(DashLayout.BubbleSide),
            RightMargin = mine ? round.Px(DashLayout.BubbleSide) : 0,
            TopMargin = top,
        };

    private LinearLayout.LayoutParams Wide() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = round.Px(DashLayout.RowLeft),
            RightMargin = round.Px(DashLayout.RowLeft),
        };
}
