using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Friday;
using Pulp.Domain.Library;
using Pulp.Domain.People;

namespace Pulp.Domain.Household.Records;

/// <summary>A viewer's veto (D111). "Bring it back" removes the record, leaving a tombstone.</summary>
public sealed record ViewerVeto(ViewerId Viewer, Veto Veto);

/// <summary>A child's own star on a title (D230).</summary>
public sealed record ChildStarEntry(ViewerId Child, ChildStar Star);

/// <summary>
/// A parent's pick for a child (D252, D254). Its own record — not a field of the child's rules — so
/// a parent picking a title and the other parent changing the ceiling at once are two edits that
/// both survive the merge. <see cref="HouseholdView.ChildRules"/> puts the picks back on the one
/// <see cref="Children.ChildRules"/> the filter and Favorites read (S4).
/// </summary>
public sealed record ParentPickEntry(ViewerId Child, ParentPick Pick);

internal sealed class VetoMap : RecordMap<ViewerVeto>
{
    public override string Kind => HouseholdKinds.Veto;

    protected override string IdOf(ViewerVeto value) => RecordIds.Join(value.Viewer.Value, value.Veto.Title.Value);

    public override IReadOnlyDictionary<string, string> FieldsOf(ViewerVeto value) =>
        new FieldWriter()
            .Text("viewer", value.Viewer.Value)
            .Text("title", value.Veto.Title.Value)
            .List("tags", value.Veto.Tags)
            .Fields;

    public override ViewerVeto Read(RecordReader record) =>
        new(new ViewerId(record.Text("viewer")), new Veto(new TitleId(record.Text("title")), record.List("tags")));
}

internal sealed class ChildStarMap : RecordMap<ChildStarEntry>
{
    public override string Kind => HouseholdKinds.ChildStar;

    protected override string IdOf(ChildStarEntry value) => RecordIds.Join(value.Child.Value, value.Star.Title.Value);

    public override IReadOnlyDictionary<string, string> FieldsOf(ChildStarEntry value) =>
        new FieldWriter()
            .Text("child", value.Child.Value)
            .Text("title", value.Star.Title.Value)
            .Instant("at", value.Star.At)
            .Fields;

    public override ChildStarEntry Read(RecordReader record) =>
        new(new ViewerId(record.Text("child")), new ChildStar(new TitleId(record.Text("title")), record.Instant("at")));
}

internal sealed class ParentPickMap : RecordMap<ParentPickEntry>
{
    public override string Kind => HouseholdKinds.ParentPick;

    /// <summary>One pick per child and title: a second parent picking it again is the same card (D230).</summary>
    protected override string IdOf(ParentPickEntry value) => RecordIds.Join(value.Child.Value, value.Pick.Title.Value);

    public override IReadOnlyDictionary<string, string> FieldsOf(ParentPickEntry value) =>
        new FieldWriter()
            .Text("child", value.Child.Value)
            .Text("title", value.Pick.Title.Value)
            .Text("parent", value.Pick.Parent.Value)
            .Instant("at", value.Pick.At)
            .Fields;

    public override ParentPickEntry Read(RecordReader record) =>
        new(
            new ViewerId(record.Text("child")),
            new ParentPick(new TitleId(record.Text("title")), new ViewerId(record.Text("parent")), record.Instant("at")));
}

/// <summary>
/// One child's filter. The parents' picks are <b>not</b> written here — they are
/// <see cref="ParentPickEntry"/> records — so a <see cref="ChildRules"/> put with picks on it stores
/// the ceiling, allow-list and blocks only, and adding or removing a pick goes through its own record.
/// </summary>
internal sealed class ChildRulesMap : RecordMap<ChildRules>
{
    private static readonly FieldWords<AgeBand> Bands = new(new Dictionary<AgeBand, string>
    {
        [AgeBand.UnderSeven] = "under-seven",
        [AgeBand.SevenToNine] = "seven-to-nine",
        [AgeBand.TenToTwelve] = "ten-to-twelve",
        [AgeBand.ThirteenToSixteen] = "thirteen-to-sixteen",
        [AgeBand.Seventeen] = "seventeen",
    });

    public override string Kind => HouseholdKinds.ChildRules;

    protected override string IdOf(ChildRules value) => value.Child.Value;

    public override IReadOnlyDictionary<string, string> FieldsOf(ChildRules value) =>
        new FieldWriter()
            .Text("child", value.Child.Value)
            .Word("ceiling", Bands, value.Ceiling)
            .List("allowed", Sorted(value.Allowed.Select(title => title.Value)))
            .List("blockedTitles", Sorted(value.BlockedTitles.Select(title => title.Value)))
            .List("blockedFranchises", Sorted(value.BlockedFranchises.Select(franchise => franchise.Value)))
            .List("blockedServices", Sorted(value.BlockedServices.Select(service => service.Value)))
            .Fields;

    public override ChildRules Read(RecordReader record) =>
        new(new ViewerId(record.Text("child")), record.Word("ceiling", Bands))
        {
            Allowed = record.List("allowed").Select(id => new TitleId(id)).ToHashSet(),
            BlockedTitles = record.List("blockedTitles").Select(id => new TitleId(id)).ToHashSet(),
            BlockedFranchises = record.List("blockedFranchises").Select(id => new FranchiseId(id)).ToHashSet(),
            BlockedServices = record.List("blockedServices").Select(id => new ServiceId(id)).ToHashSet(),
        };

    // A set has no order; sorting keeps one set one field, so an unchanged set is an unchanged record.
    private static IEnumerable<string> Sorted(IEnumerable<string> values) => values.Order(StringComparer.OrdinalIgnoreCase);
}

internal sealed class ViewerMap : RecordMap<HouseholdViewer>
{
    public override string Kind => HouseholdKinds.Viewer;

    protected override string IdOf(HouseholdViewer value) => value.Id.Value;

    public override IReadOnlyDictionary<string, string> FieldsOf(HouseholdViewer value) =>
        new FieldWriter().Text("viewer", value.Id.Value).Flag("child", value.IsChild).Fields;

    public override HouseholdViewer Read(RecordReader record) =>
        new(new ViewerId(record.Text("viewer")), record.Flag("child"));
}
