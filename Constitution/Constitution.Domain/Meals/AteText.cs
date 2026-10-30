using System.Globalization;

namespace Constitution.Domain.Meals;

/// <summary>
/// Every word "I ate something else" says (book p.15). Plain sentences, no exclamation, nothing voiced and nothing that quotes the
/// model; the figure is the only number drawn; the range and the confidence are kept in the record and not drawn (D107).
/// </summary>
public static class AteText
{
    public const string ResultLead = "You had something else · about ";

    public const string NoKeySentence = "Estimating needs the Claude key from the vault, which is not on this phone yet, so nothing was recorded.";

    public const string UnreachableSentence = "The estimate could not be reached just now, so nothing was recorded.";

    public const string NoMealSentence = "No meal could be seen in that photo, so nothing was recorded.";

    public const string UnreadableSentence = "That photo could not be turned into an estimate, so nothing was recorded.";

    public const string CaptureFailedSentence = "The camera did not give back a photo, so nothing was recorded.";

    public const string AskingSentence =
        "Asking to use the camera, only to take one photo of your meal. The photo is deleted straight after the estimate.";

    public const string DeniedSentence = "The camera was not allowed, so a meal can't be photographed.";

    public const string NoCameraSentence = "This phone has no camera, so a meal can't be photographed.";

    public static string Estimating(AteMeal meal) => $"Estimating your {AteMeals.Word(meal)}…";

    public static string Figure(int kcal) => kcal.ToString("N0", CultureInfo.InvariantCulture) + " kcal";

    /// <summary>The one sentence a breakfast or lunch photo gets, in place of a card (D107).</summary>
    public static string Noted(AteMeal meal) => $"Noted for {AteMeals.Word(meal)}.";

    public static string Sentence(AteOutcome outcome) => outcome switch
    {
        AteOutcome.NoMeal => NoMealSentence,
        AteOutcome.Unavailable unavailable => unavailable.Sentence,
        _ => UnreadableSentence,
    };
}
