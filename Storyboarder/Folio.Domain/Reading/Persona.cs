namespace Folio.Domain.Reading;

/// <summary>What a persona is for. The two kinds are read and printed differently.</summary>
public enum PersonaKind
{
    /// <summary>One of the eight whole-book sections. Prints as a section; silent when it has nothing to say.</summary>
    Section,

    /// <summary>One of the seven readers. Gets a page of its own, in character.</summary>
    Reader,
}

/// <summary>
/// Which model reads this pass.
///
/// <para>The mix is a decision taken deliberately rather than a default inherited (finding §3,
/// reversal 2). The judgement calls — the seven readers and the two synthesis passes — go to the
/// strong model, because on those the judgement <i>is</i> the product. The eight structural sections
/// go to the standard model, which is current-generation Sonnet: both newer and cheaper than the
/// Sonnet 4.6 the repo ships today. This is not the tiering that failed for entity extraction — that
/// was the cheap tier doing extraction — and it is a setting to revisit with measurement.</para>
/// </summary>
public enum ModelTier
{
    Standard,
    Strong,
}

/// <summary>
/// One voice that reads the whole manuscript and answers one question.
///
/// <para>The phase's central bet is that a read in character removes the repetition a single
/// omnibus pass produces — eight sections asking one model for eight different things in one breath
/// gets eight paraphrases of the same paragraph. It is a hypothesis with no evidence behind it yet
/// (finding §7), which is why every persona's question is written down here where it can be changed
/// on what a real run shows.</para>
/// </summary>
public sealed record Persona(string Id, string Name, string Question, PersonaKind Kind, ModelTier Tier);
