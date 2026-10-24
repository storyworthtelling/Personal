namespace Constitution.Domain.Household;

/// <summary>
/// The kind half of every household record's key (C-6.1.2). <b>Frozen once shipped</b>: the file outlives
/// every build that reads it, so a renamed kind is a reader that finds nothing while the old records sit
/// unread in Drive for good. Every value is a literal, never derived from an identifier, which would
/// rename the kind with it. <c>Constitution.Tests/Household/ConstitutionKindsTests</c> holds each value;
/// growing the set is a deliberate edit to that list.
///
/// <para><see cref="All"/> is also the <b>reserved set</b>: a kind named here is Constitution's, whether or
/// not this build has a typed map for it yet (the library keeps a record of a kind nobody maps, and writes
/// it back untouched), so no later feature may mean something else by one of these words.</para>
/// </summary>
public static class ConstitutionKinds
{
    /// <summary>A recipe card in the box.</summary>
    public const string Recipe = "recipe";

    /// <summary>One week's meal plan.</summary>
    public const string WeekPlan = "week-plan";

    /// <summary>A person's rating of a recipe.</summary>
    public const string Rating = "rating";

    /// <summary>What the household has learned about its own taste.</summary>
    public const string TasteNote = "taste-note";

    /// <summary>A pantry staple the household always has.</summary>
    public const string Staple = "staple";

    /// <summary>One person on the household roster (<see cref="RosterPerson"/>).</summary>
    public const string Person = "person";

    /// <summary>Which install holds the right to plan the week, and until when.</summary>
    public const string PlanningLease = "planning-lease";

    /// <summary>Every reserved kind, for the guard that holds them frozen.</summary>
    public static IReadOnlyList<string> All { get; } = [Recipe, WeekPlan, Rating, TasteNote, Staple, Person, PlanningLease];

    /// <summary>Whether <paramref name="kind"/> is one of Constitution's (record keys are compared without case).</summary>
    public static bool IsReserved(string kind) => All.Contains(kind, StringComparer.OrdinalIgnoreCase);
}
