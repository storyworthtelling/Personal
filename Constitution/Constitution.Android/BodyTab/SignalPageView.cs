using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Scraps;
using Constitution.Domain.Voice;
using Constitution.Scrapbook;

namespace Constitution.BodyTab;

/// <summary>
/// A signal's page (book page 16, second and third phones): the chart larger under the same window switch, the
/// latest value with its date, the signal's own sentence on a taped leaf, and the plain source notes beneath.
/// A signal still waiting on Health Connect shows what it needs and that it only reads, with the reason under
/// it; the one action, Allow in Health Connect, is in the pinned bar the shell draws (see <see cref="SignalBar"/>).
/// It paints a <see cref="SignalPageModel"/> and decides nothing; switching the window refills it in place.
/// </summary>
internal sealed class SignalPageView : LinearLayout, IFullBleedPage
{
    private readonly ScrapHost host;
    private readonly LinearLayout notes;
    private BodySeatNote? note;

    public SignalPageView(Context context, BodyWindow window, Color hue, Action<BodyWindow> onWindow) : base(context)
    {
        Orientation = Orientation.Vertical;
        AddView(BodyWindowSwitch.Build(context, window, hue, onWindow), BodyWindowSwitch.Params(context));

        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        host = new ScrapHost(context);
        column.AddView(host, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        notes = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var density = context.Resources!.DisplayMetrics!.Density;
        notes.SetPadding(
            (int)Math.Round(ScrapMetrics.WellPaddingStartDp * density),
            PaperTheme.Dp(context, DesignTokens.Space.X3),
            (int)Math.Round(ScrapMetrics.WellPaddingEndDp * density),
            PaperTheme.Dp(context, DesignTokens.Space.X8));
        column.AddView(notes, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var scroll = new ScrollView(context);
        scroll.AddView(column, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        AddView(scroll, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
    }

    /// <summary>Lays the page out for a model. <paramref name="first"/> is the signal's sentence as it stands now.</summary>
    public void Show(SignalPageModel model, SeatLine? first)
    {
        var scraps = new List<ScrapEntry>();
        note = null;
        if (model.Ask.Kind != AskKind.None)
        {
            scraps.Add(BodyScrapContent.Ask(Context!, model.Signal, model.Ask));
        }
        else
        {
            scraps.Add(BodyScrapContent.Chart(Context!, model.Card, model.Window, model.Today, onOpen: null, large: true));
            if (model.Seat is not null && first is not null)
            {
                var (entry, leaf) = BodyScrapContent.Leaf(Context!, "body-signal-sentence", first);
                note = leaf;
                scraps.Add(entry);
            }
        }

        host.SetScraps(scraps);

        notes.RemoveAllViews();
        if (model.Ask.Note is { } askNote)
        {
            notes.AddView(StatusLine(askNote));
        }

        if (model.Ask.Kind == AskKind.None && model.DateText.Length > 0)
        {
            notes.AddView(StatusLine(model.DateText));
        }

        foreach (var source in model.SourceNotes)
        {
            notes.AddView(StatusLine(source));
        }
    }

    /// <summary>Swaps the signal's sentence in place (the voiced one has arrived). UI thread.</summary>
    public void ShowSentence(SeatLine line) => note?.Show(line);

    private TextView StatusLine(string text)
    {
        var line = PaperUi.Secondary(Context!, text);
        line.SetPadding(0, PaperTheme.Dp(Context!, DesignTokens.Space.X1), 0, 0);
        return line;
    }
}

/// <summary>The pinned bar of a signal's page: the one action, and only while the chart is waiting on Health Connect.</summary>
internal static class SignalBar
{
    /// <summary>The bar for a page, or null when the chart needs nothing.</summary>
    public static View? Build(Context context, SignalPageModel model, Action onAllow) =>
        model.Ask.BarLabel is { } label
            ? PaperUi.BottomBar(context, PaperUi.PrimaryButton(context, label, onAllow))
            : null;
}
