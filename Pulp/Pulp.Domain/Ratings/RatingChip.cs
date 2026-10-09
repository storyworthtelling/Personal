namespace Pulp.Domain.Ratings;

/// <summary>
/// The colour roles the rating chip may take, by name — never a colour (the head resolves the name
/// through the Paper palette). Closed to these four on purpose: Pulp draws no Caution (D275), so the
/// teen step takes <see cref="Reference"/> (D276) and there is no member through which Caution could
/// be handed out.
/// </summary>
public enum ChipRole
{
    Positive,
    Resolved,
    Reference,
    Problem,
}

/// <summary>
/// What the rating chip shows (D215, amended by D276): the rating's own code as its label, filled in
/// its step's role — kids positive, family resolved, teen reference, mature problem — or, unrated, a
/// hairline outline with no fill. Every step differs by label, so the chip reads without colour.
/// </summary>
public sealed record RatingChip(string Label, RatingStep? Step, ChipRole? Role, bool Outlined)
{
    /// <summary>The label an unrated title's outline carries.</summary>
    public const string UnratedLabel = "Unrated";

    public static RatingChip For(ContentRating rating) =>
        rating.Step is { } step
            ? new RatingChip(rating.Code, step, RoleOf(step), Outlined: false)
            : new RatingChip(UnratedLabel, null, null, Outlined: true);

    public static ChipRole RoleOf(RatingStep step) => step switch
    {
        RatingStep.Kids => ChipRole.Positive,
        RatingStep.Family => ChipRole.Resolved,
        RatingStep.Teen => ChipRole.Reference,
        RatingStep.Mature => ChipRole.Problem,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "A rating step with no chip role."),
    };
}
