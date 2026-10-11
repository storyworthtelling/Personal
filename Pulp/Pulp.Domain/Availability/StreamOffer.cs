using Pulp.Domain.Core;

namespace Pulp.Domain.Availability;

/// <summary>
/// One service carrying one title at one quality, as the availability source reports it.
/// <paramref name="AlwaysWithAds"/> is a free, ad-supported service, which shows ads whatever the plan.
/// </summary>
public sealed record StreamOffer(ServiceId Service, VideoQuality Quality, bool AlwaysWithAds = false);

/// <summary>
/// A service the household has, whether its plan tier is ad-free (D29) and, when Settings recorded it, the
/// best picture the plan streams at (D86). That picture caps what a title can be claimed to play at on
/// this service: an HD plan shows HD whatever the catalogue lists the title at (D28).
/// </summary>
public sealed record ServicePlan(ServiceId Service, bool AdFree, VideoQuality? Picture = null);
