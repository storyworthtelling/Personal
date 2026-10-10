using System.Globalization;
using Pulp.Domain.Availability;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Services;

namespace Pulp.Domain.Settings;

/// <summary>
/// Settings → Services (mockup 09, D248, D249): a short row per service — its name and tier, and one
/// neutral "Adults only" badge where a child may not start titles (D210). The service's own page holds
/// the rest.
/// </summary>
public static class ServiceRowText
{
    public const string AdultsOnlyBadge = "Adults only";

    public const string AddService = "Add a service";

    /// <summary>Said when Settings has recorded nothing about the plan — never a guessed tier.</summary>
    public const string NotSet = "Plan not set";

    private const string Dot = " · ";

    /// <summary>"4K · no ads", "HD · with ads"; half a plan says only its half; nothing says <see cref="NotSet"/>.</summary>
    public static string Tier(ServicePlanEntry? plan)
    {
        var parts = new List<string>();
        if (plan?.Picture is { } picture)
        {
            parts.Add(Picture(picture));
        }

        if (plan?.Tier is { } tier)
        {
            parts.Add(parts.Count == 0 ? TierChoice(tier) : TierChoice(tier).ToLowerInvariant());
        }

        return parts.Count == 0 ? NotSet : string.Join(Dot, parts);
    }

    /// <summary>The Services header's quiet count: "4 of yours".</summary>
    public static string Count(int had)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(had);
        return had == 0 ? "None yet" : had.ToString(CultureInfo.InvariantCulture) + " of yours";
    }

    /// <summary>Whether the row carries "Adults only": the same question the child gate asks (<see cref="ChildFilter.BadgeFor"/>).</summary>
    public static bool AdultsOnly(ServiceId service, AttestedServices attested, ServiceCatalogue catalogue) =>
        ChildFilter.BadgeFor(service, catalogue, attested) == ServiceBadge.AdultsOnly;

    /// <summary>The picture as every other Pulp page names it.</summary>
    public static string Picture(VideoQuality picture) => picture switch
    {
        VideoQuality.Uhd => "4K",
        VideoQuality.Hd => "HD",
        VideoQuality.Sd => "SD",
        _ => throw new ArgumentOutOfRangeException(nameof(picture), picture, "A picture with no words decided."),
    };

    /// <summary>A tier as its choice on the service page: "No ads", "With ads".</summary>
    public static string TierChoice(PlanTier tier) => tier switch
    {
        PlanTier.AdFree => "No ads",
        PlanTier.WithAds => "With ads",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "A tier with no words decided."),
    };
}
