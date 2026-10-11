using Android.App;
using Android.Content;
using Pulp.Domain.HandOffs;
using Pulp.Domain.LockedOpen;
using Pulp.Domain.Services;
using Pulp.LockedOpen;

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
/// reaches Android at all. On the locked phone (Flyleaf here, the package ticked: <see cref="LockedOpenChoice"/>)
/// the same planned package and link go to Flyleaf as a locked open instead (D288, D298), falling back to the
/// plain intent if that cannot be sent, or if Flyleaf refuses or does not answer (D301). Nothing else in the head builds a VIEW intent or calls
/// <c>HandOff.For</c>; <c>HandOffExecutorIsTheOneDoorTests</c> holds that.
/// </summary>
public sealed class HandOffExecutor
{
    private readonly HandOffPlanner planner;
    private readonly ServiceCatalogue catalogue;
    private readonly LockedOpenFacts? locked;

    public HandOffExecutor(HandOffPlanner planner, ServiceCatalogue catalogue, LockedOpenFacts? locked = null)
    {
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        this.locked = locked;
    }

    /// <summary>
    /// The executor for a household as it stands: the planner is built here, inside the executor's
    /// own construction, so nothing else in the head names <see cref="HandOffPlanner"/> (the one-door
    /// guard). The planner refuses rules for anyone the roster does not name as a child.
    /// </summary>
    public HandOffExecutor(
        Pulp.Domain.People.HouseholdRoster roster,
        IEnumerable<Pulp.Domain.Children.ChildRules> childRules,
        ServiceCatalogue catalogue,
        Pulp.Domain.Children.AttestedServices attested,
        LockedOpenFacts? locked = null)
        : this(new HandOffPlanner(roster, childRules, catalogue, attested), catalogue, locked)
    {
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

        // Release G (D288): on the locked phone the same allowed intent is asked of Flyleaf, which starts it in
        // lock task mode and says so in its answer (D301). Only the planner's own package and link cross. The
        // plain hand-off below is the fallback in both senses: it runs at once if the ask cannot be sent, and
        // later, once, if Flyleaf refuses or never answers, so the title still opens.
        var pending = plan.Pending;
        var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(plan.Intent.Data.AbsoluteUri));
        intent.SetPackage(plan.Intent.Package);

        if (LockedOpenChoice.Decide(locked, plan.Intent.Package) == HandOffRoute.Locked
            && LockedOpenCourier.Send(host, plan.Intent.Package, plan.Intent.Data, () => PlainHandOff(host, intent, serviceName, pending)))
        {
            return new HandOffOutcome(HandOffResult.Started, null, plan.Pending);
        }

        return PlainHandOff(host, intent, serviceName, pending);
    }

    // An explicit package with nothing installed to answer it: the app is missing, which the owner can
    // fix, so it is a notice rather than a failure to propagate. A late fallback (after the locked route
    // answered no) has no outcome to return, so its failure is only logged.
    private static HandOffOutcome PlainHandOff(Activity host, Intent intent, string serviceName, PendingHandOff pending)
    {
        try
        {
            host.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            Android.Util.Log.Info("Pulp", $"{serviceName} is not installed; the plain hand-off had nothing to open.");
            return new HandOffOutcome(HandOffResult.NotInstalled, HandOffNotice.NotInstalled(serviceName), null);
        }

        return new HandOffOutcome(HandOffResult.Started, null, pending);
    }
}
