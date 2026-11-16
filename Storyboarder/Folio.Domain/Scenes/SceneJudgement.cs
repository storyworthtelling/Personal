namespace Folio.Domain.Scenes;

/// <summary>What to do with a scene, in one word (owner's ruling, R4).</summary>
public enum SceneCall
{
    Keep,
    Tighten,
    Rewrite,
    Cut,
}

/// <summary>The four kinds of inconsistency a scene's judgement names (owner's ruling, R4).</summary>
public enum SceneInconsistencyKind
{
    Voice,
    Tense,
    Characterisation,
    Setting,
}

/// <summary>
/// One place a scene is inconsistent with itself or the book. <see cref="Excerpt"/> is a short verbatim
/// quote, so the note is also written at its line (R5); empty when the model gave none.
/// </summary>
public sealed record SceneInconsistency(SceneInconsistencyKind Kind, string Detail, string Excerpt);

/// <summary>
/// One detected scene, judged on its own (release A3–A4). Keyed by the scene's own number, never by its
/// position in a list, because a list made separately from the detected scenes and zipped against them
/// printed every verdict after the first difference under the wrong scene.
/// </summary>
public sealed record SceneJudgement(
    int SceneNumber,
    string Chapter,
    string Accomplishes,
    SceneCall Call,
    double Tension,
    string Verdict,
    IReadOnlyList<SceneInconsistency> Inconsistencies);

/// <summary>
/// What a scene is judged from: its own text, the story so far when it begins, and the book's dominant
/// voice (for voice drift). The manuscript as a whole is not sent; the scene is the unit.
/// </summary>
public sealed record SceneToJudge(int Number, string Chapter, string Text, string StorySoFar, string DominantVoice);

/// <summary>A text block the chapter-notes read covered, and the story so far at its end.</summary>
public sealed record TextBlockState(int StartOffset, int EndOffset, string RunningState);

public static class SceneContext
{
    /// <summary>
    /// The story so far when a scene begins: the running state of the last block that ended at or before
    /// the scene's start. Empty for a scene in the first block, which has nothing before it.
    /// </summary>
    public static string StorySoFar(IEnumerable<TextBlockState> blocks, int sceneStart) =>
        blocks.Where(block => block.EndOffset <= sceneStart)
            .OrderBy(block => block.EndOffset)
            .LastOrDefault()?.RunningState ?? string.Empty;
}

/// <summary>The scene block's words, one copy for the report and anything else that says them.</summary>
public static class SceneWords
{
    public static string Call(SceneCall call) => call switch
    {
        SceneCall.Keep => "Keep",
        SceneCall.Tighten => "Tighten",
        SceneCall.Rewrite => "Rewrite",
        SceneCall.Cut => "Cut",
        _ => throw new ArgumentOutOfRangeException(nameof(call), call, "Unhandled scene call."),
    };

    /// <summary>Tension in three bands on the pacing curve's 0–1 scale.</summary>
    public static string Tension(double tension) =>
        "Tension: " + (tension < 1.0 / 3 ? "low" : tension < 2.0 / 3 ? "medium" : "high");

    public static string Kind(SceneInconsistencyKind kind) => kind switch
    {
        SceneInconsistencyKind.Voice => "Voice",
        SceneInconsistencyKind.Tense => "Tense",
        SceneInconsistencyKind.Characterisation => "Characterisation",
        SceneInconsistencyKind.Setting => "Setting",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled inconsistency kind."),
    };
}
