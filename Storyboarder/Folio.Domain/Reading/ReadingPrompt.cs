using Folio.Domain.Text;

namespace Folio.Domain.Reading;

/// <summary>One block of a request's system prompt. <paramref name="Cached"/> marks the cache breakpoint.</summary>
public sealed record PromptBlock(string Text, bool Cached);

/// <summary>
/// How a pass is addressed to the model — and, specifically, <b>in what order</b>.
///
/// <para><b>The manuscript comes first and the persona after it.</b> That ordering is the whole
/// reason this file exists, and it is not a style choice: prompt caching matches an exact
/// <i>prefix</i>, so a persona named ahead of the manuscript makes every one of the seventeen passes
/// a different prefix, and the manuscript behind it is cached seventeen times over instead of once.
/// Measured: writing the persona first produced <c>cache_creation_input_tokens: 167187</c> and
/// <c>cache_read: 0</c> on every pass, which on the strong model is roughly $17 a draft against
/// $2.</para>
///
/// <para><b>Nothing about that failure is visible.</b> Every request succeeds, every reply is
/// correct, every test passes; the only symptom is the bill. That is why the invariant is expressed
/// as a function with a guard over it rather than as a comment next to the call site: the cached
/// prefix must be <i>byte-identical whichever persona is reading</i>, and
/// <c>ReadingPromptTests</c> is what fails when it stops being.</para>
/// </summary>
public static class ReadingPrompt
{
    /// <summary>
    /// The system blocks for one pass. Block one is the manuscript and carries the cache breakpoint;
    /// everything persona-specific sits after it, where changing it costs nothing.
    /// </summary>
    public static IReadOnlyList<PromptBlock> System(string manuscript, Persona persona)
    {
        ArgumentNullException.ThrowIfNull(persona);

        return
        [
            new PromptBlock("MANUSCRIPT\n\n" + manuscript, Cached: true),
            new PromptBlock(Instruction(persona, ManuscriptLength.Classify(manuscript)), Cached: false),
        ];
    }

    /// <summary>The part of the prompt that is the same for every pass, and must stay so.</summary>
    public static string CachedPrefix(string manuscript) =>
        string.Concat(System(manuscript, PersonaCatalogue.All[0])
            .TakeWhile(block => block.Cached).Select(block => block.Text));

    /// <summary>
    /// Told after the cache breakpoint, so it costs nothing to vary — and it does vary: a manuscript is
    /// called a short story, novella, or novel by what it actually is, not by what the app assumes every
    /// manuscript is. A one-chapter, 1,200-word short story told it is "the complete novel manuscript" is
    /// asked, in effect, to find a theme hierarchy and a chapter-by-chapter pacing curve in a piece with
    /// one chapter and no room for either — and it answers anyway, which is where a report's Themes and
    /// Pacing sections stop being about the book in front of it.
    /// </summary>
    public static string Instruction(Persona persona, StoryLength length)
    {
        var noun = ManuscriptLength.Noun(length);
        return persona.Kind switch
        {
            PersonaKind.Reader =>
                $"You are a {persona.Name} reading the complete {noun} manuscript above, in character. "
                + "Nobody else's notes are available to you, and you are not summarising the book.",
            PersonaKind.Section =>
                $"You are reading the complete {noun} manuscript above for one thing only: {persona.Name}. "
                + "Leave every other concern to the reader whose job it is.",
            _ => throw new ArgumentOutOfRangeException(nameof(persona), persona.Kind, "Unhandled persona kind."),
        };
    }

    /// <summary>The question this pass is asked, which is the last thing the model reads.</summary>
    public static string Question(Persona persona) => persona.Question;
}
