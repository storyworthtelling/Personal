namespace Constitution.Domain.Areas;

/// <summary>
/// Which of the three pillars this install shows. Per install, not per household: switching a
/// pillar off hides its tab on this phone and nothing else (book p.17). Today and Settings are not
/// pillars and are always shown.
/// </summary>
public sealed record Pillars(bool Body, bool Workouts, bool Meals);

/// <summary>
/// Whether the household is planning meals. A fact of the household, deliberately separate from
/// <see cref="Pillars"/>: one adult switching the Meals tab off must not stop the plan for the
/// other, so nothing about a pillar toggle touches this.
/// </summary>
public sealed record HouseholdPlan(bool MealsPlanned);
