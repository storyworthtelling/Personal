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
/// A reply (W-5.1.2, A10; mock: Replying by voice): it opens on the system's dictation; the words come back read out
/// under who they are for, with Send, the one action, and the keyboard and dictating again one tap away. Send hands the
/// words to Post, which sends them as the phone would (B1, B2); "Sending." while it does, and when it went the screen
/// simply goes and the conversation has the text. If it did not go, the quiet line says why and the words stay.
///
/// <para>A send with no answer is sent again under the same name, so Post answers it from its book and never sends a
/// text twice; once Post has answered, trying again is a new send (<see cref="SendAttempt.Again"/>).</para>
/// </summary>
[Activity(Name = WatchIdentity.DashReplyActivityName, Exported = false)]
public sealed class ReplyActivity : Activity
{
    private const int DictationRequest = 1;
    private const int KeyboardRequest = 2;

    private Round round = null!;
    private Frame frame = null!;
    private WatchTextView said = null!;
    private WatchPillView send = null!;
    private string key = string.Empty;
    private string title = string.Empty;
    private string? words;
    private SendAttempt? attempt;
    private DashSendOutcome? heard;
    private bool sending;

    public static Intent For(Context context, string key, string title) =>
        new Intent(context, typeof(ReplyActivity)).PutExtra(DashExtras.Key, key).PutExtra(DashExtras.Title, title);

    /// <summary>The reply a finished screen sent, for the conversation to show until Post's copy comes.</summary>
    public static SentHere? SentFrom(Intent? data)
    {
        var id = data?.GetStringExtra(DashExtras.Send);
        var sent = data?.GetStringExtra(DashExtras.Words);
        var at = data?.GetLongExtra(DashExtras.At, 0) ?? 0;
        return id is null || sent is null || at <= 0 ? null : new SentHere(id, sent, DateTimeOffset.FromUnixTimeMilliseconds(at));
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        key = Intent?.GetStringExtra(DashExtras.Key) ?? string.Empty;
        title = Intent?.GetStringExtra(DashExtras.Title) ?? string.Empty;
        if (!DashIds.IsCorrespondence(key))
        {
            DashLog.Warn("DASH a reply was opened with no correspondence");
            key = string.Empty;
            Finish();
            return;
        }

        round = new Round(this);
        var pill = (int)Math.Ceiling(WatchPaints.TouchTarget(this));
        frame = Screens.Build(this, round, round.Px(DashLayout.ToCentre / 2), round.Px(2 - DashLayout.SendCentre) + pill);

        var to = new WatchTextView(this) { Tone = WatchTone.Soft, Weight = WatchWeight.Medium, Label = DashWords.To(title) };
        frame.Column.AddView(to, Centred(round.Px(DashLayout.RowLeft)));
        said = new WatchTextView(this) { Body = string.Empty };
        var aside = round.Px((2 - DashLayout.WordsWidth) / 2);
        frame.Column.AddView(said, Centred(aside, round.Px(DashLayout.WordsTop - DashLayout.ToCentre) / 2));
        frame.Column.AddView(frame.Status, Centred(0));

        send = Screens.Pin(this, frame, round, DashWords.Send, DashLayout.SendWidth, DashLayout.SendCentre);
        send.Enabled = false;
        send.Click += (_, _) => Send();
        Choose(Screens.Choice(this, DashWords.Keyboard), -DashLayout.SecondaryOffset).Click += (_, _) => Type();
        Choose(Screens.Choice(this, DashWords.Again), DashLayout.SecondaryOffset).Click += (_, _) => Dictate();

        if (savedInstanceState is null)
        {
            Dictate();
            return;
        }
        Restore(savedInstanceState);
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        outState.PutString(DashExtras.Words, words);
        outState.PutString(DashExtras.Send, attempt?.Id);
        outState.PutInt(DashExtras.Heard, heard is { } outcome ? (int)outcome : -1);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        var given = resultCode != Result.Ok ? null
            : requestCode == DictationRequest ? SystemInput.Dictated(data)
            : requestCode == KeyboardRequest ? SystemInput.Typed(data)
            : null;
        if (string.IsNullOrWhiteSpace(given))
        {
            // Backed out of the first dictation: there is no reply, and the conversation is where the owner was.
            if (words is null)
            {
                Finish();
            }
            return;
        }
        Take(given);
    }

    private void Dictate()
    {
        if (SystemInput.Dictation(this, DashWords.To(title)) is { } dictation)
        {
            StartActivityForResult(dictation, DictationRequest);
            return;
        }
        DashLog.Warn("DASH this watch has no dictation; the keyboard instead");
        Type();
    }

    private void Type()
    {
        if (SystemInput.Keyboard(this, DashWords.To(title)) is { } keyboard)
        {
            StartActivityForResult(keyboard, KeyboardRequest);
            return;
        }
        DashLog.Warn("DASH this watch has neither dictation nor a keyboard to open");
        Screens.Say(frame.Status, DashWords.For(DashTrouble.NoInput));
        send.Enabled = SendAttempt.Check(words) == SendCheck.Fits;
    }

    // New words are a new reply: whatever was sent before under another name is no longer this one.
    private void Take(string given)
    {
        words = given.Trim();
        attempt = null;
        heard = null;
        Show();
    }

    private void Show()
    {
        said.Body = words ?? string.Empty;
        var check = SendAttempt.Check(words);
        Screens.Say(frame.Status, check == SendCheck.TooLong ? DashWords.TooLong : null);
        send.Enabled = check == SendCheck.Fits && !sending;
    }

    private void Send()
    {
        if (sending || words is null || SendAttempt.Check(words) != SendCheck.Fits)
        {
            return;
        }

        attempt = attempt?.Again(heard, Guid.NewGuid()) ?? SendAttempt.Reply(key, words, Guid.NewGuid());
        sending = true;
        send.Enabled = false;
        Screens.Say(frame.Status, DashWords.Sending);
        var context = ApplicationContext!;
        var message = attempt.Message;
        Screens.WhenAnswered(this, Task.Run(() => PostLink.Shared.AskAsync(context, message)), Answered);
    }

    private void Answered(DashReply reply)
    {
        sending = false;
        if (reply.Answer is DashSent sent)
        {
            heard = sent.Outcome;
            DashLog.Info(DashLogLines.Send(sent.Outcome));
            if (DashWords.For(sent.Outcome) is not { } why)
            {
                // It went: success is silent, the screen goes and the conversation has the text (WD §6).
                var result = new Intent()
                    .PutExtra(DashExtras.Send, sent.Id)
                    .PutExtra(DashExtras.Words, words)
                    .PutExtra(DashExtras.At, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                SetResult(Result.Ok, result);
                Finish();
                return;
            }
            Screens.Say(frame.Status, why);
        }
        else
        {
            // No answer, or one that would not read: the text may have gone, so the same name goes again.
            heard = null;
            Screens.Say(frame.Status, DashWords.For(reply.Trouble ?? DashTrouble.NoAnswer));
        }
        send.Enabled = true;
    }

    private void Restore(Bundle saved)
    {
        words = saved.GetString(DashExtras.Words);
        var id = saved.GetString(DashExtras.Send);
        var outcome = saved.GetInt(DashExtras.Heard, -1);
        heard = outcome >= 0 ? (DashSendOutcome)outcome : null;
        if (id is not null && words is not null)
        {
            attempt = new SendAttempt(id, key, words);
        }
        Show();
    }

    // A quieter choice either side of the centre at the mockup's height (mock: Keyboard, Again).
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

    private static LinearLayout.LayoutParams Centred(int aside, int top = 0) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = aside, RightMargin = aside, TopMargin = top };
}
