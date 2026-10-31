using System.Text;
using System.Text.Json;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Meals;

/// <summary>
/// The fixed words the judge is given: an instruction with no digits and no names, and the one user message built from a dish name and a written reason. Nothing
/// else about the household ever reaches the judge: no person, no date, no profile, no other evidence.
/// </summary>
public static class TasteJudgePrompt
{
    /// <summary>The instruction. Built once from the cuisine words the box knows, so a cuisine the household file can hold is one the judge may name.</summary>
    public static string System { get; } =
        "You read one short sentence that a member of a household wrote about a dish, or about a line of their meal sheet, and decide whether it states "
        + "a lasting lesson about their taste or only a remark about one evening. You are told the dish name (or none) and the written reason. "
        + "A lasting lesson is a cuisine, an ingredient or a whole dish they will not want again or really like. Being tired, a missing item, the "
        + "timing, the weather or a mood is a remark about one evening and teaches nothing. Use only these kinds: cuisine, whose subject is exactly "
        + "one of " + string.Join(", ", Cuisines.All.Select(Cuisines.Words.Word)) + "; ingredient, whose subject is one or two plain lower-case food words; "
        + "dish, for the dish you were told, with no subject. The stance is likes or dislikes. Write at most three lessons and never one the words do "
        + "not state. Reply with strict JSON and nothing else, in exactly one of these shapes: "
        + "{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"<cuisine, ingredient or dish>\",\"subject\":\"<subject>\",\"stance\":\"<likes or dislikes>\"}]} "
        + "or {\"verdict\":\"oneoff\"}.";

    /// <summary>The dish and the words, each on one line and cut to a bound: a stray newline cannot start a new instruction.</summary>
    public static string User(string? dish, string words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var named = OneLine(dish, Recipe.MaxTitleLength);
        return "Dish: " + (named.Length == 0 ? "none" : named) + "\nReason: " + OneLine(words, TasteEvidence.MaxWordsLength);
    }

    private static string OneLine(string? value, int longest)
    {
        var line = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length > longest ? line[..longest] : line;
    }
}

/// <summary>
/// Reads the judge's reply, strictly. The only shapes that mean anything are the two the instruction asks for; anything else (prose, a refusal, a verdict it
/// does not know, a kind or stance outside the closed lists, a lesson without a subject, more than three lessons, a subject too long to be a note, a reply over the
/// limit) is <see cref="TasteJudgement.Unavailable"/> with <see cref="AuthorUnavailable.ReplyUnusable"/> and a fixed sentence that quotes nothing from the reply. A
/// dish lesson is always about the evidence's own card; it is never read from the reply.
/// </summary>
public static class TasteJudgeReply
{
    public const int LongestReply = 2000;

    public static TasteJudgement Parse(string? text, RecipeId? recipe)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Unusable("The judge's reply was empty.");
        }

        if (text.Length > LongestReply)
        {
            return Unusable("The judge's reply was longer than a judgement can be.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(WithoutFence(text));
        }
        catch (JsonException)
        {
            // Nothing from the exception is kept: its text can quote the reply.
            return Unusable("The judge's reply was not the JSON that was asked for.");
        }

        using (document)
        {
            return FromRoot(document.RootElement, recipe);
        }
    }

    private static TasteJudgement FromRoot(JsonElement root, RecipeId? recipe)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("verdict", out var verdict) || verdict.ValueKind != JsonValueKind.String)
        {
            return Unusable("The judge's reply had no verdict.");
        }

        var lessons = root.TryGetProperty("lessons", out var listed) ? listed : (JsonElement?)null;
        if (lessons is { ValueKind: not JsonValueKind.Array and not JsonValueKind.Null })
        {
            return Unusable("The judge's reply listed its lessons in a shape that was not asked for.");
        }

        var items = lessons is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().ToList() : [];
        switch (verdict.GetString())
        {
            case "oneoff":
                return items.Count == 0 ? new TasteJudgement.OneOff() : Unusable("The judge called the words a one-off and still listed lessons.");
            case "lasting":
                return LastingFrom(items, recipe);
            default:
                return Unusable("The judge's verdict was not one that was asked for.");
        }
    }

    private static TasteJudgement LastingFrom(List<JsonElement> items, RecipeId? recipe)
    {
        if (items.Count is 0 or > TasteLearner.MaxLessonsPerEvidence)
        {
            return Unusable("The judge's lasting verdict had no lesson or too many.");
        }

        var lessons = new List<TasteLesson>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object
                || Word(item, "kind") is not { } kindWord || !TasteNoteWords.Kinds.TryValue(kindWord, out var kind)
                || Word(item, "stance") is not { } stanceWord || !TasteNoteWords.Stances.TryValue(stanceWord, out var stance))
            {
                return Unusable("A lesson had a kind or a stance that was not asked for.");
            }

            if (kind == TasteSubjectKind.Dish)
            {
                if (recipe is not { } card)
                {
                    return Unusable("A lesson was about a dish when the words were about none.");
                }

                lessons.Add(new TasteLesson(kind, card.Value, stance));
                continue;
            }

            if (Word(item, "subject")?.Trim() is not { Length: > 0 and <= TasteNote.MaxSubjectLength } subject)
            {
                return Unusable("A lesson had no subject, or one too long to keep.");
            }

            lessons.Add(new TasteLesson(kind, subject, stance));
        }

        return new TasteJudgement.Lasting(lessons);
    }

    private static string? Word(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string WithoutFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal) || trimmed.Length < 6)
        {
            return trimmed;
        }

        var inner = trimmed[3..^3];
        var newline = inner.IndexOf('\n');
        return (newline >= 0 && inner[..newline].All(char.IsLetter) ? inner[(newline + 1)..] : inner).Trim();
    }

    private static TasteJudgement Unusable(string detail) => new TasteJudgement.Unavailable(AuthorUnavailable.ReplyUnusable, detail);
}
