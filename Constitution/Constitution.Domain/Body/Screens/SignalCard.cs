namespace Constitution.Domain.Body.Screens;

/// <summary>How a signal's chart stands on the Body tab.</summary>
public enum SignalCardState
{
    /// <summary>It has readings in the window and draws its line.</summary>
    Charted,

    /// <summary>Health Connect has not yet been asked, or was asked and said no: the chart says what it needs.</summary>
    AskPermission,

    /// <summary>Health Connect was taken away after it was allowed: the chart waits behind a Problem notice.</summary>
    Revoked,

    /// <summary>The bed has gone quiet: the sleep line stops at its last reading and waits.</summary>
    WaitingForBed,

    /// <summary>Allowed, but nothing has been read in the window yet.</summary>
    NoReadings,
}

/// <summary>
/// One doctor's-chart scrap on the Body tab, as words and the readings behind its line (book page 16):
/// the form head, the latest value in mono with when it was read, and the change in words. A chart that is
/// not drawing carries a <see cref="StatusLine"/> in place of the value. The readings are the signal's own,
/// all of them; the painter lays them out for its width with <see cref="ChartGeometry"/>.
/// </summary>
/// <param name="ValueText">"6.2 h", or empty when the chart is not drawing a value.</param>
/// <param name="WhenText">"last night", or empty.</param>
/// <param name="StatusLine">The strong line of a chart that is waiting: "Waiting for the bed"; empty when it is drawing.</param>
/// <param name="ChangeText">The right-hand words: the change, or for a waiting chart what to do or when it last read.</param>
public sealed record SignalCard(
    BodySignal Signal,
    SignalCardState State,
    FormHead Head,
    string ValueText,
    string WhenText,
    string StatusLine,
    string ChangeText,
    IReadOnlyList<BodyReading> Readings)
{
    /// <summary>True when the chart draws a line (a waiting bed still does, up to its last reading).</summary>
    public bool DrawsALine => State is SignalCardState.Charted or SignalCardState.WaitingForBed;
}
