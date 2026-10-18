using Constitution.Domain.Documents;
using Constitution.Domain.Ink;

namespace Constitution.Tests.Ink;

/// <summary>
/// One invented page that carries every kind of target the resolver reads, built through
/// <see cref="InkTargets"/> so the map is the one the painter would draw from.
/// </summary>
internal sealed class InkFixture
{
    public static readonly DateTimeOffset Deadline = new(2026, 3, 14, 17, 0, 0, TimeSpan.Zero);

    public InkFixture()
    {
        var lanes = InkTargets.CircleLanes("feel", 40, 300, ["easy", "right", "hard"]);
        Lanes = lanes.Lanes;
        var meal = InkTargets.TickRows("dinner", 40, 100, ["Lentil soup"])[0];
        var targets = new List<InkTarget>
        {
            InkTargets.LargeTick("pushups", 40, 40, "push-ups"),
            InkTargets.LargeTick("done", 40, 200, "finished"),
            InkTargets.LargeTick("sore", 40, 250, "still sore"),
            meal,
            InkTargets.NoBecause("dinner-no", meal, 400, 200),
            InkTargets.NumberBox("reach", 40, 400, "toe reach", "inches"),
            new InkTarget("oil", InkTargetKind.StandardTick, new InkRect(40, 460, 18, 18), "olive oil"),
        };
        targets.AddRange(lanes.Lanes);
        Readout = new InkReadout(new InkMap(targets));
        Resolver = new InkResolver(Readout);
    }

    public IReadOnlyList<InkTarget> Lanes { get; }

    public InkReadout Readout { get; }

    public InkResolver Resolver { get; }

    public static InkRule WorkoutBox => new WorkoutBoxRule("push-ups", "pushups");
    public static InkRule ExercisePage => new ExercisePageRule("push-ups", "done", "feel");
    public static InkRule Meal => new MealItemRule("lentil soup", "dinner-1", "dinner-no", Deadline);
    public static InkRule Rating => new RatingRule("push-ups", "feel");
    public static InkRule StillSore => new StillSoreRule("the sore knee", "sore");
    public static InkRule Staple => new StapleRule("olive oil", "oil");
    public static InkRule Number => new NumberRule("toe reach", "reach");

    public const double Sure = 0.95;

    /// <summary>A confident read of an empty target.</summary>
    public static InkTargetRead Empty(string id) => new(id, false, Sure);

    /// <summary>A confident read of a marked target.</summary>
    public static InkTargetRead Ink(string id, string? text = null) => new(id, true, Sure, text);

    /// <summary>Every lane of the feel group, confidently empty, but for the named ones.</summary>
    public static IEnumerable<InkTargetRead> Feel(params string[] circled) =>
        new[] { "feel-1", "feel-2", "feel-3" }.Select(id => circled.Contains(id) ? Ink(id) : Empty(id));

    public static InkPageRead Page(params InkTargetRead[] reads) => InkPageRead.Read(reads);

    public static InkPageRead Page(IEnumerable<InkTargetRead> reads, params InkTargetRead[] more) =>
        InkPageRead.Read(reads.Concat(more));

    public IReadOnlyList<InkResolution> Resolve(InkPageRead page, InkRule rule, DateTimeOffset? now = null) =>
        Resolver.Resolve(page, [rule], now ?? Deadline.AddHours(-9));

    public InkResolution One(InkPageRead page, InkRule rule, DateTimeOffset? now = null) =>
        Assert.Single(Resolve(page, rule, now));
}
