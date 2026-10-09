using Pulp.Domain.Core;

namespace Pulp.Domain.Availability;

/// <summary>
/// One service carrying one title at one quality, as the availability source reports it.
/// <paramref name="AlwaysWithAds"/> is a free, ad-supported service, which shows ads whatever the plan.
/// </summary>
public sealed record StreamOffer(ServiceId Service, VideoQuality Quality, bool AlwaysWithAds = false);

/// <summary>A service the household has, and whether its plan tier is ad-free (D29, recorded in Settings).</summary>
public sealed record ServicePlan(ServiceId Service, bool AdFree);
