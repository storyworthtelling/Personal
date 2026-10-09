using Android.App;
using Android.Content;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Services;

namespace Pulp.HandOffs;

/// <summary>What became of a hand-off the executor was asked for.</summary>
public enum HandOffResult
{
    /// <summary>The service's app was started with the title's link.</summary>
    Started,

    /// <summary>The gate refused it (G13); no intent was ever built.</summary>
    Refused,

    /// <summary>The service's app is not installed on this phone.</summary>
    NotInstalled,
}

/// <summary>
/// The executor's report. <see cref="Notice"/> is the dog-ear's sentence (D122) and is null on success —
/// the app opening is the answer, never a toast. <see cref="Recorded"/> is the <c>HandedOff</c> evidence
/// to keep (D10, D30) and the source of the usage count (D90); it is set only when the app started.
/// </summary>
public sealed record HandOffOutcome(HandOffResult Result, string? Notice, PendingHandOff? Recorded);

/// <summary>
/// The one door a screen hands a title off through (finding §2.4, G13). It asks
/// <see cref="HandOffPlanner"/> — which sends any child, or any group with a child in it, through the
/// child gate — and turns only the plan's own intent into an Android <c>Intent</c>: <c>ACTION_VIEW</c>,
/// the service's https deep link, and <c>SetPackage</c> with the service's package, so Flyleaf's
/// restricted browser (the locked phone's default web handler) never catches it. A refusal never
/// reaches Android at all. Nothing else in the head builds a VIEW intent or calls
/// <c>HandOff.For</c>; <c>HandOffExecutorIsTheOneDoorTests</c> holds that.
/// </summary>
public sealed class HandOffExecutor
{
    private readonly HandOffPlanner planner;
    private readonly ServiceCatalogue catalogue;

    public HandOffExecutor(HandOffPlanner planner, ServiceCatalogue catalogue)
    {
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    }

    public HandOffOutcome Start(Activity host, HandOffRequest request, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);

        var serviceName = catalogue.ById(request.Service)?.Name ?? request.Service.Value;
        var plan = planner.Plan(request, at);
        if (plan.Intent is null || plan.Pending is null)
        {
            return new HandOffOutcome(HandOffResult.Refused, HandOffNotice.ForRefusal(plan.Verdict.Refusal, serviceName), null);
        }

        var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(plan.Intent.Data.AbsoluteUri));
        intent.SetPackage(plan.Intent.Package);

        try
        {
            host.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            // An explicit package with nothing installed to answer it: the app is missing, which the
            // owner can fix, so it is a notice rather than a failure to propagate.
            return new HandOffOutcome(HandOffResult.NotInstalled, HandOffNotice.NotInstalled(serviceName), null);
        }

        return new HandOffOutcome(HandOffResult.Started, null, plan.Pending);
    }
}
