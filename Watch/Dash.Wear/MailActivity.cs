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
/// Mail (W-5.1.6, A11; mock: Mail): the time at the top, "Mail" where a conversation draws its name, then each mail line,
/// its subject over who and when ("Sam · 20 min"), newest first. People only: Post sends only the mail its own sorting
/// judged to be from a person (B1). Subject and sender only, so a line is read and never opened; nothing buzzes and
/// nothing is marked unread. Asked of the phone each time the screen comes back, "Finding your mail." while it is on
/// its way, "No mail waiting." when there is none, and the quiet line says why when the phone could not be asked again
/// (the lines from the last answer stay).
/// </summary>
[Activity(Name = WatchIdentity.DashMailActivityName, Exported = false)]
public sealed class MailActivity : Activity
{
    private Round round = null!;
    private Frame frame = null!;
    private LinearLayout rows = null!;
    private bool drawn;

    public static Intent For(Context context) => new(context, typeof(MailActivity));

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        round = new Round(this);
        frame = Screens.Build(this, round, round.Px(DashLayout.TitleCentre / 2), round.Px(DashLayout.RowPitch * 2));

        var heading = new WatchTextView(this) { Type = WatchType.Glance, Weight = WatchWeight.SemiBold, Label = DashWords.Mail };
        frame.Column.AddView(heading, Wide());
        frame.Column.AddView(frame.Status, Wide());

        // The first line is centred where the mockup draws it, a line's pitch below the name.
        rows = new LinearLayout(this) { Orientation = Orientation.Vertical };
        frame.Column.AddView(rows, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Math.Max(0, round.Px(DashLayout.MailFirst - DashLayout.MailPitch / 2 - DashLayout.TitleCentre) - heading.LineHeight / 2),
        });
    }

    protected override void OnResume()
    {
        base.OnResume();
        Ask();
    }

    private void Ask()
    {
        if (!drawn)
        {
            Screens.Say(frame.Status, DashWords.FindingMail);
        }
        var context = ApplicationContext!;
        Screens.WhenAnswered(this, Task.Run(() => PostLink.Shared.AskAsync(context, new DashMailAsk())), Show);
    }

    private void Show(DashReply reply)
    {
        if (reply.Answer is DashMail mail)
        {
            var lines = MailLines.Rows(mail, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            Draw(lines);
            Screens.Say(frame.Status, lines.Count == 0 ? DashWords.NoMail : null);
            return;
        }
        Screens.Say(frame.Status, DashWords.For(reply.Trouble ?? DashTrouble.Unreadable));
    }

    private void Draw(IReadOnlyList<MailRow> lines)
    {
        drawn = true;
        rows.RemoveAllViews();
        foreach (var line in lines)
        {
            var view = new MailRowView(this);
            view.Show(line, round.Radius);
            rows.AddView(view, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }
        frame.Scroll.RequestFocus();
    }

    private LinearLayout.LayoutParams Wide() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = round.Px(DashLayout.RowLeft),
            RightMargin = round.Px(DashLayout.RowLeft),
        };
}
