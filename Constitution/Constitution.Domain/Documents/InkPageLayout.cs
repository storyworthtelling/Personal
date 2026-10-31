namespace Constitution.Domain.Documents;

public enum InkViolationKind
{
    /// <summary>Another target or printed item is closer than the minimum clearance.</summary>
    TooClose,

    /// <summary>Something is printed inside a target.</summary>
    PrintedInside,

    /// <summary>The target is off the page or under the header.</summary>
    OutsideTheBody,

    /// <summary>No words are printed for the target.</summary>
    Unnamed,
}

/// <summary>What broke, at which target, against which other thing (a target id or a printed item's name).</summary>
public sealed record InkViolation(InkViolationKind Kind, string TargetId, string Other);

/// <summary>One page: its title, its map of targets, and everything else printed on it.</summary>
public sealed class InkPageLayout
{
    private readonly List<PrintedItem> printed;

    private InkPageLayout(string title, InkMap map, List<PrintedItem> printed)
    {
        Title = title;
        Map = map;
        this.printed = printed;
    }

    public string Title { get; }

    public InkMap Map { get; }

    public IReadOnlyList<PrintedItem> Printed => printed;

    public static InkPageLayout Create(string title, IEnumerable<InkTarget> targets, IEnumerable<PrintedItem> printed)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A page needs a title; the header prints it above the rule.", nameof(title));
        }

        return new InkPageLayout(title, new InkMap(targets), printed.ToList());
    }

    /// <summary>
    /// Every way this page breaks page 4's rules. Empty means the page is fit to print. Nothing here
    /// throws, so a test (and the painter) can ask what is wrong rather than only that something is.
    /// </summary>
    public IReadOnlyList<InkViolation> Violations()
    {
        var found = new List<InkViolation>();
        var body = ConstitutionPage.Body;

        foreach (var target in Map.Targets)
        {
            if (!body.Encloses(target.Bounds))
            {
                found.Add(new InkViolation(InkViolationKind.OutsideTheBody, target.Id, "the page body"));
            }

            if (!printed.Any(item => !string.IsNullOrWhiteSpace(item.Text) && BelongsTo(item, target)))
            {
                found.Add(new InkViolation(InkViolationKind.Unnamed, target.Id, "no words"));
            }

            var keepClear = target.Bounds.Inflate(InkTargets.MinimumClearance);

            foreach (var other in Map.Targets)
            {
                var sameGroup = target.Group is not null && target.Group == other.Group;
                if (!ReferenceEquals(other, target) && !sameGroup && keepClear.Overlaps(other.Bounds))
                {
                    found.Add(new InkViolation(InkViolationKind.TooClose, target.Id, other.Id));
                }
            }

            foreach (var item in printed)
            {
                // A shaded ground and a reserved slot are not print: no pen mark is confused with them.
                if (item.Role is PrintedRole.Band or PrintedRole.Slot or PrintedRole.Chart or PrintedRole.MarkFilled or PrintedRole.MarkOpen)
                {
                    continue;
                }

                if (BelongsToFamily(item, target) && target.AllowsOwnPrintInside)
                {
                    continue;
                }

                if (item.Bounds.Overlaps(target.Bounds))
                {
                    found.Add(new InkViolation(InkViolationKind.PrintedInside, target.Id, item.Name));
                }
                else if (keepClear.Overlaps(item.Bounds))
                {
                    found.Add(new InkViolation(InkViolationKind.TooClose, target.Id, item.Name));
                }
            }
        }

        return found;
    }

    private static bool BelongsTo(PrintedItem item, InkTarget target) =>
        item.OwnerId is not null && (item.OwnerId == target.Id || item.OwnerId == target.Group);

    /// <summary>
    /// Belongs to the target, to its group, or to a sibling in its group: a lane's word is not
    /// "close" to the lane beside it, and a divider belongs to every lane it separates.
    /// </summary>
    private bool BelongsToFamily(PrintedItem item, InkTarget target)
    {
        if (BelongsTo(item, target))
        {
            return true;
        }

        var owner = item.OwnerId is null ? null : Map.TryGet(item.OwnerId);
        return owner?.Group is not null && owner.Group == target.Group;
    }
}
