using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Codex.Domain.Design;
using Codex.Domain.Post;
using Codex.Domain.Web;
using Codex.Ui;
using Storyworthtelling.Diagnostics;

namespace Post;

/// <summary>
/// Pairs Post with Google Messages on this phone, as a Messages-for-Web client.
///
/// <para><b>Why the sign-in is a page inside Post.</b> Messages for Web pairs a Google account
/// by its messages.google.com sign-in cookies (<see cref="MessagesSignIn"/>). Chrome, even in
/// Flyleaf's Admin mode, keeps its cookies to itself, so the sign-in runs in the suite's one
/// hardened web view, here, and Post reads the cookies the moment the page lands on Messages.
/// The page may go only where Google's sign-in goes.</para>
///
/// <para><b>The emoji.</b> Google then asks Google Messages to confirm: Messages shows a choice
/// of emoji and the owner picks the one this screen shows. Messages asks through a
/// notification, which the lockdown's disabled status bar hides, so Post's notification
/// listener keeps that notification's tap for this screen (<see cref="PairingPrompt"/>) and
/// "Confirm in Google Messages" opens it. Messages runs under a Flyleaf block (a block only
/// keeps it off the shelf), so it opens from here.</para>
///
/// <para>One status sentence carries the flow (§6: no spinners); success is the sentence
/// changing (§5: no success toast).</para>
/// </summary>
[Activity(
    Label = "Post",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class MessagesPairingActivity : Activity
{
    private enum Step
    {
        SigningIn,
        Starting,
        PickEmoji,
        Paired,
        Failed,
    }

    private Step step = Step.SigningIn;
    private string status = MessagesPairing.SigningIn;
    private string? emoji;
    private PairingFailure? failure;
    private string? cookiesJson;

    private TextView statusView = null!;
    private TextView emojiView = null!;
    private WebView? web;
    private FrameLayout webHost = null!;
    private LinearLayout barSlot = null!;
    private KeyAction? pairing;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TapWatch.Attach(this);
        PaperTransitions.Apply(this);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };

        statusView = PaperUi.Body(this, status);
        statusView.SetPadding(0, 0, 0, PaperTheme.Dp(this, DesignTokens.Space.X3));
        page.AddView(statusView);

        emojiView = PaperUi.Display(this, string.Empty);
        emojiView.Gravity = GravityFlags.CenterHorizontal;
        emojiView.Visibility = ViewStates.Gone;
        page.AddView(emojiView);

        webHost = new FrameLayout(this);
        page.AddView(webHost, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        root.AddView(BinderFrame.Wrap(
            this, "Google Messages", PaperTheme.PostMark, page,
            PostShellAreas.All, PostShellAreas.Settings, SwitchArea),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        barSlot = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.AddView(barSlot);
        SetContentView(root);

        GmBridgeClient.Shared(this).PairingChanged += OnPairingChanged;
        BeginSignIn();
    }

    protected override void OnDestroy()
    {
        GmBridgeClient.Shared(this).PairingChanged -= OnPairingChanged;
        if (step is Step.Starting or Step.PickEmoji)
        {
            GmBridgeClient.Shared(this).CancelPairing();
            pairing?.Failed("the pairing screen closed before Google Messages confirmed");
        }
        PairingPrompt.Clear();
        web?.Destroy();
        base.OnDestroy();
    }

    private void SwitchArea(int index)
    {
        PostActivity.PendingArea = index;
        var intent = new Intent(this, typeof(PostActivity));
        intent.AddFlags(ActivityFlags.ReorderToFront);
        StartActivity(intent);
        Finish();
    }

    // A fresh sign-in: the old cookies go, so the account picked here is the one paired.
    private void BeginSignIn()
    {
        pairing = TapWatch.Action(PostKeyActions.PairMessages, TimeSpan.FromMinutes(20));
        cookiesJson = null;
        Show(Step.SigningIn, MessagesPairing.SigningIn);
        CookieManager.Instance?.RemoveAllCookies(null);

        web?.Destroy();
        web = new WebView(this);
        // Browsing posture (script on — Google's sign-in is script) with this screen's own
        // containment: the client below lets the page go only where the sign-in goes.
        PaperWebView.Harden(web, WebPosture.Browsing, _ => { });
        web.SetWebViewClient(new SignInClient(this));
        CookieManager.Instance?.SetAcceptCookie(true);
        // Google's sign-in sets its cookies across google.com and youtube.com in one flow.
        CookieManager.Instance?.SetAcceptThirdPartyCookies(web, true);
        webHost.RemoveAllViews();
        webHost.AddView(web);
        web.LoadUrl(MessagesSignIn.StartUrl.AbsoluteUri);
    }

    // Called by the web client when the page lands on Messages.
    private void OnSignedIn()
    {
        if (step != Step.SigningIn)
        {
            return;
        }
        CookieManager.Instance?.Flush();
        var cookies = MessagesSignIn.Read(CookieManager.Instance?.GetCookie(MessagesSignIn.CookieUrl));
        if (!cookies.IsComplete)
        {
            // Names only: the values are the account's sign-in.
            PostDiagnostics.LogWarning("Bridge", "Messages sign-in came back without " + string.Join(", ", cookies.Missing));
            pairing?.Failed("the sign-in did not leave the pairing cookies");
            Show(Step.Failed, MessagesPairing.SignInIncomplete(cookies.Missing));
            return;
        }
        cookiesJson = cookies.ToJson();
        StartPairing();
    }

    private void StartPairing()
    {
        if (cookiesJson is not { } json)
        {
            BeginSignIn();
            return;
        }
        webHost.RemoveAllViews();
        Show(Step.Starting, MessagesPairing.Starting);
        var bridge = GmBridgeClient.Shared(this);
        Task.Run(() =>
        {
            try
            {
                var picked = bridge.StartPairing(json);
                RunOnUiThread(() =>
                {
                    emoji = picked;
                    Show(Step.PickEmoji, MessagesPairing.PickEmoji(picked));
                });
            }
            catch (Exception e)
            {
                PostDiagnostics.LogWarning("Bridge", "Pairing with Google Messages would not start", e);
                var reason = ReasonOf(e.Message);
                RunOnUiThread(() => Fail(reason));
            }
        });
    }

    // The bridge prefixes a start failure with its reason code (bridge.go's pairingReason).
    private static PairingFailure ReasonOf(string? message) =>
        BridgeEvents.Read($$"""{"type":"pairFailed","reason":"{{(message ?? string.Empty).Split(':')[0]}}"}""")
            is BridgePairFailed failed ? failed.Reason : PairingFailure.Other;

    private void OnPairingChanged(BridgeEvent outcome) => RunOnUiThread(() =>
    {
        switch (outcome)
        {
            case BridgePaired:
                PairingPrompt.Clear();
                pairing?.Succeeded();
                pairing = null;
                Show(Step.Paired, MessagesPairing.Paired);
                break;
            case BridgePairFailed failed:
                Fail(failed.Reason);
                break;
        }
    });

    private void Fail(PairingFailure reason)
    {
        PairingPrompt.Clear();
        failure = reason;
        pairing?.Failed($"pairing failed: {reason}");
        Show(Step.Failed, MessagesPairing.Describe(reason));
    }

    private void Show(Step next, string sentence)
    {
        step = next;
        status = sentence;
        statusView.Text = sentence;
        emojiView.Text = next == Step.PickEmoji ? emoji : string.Empty;
        emojiView.Visibility = next == Step.PickEmoji ? ViewStates.Visible : ViewStates.Gone;

        var buttons = new List<View>();
        switch (next)
        {
            case Step.PickEmoji:
                buttons.Add(PaperUi.PrimaryButton(this, "Confirm in Google Messages", OpenConfirmation));
                buttons.Add(PaperUi.QuietButton(this, "Cancel", Finish));
                break;
            case Step.Paired:
                buttons.Add(PaperUi.PrimaryButton(this, "Done", Finish));
                break;
            case Step.Failed:
                if (failure is { } reason && MessagesPairing.WorthRetrying(reason) && cookiesJson is not null)
                {
                    buttons.Add(PaperUi.PrimaryButton(this, "Try again", () =>
                    {
                        pairing = TapWatch.Action(PostKeyActions.PairMessages, TimeSpan.FromMinutes(20));
                        StartPairing();
                    }));
                }
                buttons.Add(PaperUi.SecondaryButton(this, "Sign in again", BeginSignIn));
                buttons.Add(PaperUi.QuietButton(this, "Close", Finish));
                break;
            default:
                buttons.Add(PaperUi.QuietButton(this, "Close", Finish));
                break;
        }
        barSlot.RemoveAllViews();
        barSlot.AddView(PaperUi.BottomBar(this, [.. buttons]));
    }

    // Messages' own confirmation, if its notification has arrived; Messages itself otherwise,
    // which shows the same request when opened.
    private void OpenConfirmation()
    {
        if (PairingPrompt.TryOpen())
        {
            return;
        }
        var intent = PackageManager?.GetLaunchIntentForPackage(SmsRole.GoogleMessagesPackage);
        if (intent is null)
        {
            Show(Step.PickEmoji, MessagesPairing.PickEmoji(emoji ?? string.Empty)
                + " Google Messages is not installed on this phone.");
            return;
        }
        StartActivity(intent);
    }

    private sealed class SignInClient(MessagesPairingActivity owner) : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            if (request?.Url?.ToString() is not { } text || !Uri.TryCreate(text, UriKind.Absolute, out var url))
            {
                return true;
            }
            if (MessagesSignIn.AllowsNavigation(url))
            {
                return false;
            }
            // Host only — the rest of a sign-in URL can carry account details.
            PostDiagnostics.LogWarning("Bridge", "The Messages sign-in page tried to leave for " + url.Host);
            return true;
        }

        public override void OnPageFinished(WebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var page) && MessagesSignIn.HasReachedMessages(page))
            {
                owner.OnSignedIn();
            }
        }
    }
}

/// <summary>
/// Google Messages' pairing confirmation, kept for the pairing screen. Messages asks through a
/// notification, and the lockdown hides the status bar, so the notification listener hands the
/// tap here while a pairing waits.
/// </summary>
public static class PairingPrompt
{
    private static PendingIntent? pending;

    public static void Offer(PendingIntent? intent)
    {
        if (intent is not null)
        {
            pending = intent;
        }
    }

    public static bool TryOpen()
    {
        var intent = pending;
        if (intent is null)
        {
            return false;
        }
        try
        {
            intent.Send();
            return true;
        }
        catch (PendingIntent.CanceledException e)
        {
            PostDiagnostics.LogWarning("Bridge", "Google Messages' confirmation was no longer there", e);
            pending = null;
            return false;
        }
    }

    public static void Clear() => pending = null;
}
