using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Audio;
using Codex.Domain.Design;
using Codex.Ui;

namespace Score;

/// <summary>
/// What is downloaded, and how much of the week's allowance it is using.
///
/// The budget is <b>playback time</b>, not megabytes, because that is the owner's
/// unit and the honest one: twelve hours is a thing you can picture — a flight, a week
/// of commutes — where 4.7 GB is a number you have to convert first, and which means
/// something different for a podcast than for a lossless album.
///
/// One explainer rather than a screen of switches. There is nothing to configure here:
/// the allowance is fixed, the ten days are fixed, and what is left to decide is what
/// to keep — so the screen is a meter, a sentence, and a list you can take things off.
/// </summary>
[Activity(Label = "Downloads", Theme = "@style/Theme.FocusPhone.Paper")]
public class DownloadsActivity : Activity
{
    private DownloadStore store = null!;
    private DownloadBudget budget = DownloadBudget.Empty;
    private BudgetMeter meter = null!;
    private TextView reading = null!;
    private LinearLayout rows = null!;
    private FrameLayout undoHost = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TapWatch.Attach(this);
        PaperTransitions.Apply(this);
        store = new DownloadStore(this);

        // Swept on the way in, so the list never shows something the sweep has already
        // decided about.
        budget = Downloads.Sweep(this, store, DateTimeOffset.UtcNow);
        SetContentView(BuildLayout());
        Render();
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };

        reading = PaperUi.Body(this, string.Empty);
        reading.SetPadding(0, 0, 0, PaperTheme.Dp(this, DesignTokens.Space.X2));
        page.AddView(reading);

        meter = new BudgetMeter(this);
        var meterParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(this, BudgetMeter.HeightDp));
        meterParams.SetMargins(0, 0, 0, PaperTheme.Dp(this, DesignTokens.Space.X3));
        page.AddView(meter, meterParams);

        var explainer = PaperUi.Secondary(this,
            "Downloads are measured in playback time and share one allowance across music and podcasts. " +
            "Each one is removed ten days after it arrives — playing it does not extend that, so the phone " +
            "does not slowly fill up with things you meant to get to.");
        explainer.SetPadding(0, 0, 0, PaperTheme.Dp(this, DesignTokens.Space.X4));
        page.AddView(explainer);

        undoHost = new FrameLayout(this);
        var scroll = new ScrollView(this);
        rows = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(rows);
        undoHost.AddView(scroll, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        page.AddView(undoHost, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        root.AddView(BinderFrame.Wrap(
            this, "Downloads", ScoreShellAreas.All[ScoreShellAreas.Settings].Hue, page,
            ScoreShellAreas.All, ScoreShellAreas.Settings, SwitchArea),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return root;
    }

    // Names the tab ScoreActivity should land on once this screen finishes (see
    // ScoreActivity.PendingArea); ReorderToFront brings its existing instance forward rather
    // than creating a second one, or starts it fresh if it isn't running at all.
    private void SwitchArea(int index)
    {
        ScoreActivity.PendingArea = index;
        var intent = new Intent(this, typeof(ScoreActivity));
        intent.AddFlags(ActivityFlags.ReorderToFront);
        StartActivity(intent);
        Finish();
    }

    private void Render()
    {
        meter.Show(budget);
        reading.Text = Reading();

        rows.RemoveAllViews();
        if (budget.Items.Count == 0)
        {
            rows.AddView(PaperUi.EmptyState(this, "Nothing downloaded."));
            return;
        }

        // Oldest first: what is closest to going is what you most want to see.
        foreach (var item in budget.Items.OrderBy(held => held.DownloadedUtc))
        {
            rows.AddView(BuildRow(item));
        }
    }

    // The figure, in the mono face figures are set in, and the overage said plainly
    // rather than hidden behind a full bar.
    private string Reading()
    {
        var used = Hours(budget.Used);
        var capacity = Hours(DownloadBudget.Capacity);
        var over = budget.Overage(TimeSpan.Zero);
        return over > TimeSpan.Zero
            ? $"{used} of {capacity} — {Hours(over)} over"
            : $"{used} of {capacity}";
    }

    private View BuildRow(DownloadedItem item)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetMinimumHeight(PaperTheme.Dp(this, DesignTokens.Shape.ListRowMinHeightDp));

        var lines = new LinearLayout(this) { Orientation = Orientation.Vertical };
        lines.AddView(PaperUi.Body(this, item.Title));

        var days = budget.DaysLeft(item.MediaId, DateTimeOffset.UtcNow) ?? 0;
        lines.AddView(PaperUi.Secondary(this, $"{Hours(item.Duration)} · {Days(days)}"));
        row.AddView(lines, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        row.AddView(PaperUi.QuietButton(this, "Remove", () => RemoveWithUndo(item)));
        return row;
    }

    // Undo-first, like every other destructive act: the file is fetched again rather
    // than restored, which costs bandwidth but never costs the owner the decision.
    private void RemoveWithUndo(DownloadedItem item)
    {
        Downloads.Remove(this, item.MediaId);
        budget = budget.Without(item.MediaId);
        store.Save(budget);
        Render();

        DogEar.Show(this, undoHost, $"Removed {item.Title}.", onUndo: () =>
        {
            Downloads.Add(this, item.MediaId, MediaUri(item));
            budget = budget.With(item with { DownloadedUtc = DateTimeOffset.UtcNow });
            store.Save(budget);
            Render();
        });
    }

    // Music is a Drive file id; a podcast episode is already a URL. The ledger keeps
    // the media id, which is exactly what the player uses, so this is the only place
    // that has to know the difference.
    private static string MediaUri(DownloadedItem item) =>
        item.Kind == DownloadKind.Music ? $"drive://{item.MediaId}" : item.MediaId;

    private static string Hours(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours}h {span.Minutes:D2}m"
        : $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} min";

    private static string Days(int days) => days switch
    {
        0 => "goes today",
        1 => "1 day left",
        _ => $"{days} days left",
    };
}
