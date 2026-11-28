using Android;
using Android.App;
using Android.App.Roles;
using Android.Content;
using Android.Content.PM;
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
/// again. Mail, at the foot where the mockup draws it, opens the mail lines (W-5.1.6, B7; <see cref="MailActivity"/>), and
/// New text under it the Rolodex (W-5.1.2, B6; <see cref="RolodexActivity"/>). Coming back after a call the watch's own
/// Phone app carried, it offers the call note once (W-5.1.5, B7; <see cref="AfterCallActivity"/>).
/// </summary>
[Activity(Name = WatchIdentity.DashPeopleActivityName, MainLauncher = true, Exported = true, Icon = "@mipmap/ic_launcher")]
public sealed class PeopleActivity : Activity
{
    private Round round = null!;
    private Frame frame = null!;
    private DashGlyphView glyph = null!;
    private const int ActRequest = 1;
    private const int PhoneStateRequest = 2;
    private const int ScreeningRequest = 3;

    private LinearLayout rows = null!;
    private WatchTextView newText = null!;
    private bool drawn;
    private IReadOnlyList<PersonRow> shown = [];
    private bool askingCall;

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

        // Mail (W-5.1.6, B7) at the foot, where the mockup draws it, and a new text (W-5.1.2, B6) under it, which no
        // mockup draws; both there with nobody waiting too, each a quieter word that is a whole touch target.
        var target = (int)Math.Ceiling(WatchPaints.TouchTarget(this));
        var mail = Screens.Choice(this, DashWords.Mail);
        mail.Weight = WatchWeight.Medium;
        mail.Click += (_, _) => StartActivity(MailActivity.For(this));
        frame.Column.AddView(mail, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, target)
        {
            Gravity = GravityFlags.CenterHorizontal,
            TopMargin = round.Px(DashLayout.EntryGap),
        });
        newText = Screens.Choice(this, DashWords.NewText);
        newText.Weight = WatchWeight.Medium;
        newText.Click += (_, _) => StartActivity(RolodexActivity.For(this));
        frame.Column.AddView(newText, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, target)
        {
            Gravity = GravityFlags.CenterHorizontal,
        });

        if (savedInstanceState is null)
        {
            HearCalls();
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        Ask();
        OfferTheNote();
    }

    // The one permission Dash asks for (B7): without it the platform never says the watch's call state went idle, and no
    // note is offered. The system's own prompt; refused, Dash works as before, and Android stops asking after twice. Then
    // the call screening role (B8), which the platform asks for in its own prompt in turn.
    private void HearCalls()
    {
        if (CheckSelfPermission(Manifest.Permission.ReadPhoneState) != Permission.Granted)
        {
            RequestPermissions([Manifest.Permission.ReadPhoneState], PhoneStateRequest);
            return;
        }
        ScreenCalls();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == PhoneStateRequest)
        {
            ScreenCalls();
        }
    }

    // W-5.1.9 (A12): unknown numbers ring only if they call twice within minutes, which the watch decides in the role's
    // service (CallScreen). The platform's own prompt; after a refusal it offers "don't ask again". Refused, or a watch
    // that offers no such role, unknown numbers ring as they always did.
    private void ScreenCalls()
    {
        if (GetSystemService(RoleService) is RoleManager roles && roles.IsRoleAvailable(RoleManager.RoleCallScreening) && !roles.IsRoleHeld(RoleManager.RoleCallScreening))
        {
            StartActivityForResult(roles.CreateRequestRoleIntent(RoleManager.RoleCallScreening), ScreeningRequest);
        }
    }

    // After a call the watch's Phone app carried (W-5.1.5): Post says which call ended then, on the pool, and After the call
    // is offered once, for a call Post named. One Post has no record of is let go; one the phone could not be asked about
    // is asked about again when People waiting comes back, within the offer's half hour.
    private void OfferTheNote()
    {
        if (askingCall || AfterCall.Shared.Due(DateTimeOffset.UtcNow) is not { } end)
        {
            return;
        }
        askingCall = true;
        var context = ApplicationContext!;
        Screens.WhenAnswered(this, Task.Run(() => PostLink.Shared.AskAsync(context, new DashCallAsk(end.Ended))), reply => Offer(end, reply));
    }

    private void Offer(CallEnd end, DashReply reply)
    {
        askingCall = false;
        if (reply.Answer is not DashCall call)
        {
            return;
        }
        AfterCall.Shared.Settle(end);
        if (call.Call is not null)
        {
            StartActivity(AfterCallActivity.For(this, call, end));
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == ActRequest && resultCode == Result.Ok && ActActivity.Left(data) is { } gone)
        {
            // Filed or deleted: the row leaves now (WD §9), and the phone's next answer agrees; so does the tile.
            Draw([.. shown.Where(row => row.Key != gone)]);
            PeopleTileService.Refresh(this);
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
            // The tile shows the same people (W-5.1.7): it is asked to be drawn again, and asks the phone itself.
            PeopleTileService.Refresh(this);
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
