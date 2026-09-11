using Foreword.Domain.Tasks;

namespace Foreword.Domain.Interfaces;

/// <summary>
/// Turns the owner's own sentence into a structured rule (T6 → T4).
///
/// <para>One field and nothing else. An empty form able to express the lawn rule would carry a dozen
/// controls — a repeat picker, a season picker, a "needs N hours" stepper — and eleven of them would
/// sit blank for "vitamins every day". That form exists, but only once there is a rule to fill it.</para>
///
/// <para>The result is <b>shown back before anything is saved</b>, which is why this returns the
/// parsed rule rather than storing it: a misparse must never quietly schedule the wrong thing.</para>
/// </summary>
public interface ITaskReadingClient
{
    /// <summary>
    /// Null when the sentence could not be read — no network, no key, a refusal, a reply that was
    /// not the shape asked for. The caller keeps the sentence anyway as an unparsed once task and
    /// tries again later, so a rule typed on a dead network becomes a rule without anyone retyping
    /// it.
    /// </summary>
    Task<ForewordTask?> Read(string sentence);
}
