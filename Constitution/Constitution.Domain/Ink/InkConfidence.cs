namespace Constitution.Domain.Ink;

/// <summary>
/// A confidence is a probability. NaN is refused by name because every comparison with it is false,
/// which would let a "below 0.75" test pass it as sure and quietly guess.
/// </summary>
internal static class InkConfidence
{
    internal static double Check(double confidence, string name)
    {
        if (double.IsNaN(confidence) || confidence < 0d || confidence > 1d)
        {
            throw new ArgumentOutOfRangeException(
                name, confidence, "A read's confidence is a number from 0 to 1; anything else cannot be compared with the 0.75 floor.");
        }

        return confidence;
    }
}
