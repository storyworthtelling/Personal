using Constitution.Domain.Voice;

namespace Constitution.Domain.Body.Screens;

/// <summary>
/// A signal's page (C-7.3.4, book page 16): the chart larger with the same window switch, the latest value
/// and its date, the signal's own sentence against the owner's own past, and plain source notes. A signal
/// that is only asking permission carries the ask instead, and no sentence.
/// </summary>
public sealed class SignalPageModel
{
    private SignalPageModel(
        BodySignal signal, BodyTabInputs inputs, SignalCard card, DateOnly? latestDay, PermissionAsk ask,
        SeatRequest? seat, IReadOnlyList<string> sourceNotes)
    {
        Signal = signal;
        Window = inputs.Window;
        Today = inputs.Today;
        Card = card;
        LatestDay = latestDay;
        Ask = ask;
        Seat = seat;
        SourceNotes = sourceNotes;
    }

    public BodySignal Signal { get; }

    /// <summary>The page's Serif header: the signal's name.</summary>
    public string Title => BodySignals.Name(Signal);

    public BodyWindow Window { get; }

    public DateOnly Today { get; }

    /// <summary>The same words the tab's chart wears, larger.</summary>
    public SignalCard Card { get; }

    public SignalCardState State => Card.State;

    public FormHead Head => Card.Head;

    public string ValueText => Card.ValueText;

    public string WhenText => Card.WhenText;

    /// <summary>The right-hand words of the value row: the change, or for a waiting chart what to do.</summary>
    public string ChangeText => Card.ChangeText;

    /// <summary>The day of the latest reading in the window, or null.</summary>
    public DateOnly? LatestDay { get; }

    /// <summary>The latest reading's day in full: "Tuesday, Nov 17"; empty when there is none.</summary>
    public string DateText => LatestDay is { } day ? SignalWording.DateText(day) : string.Empty;

    /// <summary>What the chart needs, when it is asking.</summary>
    public PermissionAsk Ask { get; }

    /// <summary>The signal's own sentence, or null where there is nothing to set against the past.</summary>
    public SeatRequest? Seat { get; }

    /// <summary>Plain status lines saying where the figures came from.</summary>
    public IReadOnlyList<string> SourceNotes { get; }

    public static SignalPageModel Build(BodyTabInputs inputs, BodySignal signal)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var bed = BedOffline.Judge(inputs.Readings, inputs.BedChosen, inputs.Today);
        var card = BodyTabModel.CardFor(inputs, signal, bed);
        var ask = BodyTabModel.AskFor(inputs, signal);
        var latest = BodyTabModel.LatestIn(card.Readings, inputs.Window, inputs.Today);
        var asking = card.State is SignalCardState.AskPermission or SignalCardState.Revoked;

        // A waiting bed has no value row of its own; the page still says the last reading it has.
        var shown = card;
        if (card.State == SignalCardState.WaitingForBed && latest is not null)
        {
            shown = card with
            {
                ValueText = SignalWording.ValueText(signal, latest.Value),
                WhenText = SignalWording.WhenText(signal, latest.Day, inputs.Today),
            };
        }

        return new SignalPageModel(
            signal, inputs, shown, asking ? null : latest?.Day, ask, asking ? null : SeatFor(inputs, signal, latest),
            asking ? [] : Notes(inputs, signal, bed));
    }

    private static SeatRequest? SeatFor(BodyTabInputs inputs, BodySignal signal, BodyReading? latest)
    {
        if (latest is null
            || SignalWording.SeatUnit(signal) is not { } unit
            || SignalChange.From(inputs.Readings, signal, inputs.Window, inputs.Today) is not { } change)
        {
            return null;
        }

        return SeatFacts.Signal(new SignalFacts(
            BodySignals.Name(signal), unit, latest.Value, SignalWording.WhenText(signal, latest.Day, inputs.Today),
            change.Previous, SignalWording.PastLabel(inputs.Window, inputs.Today),
            RisingIsWorse: BodySignals.Rising(signal) == RisingIs.Worse));
    }

    private static List<string> Notes(BodyTabInputs inputs, BodySignal signal, BedOfflineState bed)
    {
        var notes = new List<string>
        {
            signal switch
            {
                BodySignal.Strength => "Worked out from the workout pages.",
                BodySignal.Calories => "Estimated from the meals.",
                _ => "From Health Connect.",
            },
        };

        if (signal == BodySignal.Sleep && inputs.BedChosen)
        {
            notes.Add("The Sleep Number bed fills in nights the watch read as zero.");
            if (bed.IsOffline)
            {
                notes.Add($"The bed hasn’t reported since {bed.LastNight}.");
            }
        }

        return notes;
    }
}
