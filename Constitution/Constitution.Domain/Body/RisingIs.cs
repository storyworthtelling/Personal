namespace Constitution.Domain.Body;

/// <summary>
/// Which way a signal's rising line points. This decides only the <em>order</em> Body lists signals
/// in (worsening first, D70) and the plain word "worse"; it never reaches a chart, which is one
/// neutral line whichever way it goes.
/// </summary>
public enum RisingIs
{
    /// <summary>A rising line is worse: weight, resting heart rate, body fat.</summary>
    Worse,

    /// <summary>A rising line is better: sleep, heart-rate variability, steps, active minutes, strength.</summary>
    Better,

    /// <summary>A rising line is neither: calories. It can move, but it is never called worsening.</summary>
    Neutral,
}
