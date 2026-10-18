using Constitution.Domain.Health;
using Constitution.Domain.Voice;

namespace Constitution.Domain.Body.Screens;

/// <summary>Everything the Body tab is built from: the stored readings, the window, today, and what Health Connect has said.</summary>
/// <param name="Readings">Every stored reading, all signals.</param>
/// <param name="Choices">What the owner has been asked and has given, as saved.</param>
/// <param name="IsGranted">Whether the platform lets a signal be read right now.</param>
/// <param name="BedChosen">Whether a side of the Sleep Number bed has been chosen, so the bed's silence means something.</param>
/// <param name="RevokedSignals">Signals this process has seen taken away and not yet allowed again.</param>
public sealed record BodyTabInputs(
    IReadOnlyList<BodyReading> Readings,
    BodyWindow Window,
    DateOnly Today,
    HealthChoices Choices,
    Func<BodySignal, bool> IsGranted,
    bool BedChosen,
    IReadOnlySet<BodySignal> RevokedSignals)
{
    /// <summary>These inputs with the day still being counted left out of the readings (S5), so every figure and line drawn from them agrees.</summary>
    public BodyTabInputs Settled() => this with { Readings = BodySignals.Settled(Readings, Today).ToList() };
}

/// <summary>
/// The Body tab (C-7.3.3, book page 16): the window switch, the month's sentence, then one chart per
/// signal, the worsening first. Every decision is made here; the head paints and taps. Nothing in a card
/// is a colour or a verdict: the line is neutral ink, and the order is the only place a signal's direction
/// shows.
/// </summary>
public sealed class BodyTabModel
{
    /// <summary>What the page says in place of a monthly sentence when there is no earlier stretch to compare with.</summary>
    public const string NothingToCompareSentence = "Nothing to compare yet. Body reads from the day it was installed.";

    /// <summary>The words on the right of a chart that has no earlier stretch.</summary>
    public const string NothingEarlier = "nothing to compare with yet";

    private BodyTabModel(
        BodyWindow window, DateOnly today, IReadOnlyList<SignalCard> cards, SeatRequest? monthly,
        string? skippedDevice, BedOfflineState bed)
    {
        Window = window;
        Today = today;
        Cards = cards;
        Monthly = monthly;
        SkippedDevice = skippedDevice;
        Bed = bed;
    }

    /// <summary>The three windows in the order the switch shows them.</summary>
    public static IReadOnlyList<BodyWindow> Windows => BodyWindows.All;

    /// <summary>The switch's three labels, in the same order.</summary>
    public static IReadOnlyList<string> WindowLabels { get; } = ["Month", "Quarter", "Year"];

    public BodyWindow Window { get; }

    public DateOnly Today { get; }

    /// <summary>The charts in the order they are listed.</summary>
    public IReadOnlyList<SignalCard> Cards { get; }

    /// <summary>The monthly seat's request, or null when there is nothing to say.</summary>
    public SeatRequest? Monthly { get; }

    /// <summary>The device the day-after reminder names, or null.</summary>
    public string? SkippedDevice { get; }

    /// <summary>The bed, quiet or not.</summary>
    public BedOfflineState Bed { get; }

    /// <summary>What the note says when <see cref="Monthly"/> is null.</summary>
    public string NothingToCompare => NothingToCompareSentence;

    public static BodyTabModel Build(BodyTabInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        inputs = inputs.Settled();
        var bed = BedOffline.Judge(inputs.Readings, inputs.BedChosen, inputs.Today);
        var cards = Signals(inputs).Select(signal => CardFor(inputs, signal, bed)).ToList();
        return new BodyTabModel(
            inputs.Window, inputs.Today, Ordered(inputs, cards), MonthlySeat(inputs, bed, out var device), device, bed);
    }

    // ---- which charts, and in what order ----

    /// <summary>Health Connect's signals always; a worked-out one once it has a reading.</summary>
    internal static IEnumerable<BodySignal> Signals(BodyTabInputs inputs) =>
        BodySignals.All.Where(signal =>
            BodySignals.HealthConnect.Contains(signal) || inputs.Readings.Any(reading => reading.Signal == signal));

    private static List<SignalCard> Ordered(BodyTabInputs inputs, List<SignalCard> cards)
    {
        var withChange = cards
            .Where(card => card.DrawsALine)
            .Select(card => SignalChange.From(inputs.Readings, card.Signal, inputs.Window, inputs.Today))
            .OfType<SignalChange>()
            .ToList();
        var first = SignalOrder.Order(withChange).Select(change => cards.Single(card => card.Signal == change.Signal)).ToList();
        return [.. first, .. cards.Where(card => !first.Contains(card))];
    }

    // ---- one chart ----

    internal static SignalCard CardFor(BodyTabInputs inputs, BodySignal signal, BedOfflineState bed)
    {
        var own = inputs.Readings.Where(reading => reading.Signal == signal).OrderBy(reading => reading.Day).ToList();
        var ask = AskFor(inputs, signal);
        if (ask.Kind == AskKind.Revoked)
        {
            return new SignalCard(
                signal, SignalCardState.Revoked, FormHead.For(signal, window: null), string.Empty, string.Empty,
                "Health Connect was switched off", "open to allow it again", own);
        }

        if (ask.Kind != AskKind.None)
        {
            return new SignalCard(
                signal, SignalCardState.AskPermission, FormHead.For(signal, window: null), string.Empty, string.Empty,
                "Not read yet", "open to allow it", own);
        }

        var head = FormHead.For(signal, inputs.Window);
        if (signal == BodySignal.Sleep && bed.IsOffline)
        {
            return new SignalCard(
                signal, SignalCardState.WaitingForBed, head, string.Empty, string.Empty,
                BedOffline.Headline, $"Last reading {bed.LastNight}", own);
        }

        var latest = LatestIn(own, inputs.Window, inputs.Today);
        if (latest is null)
        {
            return new SignalCard(
                signal, SignalCardState.NoReadings, head, string.Empty, string.Empty, "No readings yet", string.Empty, own);
        }

        var change = SignalChange.From(inputs.Readings, signal, inputs.Window, inputs.Today);
        return new SignalCard(
            signal, SignalCardState.Charted, head,
            SignalWording.ValueText(signal, latest.Value),
            SignalWording.WhenText(signal, latest.Day, inputs.Today),
            string.Empty,
            change?.Words ?? NothingEarlier,
            own);
    }

    internal static PermissionAsk AskFor(BodyTabInputs inputs, BodySignal signal)
    {
        // A side of the bed chosen means the bed supplies sleep (S7): Health Connect's sleep is then a bonus, not a need.
        var bedSupplies = signal == BodySignal.Sleep && inputs.BedChosen;
        return PermissionAsk.For(
            signal,
            inputs.Choices.Of(signal),
            bedSupplies || !BodySignals.HealthConnect.Contains(signal) || inputs.IsGranted(signal),
            !bedSupplies && inputs.RevokedSignals.Contains(signal));
    }

    /// <summary>The most recent reading in the window, or null.</summary>
    internal static BodyReading? LatestIn(IReadOnlyList<BodyReading> readings, BodyWindow window, DateOnly today)
    {
        var first = today.AddDays(1 - BodyWindows.Days(window));
        return BodySignals.Settled(readings, today)
            .Where(reading => reading.Day >= first && reading.Day <= today)
            .MaxBy(reading => reading.Day);
    }

    // ---- the month's sentence ----

    private static SeatRequest? MonthlySeat(BodyTabInputs inputs, BedOfflineState bed, out string? device)
    {
        var months = new List<SignalMonth>();
        foreach (var signal in BodySignals.HealthConnect)
        {
            if (AskFor(inputs, signal).Kind != AskKind.None
                || SignalWording.SeatUnit(signal) is not { } unit
                || SignalChange.From(inputs.Readings, signal, BodyWindow.Month, inputs.Today) is not { } change)
            {
                continue;
            }

            months.Add(new SignalMonth(
                BodySignals.Name(signal), unit, change.Current, change.Previous,
                RisingIsWorse: BodySignals.Rising(signal) == RisingIs.Worse,
                NamePlural: signal is BodySignal.Steps or BodySignal.ActiveMinutes));
        }

        device = DeviceReminder.SkippedDevice(inputs.Readings, inputs.Today);
        if (device is not null)
        {
            return SeatFacts.BodyMonthly(new BodyMonthFacts(months, device));
        }

        if (months.Count == 0)
        {
            return bed.IsOffline ? new SeatRequest(VoiceSeat.BodyMonthly, [BedOffline.Sentence(bed)], BedOffline.Sentence(bed)) : null;
        }

        var request = SeatFacts.BodyMonthly(new BodyMonthFacts(months));
        return bed.IsOffline
            ? new SeatRequest(VoiceSeat.BodyMonthly, [.. request.Facts, BedOffline.Sentence(bed)], $"{request.Fallback} {BedOffline.Sentence(bed)}")
            : request;
    }
}
