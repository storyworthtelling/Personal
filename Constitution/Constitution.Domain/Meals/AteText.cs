using System.Globalization;

namespace Constitution.Domain.Meals;

/// <summary>
/// Every word "I ate something else" says (book p.15). Plain sentences, no exclamation, nothing voiced and nothing that quotes the
/// model; the figure and its range are numbers the reading was checked into, and the uncertainty is said in words beside them.
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

    /// <summary>What the estimate rests on and how far to trust it, in plain words, under the figure.</summary>
    public static string Detail(AteReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var range = reading.KcalLow.ToString("N0", CultureInfo.InvariantCulture) + " to " + reading.KcalHigh.ToString("N0", CultureInfo.InvariantCulture);
        var text = $"Looks like {reading.What}. A guess from one photo: anywhere from {range} kcal.";
        if (reading.ProteinLow is { } low && reading.ProteinHigh is { } high)
        {
            text += $" Protein roughly {low} to {high} g.";
        }

        return text + " " + ConfidenceWords(reading.Confidence);
    }

    public static string ConfidenceWords(AteConfidence confidence) => confidence switch
    {
        AteConfidence.Good => "A good look at the plate, though portions are still a guess.",
        AteConfidence.Fair => "A fair guess; portions are hard to judge from a photo.",
        _ => "Not very sure; the dish and the portion are hard to judge from this photo.",
    };

    public static string Sentence(AteOutcome outcome) => outcome switch
    {
        AteOutcome.NoMeal => NoMealSentence,
        AteOutcome.Unavailable unavailable => unavailable.Sentence,
        _ => UnreadableSentence,
    };
}
