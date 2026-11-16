using System.Globalization;
using Folio.Domain.Text;

namespace Folio.Domain.Reading;

/// <summary>One block of a request's system prompt. <paramref name="Cached"/> marks the cache breakpoint.</summary>
public sealed record PromptBlock(string Text, bool Cached);

/// <summary>
/// How a pass is addressed to the model — and, specifically, <b>in what order</b>.
///
/// <para><b>The manuscript comes first and the persona after it.</b> That ordering is the whole
/// reason this file exists, and it is not a style choice: prompt caching matches an exact
/// <i>prefix</i>, so a persona named ahead of the manuscript makes every one of the fourteen passes
/// a different prefix, and the manuscript behind it is cached fourteen times over instead of once per model.
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
    public static IReadOnlyList<PromptBlock> System(string manuscript, Persona persona, ReadingFrame frame)
    {
        ArgumentNullException.ThrowIfNull(persona);
        ArgumentNullException.ThrowIfNull(frame);

        // A series book's canon (R27) is the same for every pass of the draft, so it is cached with the
        // manuscript, after it: the prefix stays byte-identical whichever persona is reading.
        var instruction = new PromptBlock(
            Instruction(persona, ManuscriptLength.CountWords(manuscript), frame.Genre, frame.HasSeriesCanon), Cached: false);
        return frame.HasSeriesCanon
            ?
            [
                new PromptBlock("MANUSCRIPT\n\n" + manuscript, Cached: true),
                new PromptBlock(frame.SeriesCanon, Cached: true),
                instruction,
            ]
            : [new PromptBlock("MANUSCRIPT\n\n" + manuscript, Cached: true), instruction];
    }

    /// <summary>The part of the prompt that is the same for every pass, and must stay so.</summary>
    public static string CachedPrefix(string manuscript) =>
        string.Concat(System(manuscript, PersonaCatalogue.All[0], GenreFrame.Unknown)
            .TakeWhile(block => block.Cached).Select(block => block.Text));

    /// <summary>
    /// Told after the cache breakpoint, so it costs nothing to vary — and it does vary: every pass is
    /// told the manuscript's word count and called a short story, novella, or novel by what it actually
    /// is. A one-chapter, 1,200-word short story told it is "the complete novel manuscript" is asked, in
    /// effect, to find a theme hierarchy and a chapter-by-chapter pacing curve in a piece with one
    /// chapter and no room for either — and it answers anyway. Told only the noun, it still went
    /// looking for novel-scale structure and counted the story's length against it, so a short story
    /// is told outright to be read as one.
    /// </summary>
    public static string Instruction(Persona persona, int wordCount, GenreFrame genre, bool seriesCanon = false)
    {
        ArgumentNullException.ThrowIfNull(persona);
        ArgumentNullException.ThrowIfNull(genre);

        var length = ManuscriptLength.Classify(wordCount);
        var noun = ManuscriptLength.Noun(length);
        var words = $"It is {wordCount.ToString("N0", CultureInfo.InvariantCulture)} words long. ";
        var scale = length switch
        {
            StoryLength.ShortStory =>
                "Read it as a short story: do not look for novel-scale structure (chapters, acts, subplots, a "
                + "cast to map, a hierarchy of themes), do not count its length or its form against it, and judge "
                + "it by what a short story of this length can do. ",
            StoryLength.Novella =>
                "Read it as a novella: judge it by what a novella can do, not by what a novel would. ",
            _ => string.Empty,
        };
        var brief = length == StoryLength.ShortStory && persona.ShortStoryBrief is { } own
            ? own + " "
            : persona.Brief is { } general ? general + " " : string.Empty;
        var purpose = length == StoryLength.Novel && persona.NovelPurpose is { } more ? more + " " : string.Empty;
        var genreLine = GenreLine(genre, length);
        var fan = persona.FanOfTheGenre ? FanBrief(genre) : string.Empty;
        var score = ScoreBrief(persona, length);
        var series = seriesCanon
            ? "The series bible above is the canon of the earlier book in this series; weigh this book against it where it bears on your question. "
            : string.Empty;

        return persona.Kind switch
        {
            PersonaKind.Reader =>
                $"You are {persona.ReaderName ?? persona.Name}, the {persona.Name.ToLowerInvariant()}, reading the complete {noun} manuscript above, in character. "
                + (persona.Voice is { } voice ? $"Your voice: {voice}. Let it shape how you write, never what you judge. " : string.Empty)
                + words + genreLine + series + scale + fan + brief + purpose + NoLineEdits + score
                + "Nobody else's notes are available to you, and you are not summarising the book.",
            PersonaKind.Section =>
                $"You are reading the complete {noun} manuscript above for one thing only: {persona.Name}. "
                + words + genreLine + series + scale + fan + brief
                + "Leave every other concern to the reader whose job it is.",
            _ => throw new ArgumentOutOfRangeException(nameof(persona), persona.Kind, "Unhandled persona kind."),
        };
    }

    /// <summary>
    /// The genre as the consolidation read it from the text, and the length its market expects, so a
    /// judgement of length (trim, expand, split into volumes) is made against the right norm.
    /// </summary>
    private static string GenreLine(GenreFrame genre, StoryLength length)
    {
        if (!genre.IsKnown)
            return string.Empty;

        // The range is the genre's book length; against a short story it is a number to fall short of.
        if (length == StoryLength.ShortStory)
            return $"Read from the text, its genre is {genre.Genre.Trim()}. ";

        var range = string.IsNullOrWhiteSpace(genre.WordCountRange)
            ? string.Empty
            : $", where publishers usually expect {genre.WordCountRange.Trim()} words";
        return $"Read from the text, its genre is {genre.Genre.Trim()}{range}; weigh its length and its "
            + "story against what that genre's readers expect. ";
    }

    /// <summary>
    /// The genre fan is a fan of THIS book's genre — the owner's intent. Called only "Genre fan", the
    /// model took the word the way the trade uses it and read as a fan of science fiction, fantasy and
    /// horror whatever the book was, so the genre is named and the reading is ruled out in words.
    /// </summary>
    private static string FanBrief(GenreFrame genre)
    {
        var name = genre.IsKnown ? genre.Genre.Trim() : "this book's own genre";
        return $"You are a devoted reader of {name}, not science fiction, fantasy or horror unless that is "
            + "what this book is. You picked this book up because it is the kind you love, so you "
            + $"want to like it; but you have read a great deal of {(genre.IsKnown ? name : "it")} and you are "
            + "particular about how it is done: you know its conventions and its tropes, what has been done "
            + "well before, and where this one is fresh, where it is tired, and where it lets a fan down. ";
    }

    /// <summary>
    /// The owner's ruling: each reader scores the manuscript from 1 to 10 on its own question, against
    /// its own written anchors, and says in a sentence why. A section has no anchors and is not asked.
    /// </summary>
    private static string ScoreBrief(Persona persona, StoryLength length)
    {
        var anchors = persona.AnchorsFor(length);
        if (anchors.Count == 0)
            return string.Empty;

        var points = string.Join("; ", anchors.Select((anchor, i) => $"{(i + 1) * 2}: {anchor}"));
        return $"Score the {ManuscriptLength.Noun(length)} from 1 to 10 on your own question, in your own terms, "
            + $"against these anchors — {points}. The odd numbers fall between them. Use the whole scale, "
            + "and give one sentence saying why the score is that number. ";
    }

    // The owner's ruling (R11): readers make no line edits, even where the line work is costing the score.
    // The marked manuscript already carries the line-level notes; general terms and real blockers stay.
    private const string NoLineEdits =
        "Make no line edits: do not comment on word choice, grammar, punctuation, typos or single sentences. "
        + "You may say in general terms that the prose needs work (\"the prose is sloppy\"), and name a "
        + "line-level problem only when it blocks your answer (\"tense shifts that a line editor should catch\"). ";

    /// <summary>The question this pass is asked, which is the last thing the model reads.</summary>
    public static string Question(Persona persona, StoryLength length) => persona.QuestionFor(length);
}
