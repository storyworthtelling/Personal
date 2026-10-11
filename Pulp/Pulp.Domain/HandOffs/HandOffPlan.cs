using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Services;
using Pulp.Domain.Watching;

namespace Pulp.Domain.HandOffs;

/// <summary>Who the gate judged a hand-off for: an adult (or adults), one child, or several children.</summary>
public enum HandOffAudience
{
    Adult,
    Child,
    ChildrenTogether,
}

/// <summary>
/// A hand-off that is about to be sent: the <see cref="HandedOff"/> evidence (D10, D30) it will become
/// once the service's app has actually opened, and what the mis-landing check (D122) and the usage
/// count (D90) need from it. Pending until the executor reports the app started.
/// </summary>
public sealed record PendingHandOff(
    TitleId Title,
    ServiceId Service,
    string Package,
    IReadOnlyList<ViewerId> Watching,
    IReadOnlyList<string> SessionNames,
    HandedOff Evidence);

/// <summary>
/// The planner's answer: the verdict, and — only when it allows the start — the intent and the pending
/// evidence. Built only by <see cref="HandOffPlanner"/>, so holding one means the gate was asked.
/// </summary>
public sealed class HandOffPlan
{
    internal HandOffPlan(HandOffAudience audience, StartVerdict verdict, HandOffIntent? intent, PendingHandOff? pending)
    {
        Audience = audience;
        Verdict = verdict;
        Intent = intent;
        Pending = pending;
    }

    public HandOffAudience Audience { get; }

    public StartVerdict Verdict { get; }

    /// <summary>The intent to send; null whenever the verdict refused (G13).</summary>
    public HandOffIntent? Intent { get; }

    /// <summary>The evidence to record once the app opens; null whenever the verdict refused.</summary>
    public PendingHandOff? Pending { get; }
}
