using Constitution.Domain.Documents;
using Constitution.Domain.Ink;

namespace Constitution.Domain.ReadBack;

/// <summary>
/// What each <see cref="InkRule"/> reads off the page and what it is reported under. The resolver holds
/// this knowledge privately; the read-back needs it twice more (to tell the vision read which boxes to
/// look at, and to say which ids belong to a session), so it is written once here and a test holds it to
/// the resolver.
/// </summary>
public static class RuleTargets
{
    /// <summary>The boxes this rule reads, lanes of a circle group included. A box the map does not have is left out.</summary>
    public static IReadOnlyList<InkTarget> For(InkRule rule, InkMap map)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(map);

        var found = new List<InkTarget>();
        switch (rule)
        {
            case WorkoutBoxRule r:
                AddBox(found, map, r.BoxId);
                break;
            case ExercisePageRule r:
                AddBox(found, map, r.DoneBoxId);
                AddLanes(found, map, r.RatingGroup);
                break;
            case MealItemRule r:
                AddBox(found, map, r.YesBoxId);
                AddBox(found, map, r.ReasonId);
                break;
            case RatingRule r:
                AddLanes(found, map, r.Group);
                break;
            case StillSoreRule r:
                AddBox(found, map, r.BoxId);
                break;
            case StapleRule r:
                AddBox(found, map, r.LineId);
                break;
            case NumberRule r:
                AddBox(found, map, r.BoxId);
                break;
            default:
                throw UnknownRule(rule);
        }

        return found;
    }

    /// <summary>The ids the resolver reports this rule's answer under.</summary>
    public static IReadOnlyList<string> SubjectIds(InkRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule switch
        {
            WorkoutBoxRule r => [r.BoxId],
            ExercisePageRule r => [r.DoneBoxId, r.RatingGroup],
            MealItemRule r => [r.YesBoxId],
            RatingRule r => [r.Group],
            StillSoreRule r => [r.BoxId],
            StapleRule r => [r.LineId],
            NumberRule r => [r.BoxId],
            _ => throw UnknownRule(rule),
        };
    }

    private static void AddBox(List<InkTarget> found, InkMap map, string id)
    {
        if (map.TryGet(id) is { } target)
        {
            found.Add(target);
        }
    }

    private static void AddLanes(List<InkTarget> found, InkMap map, string group) =>
        found.AddRange(map.Targets.Where(target => target.Kind == InkTargetKind.CircleLane && string.Equals(target.Group, group, StringComparison.Ordinal)));

    private static ArgumentOutOfRangeException UnknownRule(InkRule rule) =>
        new(nameof(rule), rule, "This rule has no entry in RuleTargets; add it here and to InkResolver together.");
}
