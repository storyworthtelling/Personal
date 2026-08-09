using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Audio.Google;
using FocusPhone.Domain.Audio;
using FocusPhone.Domain.Design;
using FocusPhone.Infrastructure.Podcasts;
using FocusPhone.Ui;

namespace FocusPhone.Audio;

/// <summary>
/// Manage podcast subscriptions: add by URL, import an OPML export, reorder, and
/// remove (undo-first). Backed by the ordered <see cref="SubscriptionList"/> via
/// <see cref="SubscriptionStore"/>. Editing here; the home shelf reflects the
/// changes on its next refresh.
/// </summary>
[Activity(Label = "Subscriptions", Theme = "@style/Theme.FocusPhone.Paper")]
public class ManageSubscriptionsActivity : Activity
{
    private const int OpmlRequestCode = 4021;

    private SubscriptionStore store = null!;
    private PodcastLibrarySync librarySync = null!;
    private readonly DeviceAccountTokenProvider tokens = new();
    private SubscriptionList subscriptions = new();
    private FrameLayout undoHost = null!;
    private LinearLayout rows = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        store = new SubscriptionStore(this);
        librarySync = new PodcastLibrarySync(
            tokens.GetAccessToken, "FocusPhone.Audio",
            new PodcastStore(this), store, new PlaybackProgressStore(this),
            new PlaybackSettingsStore(this), new EqualizerStore(this));
        subscriptions = new SubscriptionList(store.Load());
        SetContentView(BuildLayout());
        Render();
    }

    // Mirror subscription edits up to Drive on the way out, so a show added or removed here is backed up
    // even if the app is killed before the home screen next pauses. Best-effort and off the UI thread.
    protected override void OnPause()
    {
        base.OnPause();
        Task.Run(async () =>
        {
            try
            {
                await librarySync.BackupAsync(System.Threading.CancellationToken.None);
            }
            catch
            {
                // Best-effort: the home screen's backup catches the cloud copy up next time.
            }
        });
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        var header = PaperUi.PageHeader(this, "Subscriptions");
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        root.AddView(header);

        undoHost = new FrameLayout(this);
        var scroll = new ScrollView(this);
        rows = new LinearLayout(this) { Orientation = Orientation.Vertical };
        rows.SetPadding(edge, 0, edge, 0);
        scroll.AddView(rows);
        undoHost.AddView(scroll, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        root.AddView(undoHost, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        root.AddView(PaperUi.BottomBar(this,
            PaperUi.SecondaryButton(this, "Add feed", ShowAddFeedDialog),
            PaperUi.QuietButton(this, "Import OPML", ImportOpml)));
        return root;
    }

    private void Render()
    {
        rows.RemoveAllViews();
        var ordered = subscriptions.Ordered;
        if (ordered.Count == 0)
        {
            rows.AddView(PaperUi.EmptyState(this, "No subscriptions yet."));
            return;
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            rows.AddView(BuildRow(ordered[i], i, ordered.Count));
        }
    }

    private View BuildRow(PodcastSubscription subscription, int index, int total)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        var vertical = PaperTheme.Dp(this, DesignTokens.Space.X3);
        row.SetPadding(0, vertical, 0, vertical);

        var title = PaperUi.Body(this, subscription.Title);
        row.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        if (index > 0)
        {
            row.AddView(PaperUi.QuietButton(this, "↑", () => Move(index, index - 1)));
        }

        if (index < total - 1)
        {
            row.AddView(PaperUi.QuietButton(this, "↓", () => Move(index, index + 1)));
        }

        row.AddView(PaperUi.DestructiveButton(this, "Remove", () => RemoveWithUndo(subscription)));
        return row;
    }

    private void Move(int fromIndex, int toIndex)
    {
        subscriptions.Move(fromIndex, toIndex);
        Persist();
        Render();
    }

    // Undo-first (DESIGN.md §7): remove immediately, offer the reversal by
    // restoring the whole ordered snapshot.
    private void RemoveWithUndo(PodcastSubscription subscription)
    {
        var snapshot = subscriptions.Export();
        subscriptions.Remove(subscription.FeedUrl);
        Persist();
        Render();
        UndoBar.Show(this, undoHost, $"Removed {subscription.Title}.", onUndo: () =>
        {
            subscriptions = new SubscriptionList(snapshot);
            Persist();
            Render();
        });
    }

    private void ShowAddFeedDialog()
    {
        var input = PaperUi.MonoField(this, "RSS feed URL");
        new AlertDialog.Builder(this)
            .SetTitle("Subscribe to podcast")!
            .SetView(input)!
            .SetPositiveButton("Add", (_, _) =>
            {
                if (Uri.TryCreate(input.Text, UriKind.Absolute, out _))
                {
                    subscriptions.Add(input.Text!, input.Text!, DateTimeOffset.Now);
                    Persist();
                    ReturnToHome(); // land on Home so the new show is visibly there, not a dead-end here
                }
                else
                {
                    Toast.MakeText(this, "Not a valid URL.", ToastLength.Short)!.Show();
                }
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void ImportOpml()
    {
        var intent = new Intent(Intent.ActionGetContent);
        intent.SetType("*/*");
        StartActivityForResult(Intent.CreateChooser(intent, "Choose OPML file"), OpmlRequestCode);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != OpmlRequestCode || resultCode != Result.Ok || data?.Data is not { } uri)
        {
            return;
        }

        try
        {
            using var stream = ContentResolver!.OpenInputStream(uri);
            using var reader = new StreamReader(stream!);
            var selection = new OpmlImportSelection(OpmlImportParser.Parse(reader.ReadToEnd()), subscriptions);
            if (selection.Items.Count == 0)
            {
                Toast.MakeText(this, "No feeds found in that file.", ToastLength.Short)!.Show();
                return;
            }

            ShowImportChecklist(selection);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.Xml.XmlException)
        {
            Toast.MakeText(this, "That file couldn't be read as OPML.", ToastLength.Short)!.Show();
        }
    }

    // Confirm which feeds to add before subscribing (new ones pre-checked), then land on
    // a populated Home rather than stranding the user on this management list.
    private void ShowImportChecklist(OpmlImportSelection selection)
    {
        var labels = selection.Items
            .Select(item => item.AlreadySubscribed ? $"{item.Title} (subscribed)" : item.Title)
            .ToArray();
        var checkedState = selection.Items.Select(item => item.Checked).ToArray();

        new AlertDialog.Builder(this)
            .SetTitle("Import subscriptions")!
            .SetMultiChoiceItems(labels, checkedState, (_, args) => checkedState[args.Which] = args.IsChecked)!
            .SetPositiveButton("Add selected", (_, _) =>
            {
                var chosen = selection.Items
                    .Where((_, index) => checkedState[index])
                    .Select(item => item.FeedUrl);
                subscriptions = selection.Apply(chosen, DateTimeOffset.Now);
                Persist();
                ReturnToHome();
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    // Persisted; AudioActivity reloads subscriptions and refreshes feeds in OnResume, so
    // popping back to it shows the shows and their fetching state.
    private void ReturnToHome() => Finish();

    private void Persist() => store.Save(subscriptions.Export());
}
