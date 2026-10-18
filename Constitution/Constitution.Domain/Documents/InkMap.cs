namespace Constitution.Domain.Documents;

/// <summary>Every target on one page, by id and by position: the single map painter and reader share.</summary>
public sealed class InkMap
{
    private readonly List<InkTarget> targets;
    private readonly Dictionary<string, InkTarget> byId = new(StringComparer.Ordinal);

    public InkMap(IEnumerable<InkTarget> targets)
    {
        this.targets = targets.ToList();
        foreach (var target in this.targets)
        {
            if (!byId.TryAdd(target.Id, target))
            {
                throw new ArgumentException(
                    $"Two targets on one page are both called '{target.Id}'; the reader would not know which box was meant.",
                    nameof(targets));
            }
        }
    }

    public IReadOnlyList<InkTarget> Targets => targets;

    public InkTarget ById(string id) =>
        byId.TryGetValue(id, out var target)
            ? target
            : throw new KeyNotFoundException($"No target called '{id}' is on this page.");

    public InkTarget? TryGet(string id) => byId.GetValueOrDefault(id);
}
