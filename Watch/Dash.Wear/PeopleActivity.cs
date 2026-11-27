using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Ui.Wear;

[assembly: Application(Label = "Dash", Icon = "@mipmap/ic_launcher", AllowBackup = false, Theme = "@android:style/Theme.DeviceDefault")] // not copy: the app's name, which only the watch's settings and app list show (A48)

namespace Dash.Wear;

/// <summary>
/// People waiting, Dash's first screen (W-5.1.1, A14; mock: People waiting): the time and Dash's glyph at the top, then
/// Post's waiting correspondences, newest first, each a name and one line. Asked of the phone each time the screen
/// comes back, and said as a status sentence while it is on its way; nobody waiting is the faint glyph and nothing
/// else (mock: nobody waiting). A tap opens the conversation.
/// </summary>
[Activity(Name = WatchIdentity.DashPeopleActivityName, MainLauncher = true, Exported = true, Icon = "@mipmap/ic_launcher")]
public sealed class PeopleActivity : Activity
{
    private Round round = null!;
    private Frame frame = null!;
    private DashGlyphView glyph = null!;
    private LinearLayout rows = null!;
    private bool drawn;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        round = new Round(this);
        frame = Screens.Build(this, round, round.Px(DashLayout.GlyphTop), round.Px(DashLayout.RowPitch * 2));

        glyph = new DashGlyphView(this);
        frame.Column.AddView(glyph, new LinearLayout.LayoutParams(round.Px(DashLayout.GlyphSide), round.Px(DashLayout.GlyphSide)) { Gravity = GravityFlags.CenterHorizontal });
        frame.Column.AddView(frame.Status, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        rows = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var firstTop = DashLayout.FirstRow - DashLayout.RowHeight / 2 - DashLayout.GlyphTop - DashLayout.GlyphSide;
        frame.Column.AddView(rows, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = round.Px(firstTop) });
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
            Screens.Say(frame.Status, DashWords.Finding);
        }
        var context = ApplicationContext!;
        Screens.WhenAnswered(this, Task.Run(() => PostLink.Shared.AskAsync(context, new DashPeopleAsk())), Show);
    }

    private void Show(DashReply reply)
    {
        if (reply.Answer is DashPeople people)
        {
            Draw(PeopleWaiting.Rows(people, DateTimeOffset.UtcNow, TimeZoneInfo.Local));
            Screens.Say(frame.Status, null);
            return;
        }
        // The rows from the last answer stay; the quiet line says why they could not be asked for again.
        Screens.Say(frame.Status, DashWords.For(reply.Trouble ?? DashTrouble.Unreadable));
    }

    private void Draw(IReadOnlyList<PersonRow> people)
    {
        drawn = true;
        glyph.Quiet = people.Count == 0;
        rows.RemoveAllViews();
        for (var index = 0; index < people.Count; index++)
        {
            var person = people[index];
            var view = new PersonRowView(this);
            view.Show(person, round.Radius);
            view.Click += (_, _) => StartActivity(ConversationActivity.For(this, person.Key, person.Name, person.Group));
            rows.AddView(view, new LinearLayout.LayoutParams(round.Px(DashLayout.RowWidth), ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.CenterHorizontal,
                TopMargin = index == 0 ? 0 : round.Px(DashLayout.RowPitch - DashLayout.RowHeight),
            });
        }
        frame.Scroll.RequestFocus();
    }
}
