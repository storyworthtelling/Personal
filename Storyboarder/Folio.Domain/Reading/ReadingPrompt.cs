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
            new PromptBlock(Instruction(persona), Cached: false),
        ];
    }

    /// <summary>The part of the prompt that is the same for every pass, and must stay so.</summary>
    public static string CachedPrefix(string manuscript) =>
        string.Concat(System(manuscript, PersonaCatalogue.All[0])
            .TakeWhile(block => block.Cached).Select(block => block.Text));

    public static string Instruction(Persona persona) => persona.Kind switch
    {
        PersonaKind.Reader =>
            $"You are a {persona.Name} reading the complete novel manuscript above, in character. "
            + "Nobody else's notes are available to you, and you are not summarising the book.",
        PersonaKind.Section =>
            $"You are reading the complete novel manuscript above for one thing only: {persona.Name}. "
            + "Leave every other concern to the reader whose job it is.",
        _ => throw new ArgumentOutOfRangeException(nameof(persona), persona.Kind, "Unhandled persona kind."),
    };

    /// <summary>The question this pass is asked, which is the last thing the model reads.</summary>
    public static string Question(Persona persona) => persona.Question;
}
