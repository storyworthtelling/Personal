using System.Text.Json;

namespace Constitution.Domain.Meals;

/// <summary>
/// The model's reply, read strictly. It is tolerant only about wrapping (a code fence, a sentence before or after) and strict about
/// everything inside: a reply with no usable energy range is <see cref="AteOutcome.Unreadable"/>, never a figure and never zero.
/// A protein range that is not plausible is dropped and the energy kept; an unknown confidence is the cautious one.
/// </summary>
public static class AteReply
{
    public static AteOutcome Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AteOutcome.Unreadable();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return new AteOutcome.Unreadable();
        }

        try
        {
            using var parsed = JsonDocument.Parse(text[start..(end + 1)]);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new AteOutcome.Unreadable();
            }

            if (root.TryGetProperty("is_meal", out var isMeal) && isMeal.ValueKind == JsonValueKind.False)
            {
                return new AteOutcome.NoMeal();
            }

            if (!Whole(root, "kcal_low", out var kcalLow) || !Whole(root, "kcal_high", out var kcalHigh)
                || !root.TryGetProperty("food", out var food) || food.ValueKind != JsonValueKind.String)
            {
                return new AteOutcome.Unreadable();
            }

            var hasProtein = Whole(root, "protein_g_low", out var proteinLow) & Whole(root, "protein_g_high", out var proteinHigh);
            return AteReading.TryCreate(
                food.GetString(),
                kcalLow,
                kcalHigh,
                hasProtein ? proteinLow : null,
                hasProtein ? proteinHigh : null,
                ConfidenceOf(root),
                out var reading)
                ? new AteOutcome.Read(reading!)
                : new AteOutcome.Unreadable();
        }
        catch (JsonException)
        {
            // A reply that is not JSON holds no estimate; it is "could not estimate", and nothing of it is kept.
            return new AteOutcome.Unreadable();
        }
    }

    private static bool Whole(JsonElement root, string name, out long value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var number)
            || double.IsNaN(number) || Math.Abs(number) > 1_000_000)
        {
            return false;
        }

        value = (long)Math.Round(number, MidpointRounding.AwayFromZero);
        return true;
    }

    private static AteConfidence ConfidenceOf(JsonElement root)
    {
        if (root.TryGetProperty("confidence", out var element) && element.ValueKind == JsonValueKind.String)
        {
            switch (element.GetString()?.Trim().ToLowerInvariant())
            {
                case "good":
                    return AteConfidence.Good;
                case "fair":
                    return AteConfidence.Fair;
            }
        }

        return AteConfidence.Low;
    }
}
