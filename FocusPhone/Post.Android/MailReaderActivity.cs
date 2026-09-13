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
        PaperTransitions.Apply(this);

        var item = Load();
        if (item is null)
        {
            Finish();
            return;
        }

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        var header = PaperUi.ScreenHeader(this, item.Subject ?? "(no subject)", PaperTheme.PostMark);
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        root.AddView(header);

        var from = PaperUi.Secondary(this, ContactNameResolver.DisplayName(this, item.ContactKey));
        from.SetPadding(edge, 0, edge, PaperTheme.Dp(this, DesignTokens.Space.X3));
        root.AddView(from);

        host = new FrameLayout(this);
        root.AddView(host, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        if (item.Attachments.Count > 0)
        {
            root.AddView(PaperUi.BottomBar(this, PaperUi.SecondaryButton(
                this, Attachments.Label(item.Attachments), () => Attachments.Offer(this, host, item))));
        }

        SetContentView(root);
        Render(item, edge);
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

    // Every link in an email leaves. Flyleaf's restricted browser is where the suite's
    // origin containment lives, so a link from a stranger meets the same rules as one the
    // owner typed. Targeted explicitly at RestrictedBrowserActivity rather than left to an
    // implicit VIEW intent — the device-owner persistent-preferred-activity pin that would
    // otherwise route it there can be outranked by a host's verified Android App Links,
    // which is how a link was observed opening in full Chrome instead.
    private void OpenInRestrictedBrowser(string url)
    {
        var uri = Android.Net.Uri.Parse(url);
        var intent = new Intent(Intent.ActionView, uri);
        intent.SetComponent(new ComponentName(RestrictedBrowserHandoff.Package, RestrictedBrowserHandoff.Activity));
        try
        {
            StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            // Flyleaf isn't installed under the expected id on this device — fall back to
            // whatever the platform resolves rather than dropping the link entirely.
            try
            {
                StartActivity(new Intent(Intent.ActionView, uri));
            }
            catch (Exception e)
            {
                PostDiagnostics.LogWarning("MailReader", $"Could not open a link from mail: {url}", e);
            }
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning("MailReader", $"Could not open a link from mail: {url}", e);
        }
    }

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
