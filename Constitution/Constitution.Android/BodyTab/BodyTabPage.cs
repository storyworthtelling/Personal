using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Ui;
using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Scraps;
using Constitution.Domain.Voice;
using Constitution.Scrapbook;

namespace Constitution.BodyTab;

/// <summary>
/// The Body tab (book page 16): the Month / Quarter / Year switch, the month's sentence on a taped notepad
/// leaf, then one doctor's-chart scrap per signal in the order the model gave. It paints a
/// <see cref="BodyTabModel"/> and decides nothing. Switching the window refills the scraps in place, and the
/// voiced sentence replaces the plain one in place when it arrives; nothing waits behind a spinner.
/// </summary>
internal sealed class BodyTabPage : LinearLayout, IFullBleedPage
{
    private readonly ScrapHost host;
    private readonly Action<BodySignal> onOpen;
    private BodyTabModel? shown;

    public BodyTabPage(Context context, BodyWindow window, Color hue, Action<BodyWindow> onWindow, Action<BodySignal> onOpen)
        : base(context)
    {
        this.onOpen = onOpen;
        Orientation = Orientation.Vertical;

        AddView(BodyWindowSwitch.Build(context, window, hue, onWindow), BodyWindowSwitch.Params(context));

        host = new ScrapHost(context);
        var scroll = new ScrollView(context);
        scroll.AddView(host, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        AddView(scroll, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
    }

    /// <summary>Lays the scraps out for a model. <paramref name="first"/> is the month's sentence as it stands now.</summary>
    public void Show(BodyTabModel model, SeatLine? first)
    {
        var scraps = new List<ScrapEntry>();
        shown = model;
        if (model.Monthly is null)
        {
            scraps.Add(BodyScrapContent.PlainLeaf(Context!, BodyScrapContent.MonthlyScrapId, model.NothingToCompare));
        }
        else if (first is not null)
        {
            // The month's sentence is shown only in the owner's voice; with none, the seat hides (owner's ruling, W10).
            scraps.Add(BodyScrapContent.Leaf(Context!, BodyScrapContent.MonthlyScrapId, first).Entry);
        }

        foreach (var card in model.Cards)
        {
            var signal = card.Signal;
            scraps.Add(BodyScrapContent.Chart(Context!, card, model.Window, model.Today, () => onOpen(signal)));
        }

        host.SetScraps(scraps);
    }

    /// <summary>Lays the page out again with the month's voiced sentence, now it has arrived. UI thread.</summary>
    public void ShowSentence(SeatLine line)
    {
        if (shown is { } model)
        {
            Show(model, line);
        }
    }
}
