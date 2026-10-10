using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.People;

namespace Pulp.Domain.Household.Records;

/// <summary>A service the owner attested keeps every adult profile PIN-locked (D210); removing it is its tombstone.</summary>
public sealed record AttestedServiceEntry(ServiceId Service);

/// <summary>A service the household has; removing it is its tombstone.</summary>
public sealed record HouseholdServiceEntry(ServiceId Service);

internal sealed class AttestedServiceMap : RecordMap<AttestedServiceEntry>
{
    public override string Kind => HouseholdKinds.AttestedService;

    protected override string IdOf(AttestedServiceEntry value) => value.Service.Value;

    public override IReadOnlyDictionary<string, string> FieldsOf(AttestedServiceEntry value) =>
        new FieldWriter().Text("service", value.Service.Value).Fields;

    public override AttestedServiceEntry Read(RecordReader record) => new(new ServiceId(record.Text("service")));
}

internal sealed class HouseholdServiceMap : RecordMap<HouseholdServiceEntry>
{
    public override string Kind => HouseholdKinds.HouseholdService;

    protected override string IdOf(HouseholdServiceEntry value) => value.Service.Value;

    public override IReadOnlyDictionary<string, string> FieldsOf(HouseholdServiceEntry value) =>
        new FieldWriter().Text("service", value.Service.Value).Fields;

    public override HouseholdServiceEntry Read(RecordReader record) => new(new ServiceId(record.Text("service")));
}

/// <summary>The one map per record kind. Writers and readers both go through these, so a kind has one schema.</summary>
public static class HouseholdMaps
{
    public static RecordMap<WatchedSitting> Watched { get; } = new WatchedMap();

    public static RecordMap<HouseholdPlaylist> Playlist { get; } = new PlaylistMap();

    public static RecordMap<ViewerVeto> Veto { get; } = new VetoMap();

    public static RecordMap<ChildStarEntry> ChildStar { get; } = new ChildStarMap();

    public static RecordMap<ParentPickEntry> ParentPick { get; } = new ParentPickMap();

    public static RecordMap<HomeChoice> HomeOverride { get; } = new HomeOverrideMap();

    public static RecordMap<ChildRules> ChildRules { get; } = new ChildRulesMap();

    public static RecordMap<HouseholdViewer> Viewer { get; } = new ViewerMap();

    public static RecordMap<AttestedServiceEntry> AttestedService { get; } = new AttestedServiceMap();

    public static RecordMap<HouseholdServiceEntry> HouseholdService { get; } = new HouseholdServiceMap();

    public static RecordMap<ContinueDismissedEntry> ContinueDismissed { get; } = new ContinueDismissedMap();

    public static RecordMap<ServicePlanEntry> ServicePlan { get; } = new ServicePlanMap();

    public static RecordMap<ViewerNameEntry> ViewerName { get; } = new ViewerNameMap();

    /// <summary>Every map's kind, for the guard that holds them frozen.</summary>
    public static IReadOnlyList<string> Kinds { get; } =
    [
        Watched.Kind, Playlist.Kind, Veto.Kind, ChildStar.Kind, ParentPick.Kind,
        HomeOverride.Kind, ChildRules.Kind, Viewer.Kind, AttestedService.Kind, HouseholdService.Kind,
        ContinueDismissed.Kind, ServicePlan.Kind, ViewerName.Kind,
    ];
}
