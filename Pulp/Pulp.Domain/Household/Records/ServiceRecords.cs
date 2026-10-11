using Pulp.Domain.Availability;
using Pulp.Domain.Core;

namespace Pulp.Domain.Household.Records;

/// <summary>A subscription's ad tier, as Settings records it (D29).</summary>
public enum PlanTier
{
    AdFree,
    WithAds,
}

/// <summary>
/// What Settings records about one of the household's services (D29, D86, D8): its plan tier, the best
/// picture the plan streams at, and whether Flyleaf shows its app. Each is optional on its own — the
/// owner may set the tier before the picture, or neither — and an absent tier is <b>not set</b>, never
/// taken as either answer here (<see cref="HouseholdView.ServicePlans"/> decides what an unset tier
/// means for the best home). Whether the household has the service at all is
/// <see cref="HouseholdServiceEntry"/>'s, a separate record, so unticking a service keeps its plan for
/// when it is ticked again.
/// </summary>
public sealed record ServicePlanEntry(ServiceId Service)
{
    public PlanTier? Tier { get; init; }

    /// <summary>The best picture the plan streams at (the "4K" of "4K · no ads"); null when not set.</summary>
    public VideoQuality? Picture { get; init; }

    /// <summary>
    /// D8: a streaming app leaves the Flyleaf tray and is reached only through Pulp, so the default is
    /// hidden; true is the owner's choice to keep this one on the tray. Stored now, read by Flyleaf when
    /// the cross-app contract lands (Release F).
    /// </summary>
    public bool ShownInFlyleaf { get; init; }
}

/// <summary>
/// The name a viewer goes by. The <see cref="ViewerId"/> keys every record a viewer has — sittings,
/// vetoes, playlists, a child's rules — so a rename is a name record, not a new id: renaming by id
/// would leave everything they own under the old one.
/// </summary>
public sealed record ViewerNameEntry(ViewerId Viewer, string Name);

internal sealed class ServicePlanMap : RecordMap<ServicePlanEntry>
{
    private static readonly FieldWords<PlanTier> Tiers = new(new Dictionary<PlanTier, string>
    {
        [PlanTier.AdFree] = "ad-free",
        [PlanTier.WithAds] = "with-ads",
    });

    private static readonly FieldWords<VideoQuality> Pictures = new(new Dictionary<VideoQuality, string>
    {
        [VideoQuality.Sd] = "sd",
        [VideoQuality.Hd] = "hd",
        [VideoQuality.Uhd] = "uhd",
    });

    public override string Kind => HouseholdKinds.ServicePlan;

    protected override string IdOf(ServicePlanEntry value) => value.Service.Value;

    public override IReadOnlyDictionary<string, string> FieldsOf(ServicePlanEntry value) =>
        new FieldWriter()
            .Text("service", value.Service.Value)
            .Text("tier", value.Tier is { } tier ? Tiers.Word(tier) : null)
            .Text("picture", value.Picture is { } picture ? Pictures.Word(picture) : null)
            .Flag("shownInFlyleaf", value.ShownInFlyleaf)
            .Fields;

    public override ServicePlanEntry Read(RecordReader record) =>
        new(new ServiceId(record.Text("service")))
        {
            Tier = Optional(record, "tier", Tiers),
            Picture = Optional(record, "picture", Pictures),
            ShownInFlyleaf = record.OptionalText("shownInFlyleaf") is not null && record.Flag("shownInFlyleaf"),
        };

    // An empty field is "not set"; a word this build does not know is unreadable, never a guess.
    private static TEnum? Optional<TEnum>(RecordReader record, string name, FieldWords<TEnum> words)
        where TEnum : struct, Enum =>
        record.OptionalText(name) is null ? null : record.Word(name, words);
}

internal sealed class ViewerNameMap : RecordMap<ViewerNameEntry>
{
    public override string Kind => HouseholdKinds.ViewerName;

    protected override string IdOf(ViewerNameEntry value) => value.Viewer.Value;

    public override IReadOnlyDictionary<string, string> FieldsOf(ViewerNameEntry value) =>
        new FieldWriter().Text("viewer", value.Viewer.Value).Text("name", value.Name).Fields;

    public override ViewerNameEntry Read(RecordReader record) =>
        new(new ViewerId(record.Text("viewer")), record.Text("name"));
}
