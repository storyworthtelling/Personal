using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Ui.Wear;

[assembly: Application(Label = "Dash", Icon = "@mipmap/ic_launcher", AllowBackup = false, Theme = "@android:style/Theme.DeviceDefault.NoActionBar")] // not copy: the app's name, which only the watch's settings and app list show (A48)

namespace Dash.Wear;

/// <summary>
/// People waiting, Dash's first screen (W-5.1.1, A14; mock: People waiting): the time and Dash's glyph at the top, then
/// Post's waiting correspondences, newest first, each a name and one line. Asked of the phone each time the screen
/// comes back, and said as a status sentence while it is on its way; nobody waiting is the faint glyph and nothing
/// else (mock: nobody waiting) but New text. A tap opens the conversation; a long press offers to file or delete it,
/// undo-first (W-5.1.4, <see cref="ActActivity"/>), and a row whose act stands leaves at once, before the phone is asked
/// again. New text, at the foot where the mockup draws Mail, opens the Rolodex (W-5.1.2, B6; <see cref="RolodexActivity"/>).
/// </summary>
[Activity(Name = WatchIdentity.DashPeopleActivityName, MainLauncher = true, Exported = true, Icon = "@mipmap/ic_launcher")]
public sealed class PeopleActivity : Activity
{
    private Round round = null!;
    private Frame frame = null!;
    private DashGlyphView glyph = null!;
    private const int ActRequest = 1;

    private LinearLayout rows = null!;
    private WatchTextView newText = null!;
    private bool drawn;
    private IReadOnlyList<PersonRow> shown = [];

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

        // A new text (W-5.1.2, B6) starts at the foot, where the mockup draws Mail, and is there with nobody waiting too.
        newText = Screens.Choice(this, DashWords.NewText);
        newText.Weight = WatchWeight.Medium;
        newText.Click += (_, _) => StartActivity(RolodexActivity.For(this));
        var target = (int)Math.Ceiling(WatchPaints.TouchTarget(this));
        frame.Column.AddView(newText, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, target)
        {
            Gravity = GravityFlags.CenterHorizontal,
            TopMargin = round.Px(DashLayout.EntryGap),
        });
    }

    protected override void OnResume()
    {
        base.OnResume();
        Ask();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == ActRequest && resultCode == Result.Ok && ActActivity.Left(data) is { } gone)
        {
            // Filed or deleted: the row leaves now (WD §9), and the phone's next answer agrees.
            Draw([.. shown.Where(row => row.Key != gone)]);
        }
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
        shown = people;
        glyph.Quiet = people.Count == 0;
        rows.RemoveAllViews();
        for (var index = 0; index < people.Count; index++)
        {
            var person = people[index];
            var view = new PersonRowView(this);
            view.Show(person, round.Radius);
            view.Click += (_, _) => StartActivity(ConversationActivity.For(this, person.Key, person.Name, person.Group));
            view.LongClick += (_, _) => StartActivityForResult(ActActivity.For(this, person.Key, person.Name), ActRequest);
            rows.AddView(view, new LinearLayout.LayoutParams(round.Px(DashLayout.RowWidth), ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.CenterHorizontal,
                TopMargin = index == 0 ? 0 : round.Px(DashLayout.RowPitch - DashLayout.RowHeight),
            });
        }
        frame.Scroll.RequestFocus();
    }
}
