using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Policy;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Ui;

namespace Post;

/// <summary>
/// One email, opened as its own page — the design's answer to "email in a thread is a
/// subject and a snippet; tap to open it".
///
/// The body renders through the <b>shared</b> hardened web view rather than a second
/// WebView of Post's own, so the browser configuration exists once and is hardened once.
/// The posture is <see cref="WebPosture.Reading"/>: no script, and no link navigates the
/// reader — every one leaves for Flyleaf's restricted browser, where the same origin
/// containment meets it as any other link. A message can never quietly become a page.
///
/// Plain-text mail is shown as text; there is nothing to render and a web view would be
/// dressing up a paragraph.
/// </summary>
[Activity(
    Label = "Post",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class MailReaderActivity : Activity
{
    public const string ExtraKind = "kind";
    public const string ExtraSourceId = "sourceId";

    private FrameLayout? host;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TapWatch.Attach(this);
        PaperTransitions.Apply(this);

        var item = Load();
        if (item is null)
        {
            Finish();
            return;
        }

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var from = PaperUi.Secondary(this, ContactNameResolver.DisplayName(this, item.ContactKey));
        from.SetPadding(0, 0, 0, PaperTheme.Dp(this, DesignTokens.Space.X3));
        page.AddView(from);

        host = new FrameLayout(this);
        page.AddView(host, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // BinderFrame applies its own top-inset padding to the page it draws (see its own
        // remarks for why), so this screen no longer wraps its whole root in WrapForTopInset
        // itself — doing both would pad the subject header down twice.
        root.AddView(BinderFrame.Wrap(
            this, item.Subject ?? "(no subject)", PaperTheme.PostMark, page,
            PostShellAreas.All, PostShellAreas.Mailbox, SwitchArea),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        var buttons = new List<View>();
        if (item.Attachments.Count > 0)
        {
            buttons.Add(PaperUi.SecondaryButton(
                this, Attachments.Label(item.Attachments), () => Attachments.Offer(this, host, item)));
        }
        buttons.Add(PaperUi.SecondaryButton(this, "File", () => Hand(item, ReaderAction.File)));
        buttons.Add(PaperUi.DestructiveButton(this, "Delete", () => Hand(item, ReaderAction.Delete)));
        root.AddView(PaperUi.BottomBar(this, [.. buttons]));

        SetContentView(root);
        // 0, not edge: BinderFrame's own contentHost already pads the horizontal edge, and
        // padding it again here would double it.
        Render(item, 0);
    }

    // Names the tab PostActivity should land on once this screen finishes (see
    // PostActivity.PendingArea); ReorderToFront brings its existing instance forward rather
    // than creating a second one, or starts it fresh if it isn't running at all.
    // File or delete is the Mailbox's to carry out (it owns the undo window), so the reader
    // names the message and the act, then gets out of the way.
    private void Hand(TimelineItem item, ReaderAction action)
    {
        PostActivity.PendingReaderAction = (item.SourceId, action);
        PostActivity.PendingArea = PostShellAreas.Mailbox;
        var intent = new Intent(this, typeof(PostActivity));
        intent.AddFlags(ActivityFlags.ReorderToFront);
        StartActivity(intent);
        Finish();
    }

    private void SwitchArea(int index)
    {
        PostActivity.PendingArea = index;
        var intent = new Intent(this, typeof(PostActivity));
        intent.AddFlags(ActivityFlags.ReorderToFront);
        StartActivity(intent);
        Finish();
    }

    private void Render(TimelineItem item, int edge)
    {
        // Plain-text mail is shown as text. A web view around a paragraph is dressing up a
        // paragraph, and it is one more engine running for no reason.
        if (string.IsNullOrWhiteSpace(item.HtmlBody))
        {
            var scroll = new ScrollView(this);
            var body = PaperUi.Body(this, item.Body ?? string.Empty);
            body.SetPadding(edge, 0, edge, PaperTheme.Dp(this, DesignTokens.Space.X6));
            scroll.AddView(body);
            host!.AddView(scroll);
            return;
        }

        try
        {
            var web = new WebView(this);
            PaperWebView.LoadMail(web, item.HtmlBody!, OpenInRestrictedBrowser);
            host!.AddView(web);
            FieldNotesStore.Record(this, notes => notes.WithHtmlRender(rendered: true));
        }
        catch (Exception e)
        {
            // A device without a usable WebView still has to show the message: the readable
            // text was extracted at map time precisely so this fallback is not a blank page.
            Android.Util.Log.Warn("Post", $"Could not render HTML mail: {e}");
            PostDiagnostics.LogWarning("MailReader", "Could not render HTML mail; showing text", e);
            FieldNotesStore.Record(this, notes => notes.WithHtmlRender(rendered: false));

            var scroll = new ScrollView(this);
            var body = PaperUi.Body(this, item.Body ?? string.Empty);
            body.SetPadding(edge, 0, edge, PaperTheme.Dp(this, DesignTokens.Space.X6));
            scroll.AddView(body);
            host!.AddView(scroll);
        }
    }

    private void OpenInRestrictedBrowser(string url) => LinkOpener.Open(this, url);

    private TimelineItem? Load()
    {
        var sourceId = Intent?.GetStringExtra(ExtraSourceId);
        if (string.IsNullOrEmpty(sourceId))
        {
            return null;
        }

        try
        {
            using var database = TimelineDatabase.Open(this);
            return database.Cache.LoadRecent(limit: 2_000)
                .FirstOrDefault(candidate => candidate.SourceId == sourceId);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning("MailReader", "Could not read the message", e);
            return null;
        }
    }
}
