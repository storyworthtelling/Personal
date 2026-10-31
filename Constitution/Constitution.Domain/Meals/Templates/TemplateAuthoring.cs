using System.Text.Json;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Meals.Templates;

/// <summary>A template as the author is told of it: its slot and its words. No id, no person.</summary>
public sealed record TemplateKnown(MealTemplateSlot Slot, string Words);

/// <summary>A lasting taste as a word: a cuisine or ingredient, liked or not. Never whose it is.</summary>
public sealed record TemplateTasteHint(string Kind, string Subject, string Stance);

/// <summary>
/// All the author is ever shown: how many to propose, for which slots, the approved, rejected and still-waiting words, and the lasting tastes as
/// words (D98: no names, no readings of the body). A reflection test holds the member types to text, numbers, slots and these lists.
/// </summary>
public sealed record TemplateRequest(
    int Count,
    IReadOnlyList<MealTemplateSlot> Wanted,
    IReadOnlyList<TemplateKnown> Approved,
    IReadOnlyList<TemplateKnown> Rejected,
    IReadOnlyList<TemplateKnown> Pending,
    IReadOnlyList<TemplateTasteHint> Tastes)
{
    public const int MaxCount = 6;

    /// <summary>
    /// The request for <paramref name="count"/> (1 to <see cref="MaxCount"/>) new templates in the wanted slots. The rejected are told as words only:
    /// the household's written reasons are theirs, may name anyone, and reach the taste judge instead. Dish notes are left out (their subjects are ids).
    /// </summary>
    public static TemplateRequest From(TemplateLibrary library, IReadOnlyList<TasteNote> notes, IReadOnlyList<MealTemplateSlot> wanted, int count)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(wanted);
        var hints = notes
            .Where(note => note.Kind is TasteSubjectKind.Cuisine or TasteSubjectKind.Ingredient)
            .Select(note => new TemplateTasteHint(TasteNoteWords.Kinds.Word(note.Kind), note.Subject, TasteNoteWords.Stances.Word(note.Stance)))
            .Distinct()
            .OrderBy(hint => hint.Kind, StringComparer.Ordinal)
            .ThenBy(hint => hint.Subject, StringComparer.Ordinal)
            .ThenBy(hint => hint.Stance, StringComparer.Ordinal)
            .ToList();
        return new TemplateRequest(
            Math.Clamp(count, 1, MaxCount),
            wanted.Distinct().ToList(),
            Known(library.Approved),
            Known(library.Rejected),
            Known(library.Proposed),
            hints);
    }

    private static List<TemplateKnown> Known(IEnumerable<MealTemplate> templates) =>
        templates.Select(template => new TemplateKnown(template.Slot, template.Words)).ToList();
}

/// <summary>One proposed line, before it has an id or a status.</summary>
public sealed record TemplateDraft(MealTemplateSlot Slot, string Words);

/// <summary>What the author came back with.</summary>
public abstract record TemplateAuthored
{
    public sealed record Drafts(IReadOnlyList<TemplateDraft> Proposals) : TemplateAuthored;

    public sealed record Unavailable(AuthorUnavailable Reason) : TemplateAuthored;
}

/// <summary>The seam where Claude proposes new templates. An implementation lives in Infrastructure and turns every failure it can name into <see cref="TemplateAuthored.Unavailable"/>.</summary>
public interface ITemplateAuthor
{
    Task<TemplateAuthored> ProposeAsync(TemplateRequest request, CancellationToken cancellation);
}

/// <summary>The author when none is set up: always unavailable, so nothing is ever proposed.</summary>
public sealed class NoTemplateAuthor : ITemplateAuthor
{
    public Task<TemplateAuthored> ProposeAsync(TemplateRequest request, CancellationToken cancellation) =>
        Task.FromResult<TemplateAuthored>(new TemplateAuthored.Unavailable(AuthorUnavailable.NotConfigured));
}

/// <summary>Reads an author's reply into drafts. Tolerant and bounded: anything unusable proposes nothing.</summary>
public static class TemplateProposalParser
{
    public const int MaxReplyLength = 20000;

    public static IReadOnlyList<TemplateDraft> Parse(string? reply, TemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(reply) || reply.Length > MaxReplyLength)
        {
            return [];
        }

        var first = reply.IndexOf('[', StringComparison.Ordinal);
        var last = reply.LastIndexOf(']');
        if (first < 0 || last <= first)
        {
            return [];
        }

        var known = request.Approved.Concat(request.Rejected).Concat(request.Pending).Select(item => MealTemplate.Key(item.Words)).ToHashSet(StringComparer.Ordinal);
        var drafts = new List<TemplateDraft>();
        try
        {
            using var document = JsonDocument.Parse(reply[first..(last + 1)], new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (drafts.Count >= request.Count)
                {
                    break;
                }

                if (DraftOf(item, request) is { } draft && known.Add(MealTemplate.Key(draft.Words)))
                {
                    drafts.Add(draft);
                }
            }
        }
        catch (JsonException)
        {
            // A reply that is not a list of proposals proposes nothing; the weekly sheet simply prints no new line.
            return [];
        }

        return drafts;
    }

    private static TemplateDraft? DraftOf(JsonElement item, TemplateRequest request)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("slot", out var slotElement) || slotElement.ValueKind != JsonValueKind.String
            || !item.TryGetProperty("words", out var wordsElement) || wordsElement.ValueKind != JsonValueKind.String
            || !MealTemplateWords.Slots.TryValue(slotElement.GetString() ?? string.Empty, out var slot)
            || !request.Wanted.Contains(slot))
        {
            return null;
        }

        var words = MealTemplate.Normalize(wordsElement.GetString());
        return words.Length is > 0 and <= MealTemplate.MaxWordsLength && MealTemplate.Prints(words) ? new TemplateDraft(slot, words) : null;
    }
}
