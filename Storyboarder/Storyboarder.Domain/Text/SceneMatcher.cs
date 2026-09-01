namespace Storyboarder.Domain.Text;

// One scene as text, for cross-draft matching. Number is the scene's 1-based reading order within its
// draft; Text is its source prose.
public sealed record SceneText(int Number, string Text);

// The outcome of matching one of this draft's scenes to the previous draft. PreviousNumber is the reading
// order of the scene it matched in the previous draft, or null when it is new this draft (no counterpart).
// Changed says whether a matched scene's text moved: false → the previous render can be reused as-is;
// true → render fresh and show a before/after pair. A new scene (PreviousNumber null) is always rendered
// and carries Changed = false (there is no prior text it changed from).
public sealed record SceneMatch(int CurrentNumber, int? PreviousNumber, bool Changed);

// Matches this draft's scenes against the previous draft's, purely from their text — scenes are re-detected
// per draft and renumber under edits, so matching is content-based, never by scene number or offset. Two
// passes: exact (normalized) text matches first, so unchanged scenes pair regardless of how they shifted;
// then the remaining scenes are paired to the most similar leftover previous scene above a similarity
// threshold (a reworded-but-same beat), and anything still unmatched is a new scene. Deterministic and
// dependency-free, so it is unit-tested without a model call.
public sealed class SceneMatcher
{
    // A reworded scene must still share at least this fraction of its distinct content words (Jaccard over
    // token sets) to be considered the same beat; below it, the scene is treated as new rather than forcing
    // a weak pairing that would mislead the before/after diff.
    private const double SimilarityThreshold = 0.5;

    public IReadOnlyList<SceneMatch> Match(
        IReadOnlyList<SceneText> previous,
        IReadOnlyList<SceneText> current)
    {
        var previousNorm = previous.Select(p => Normalize(p.Text)).ToArray();
        var previousTokens = previous.Select(p => Tokenize(p.Text)).ToArray();
        var used = new bool[previous.Count];
        var matches = new SceneMatch[current.Count];

        // Pass 1 — exact normalized-text matches (unchanged scenes), in reading order so that when the same
        // text appears more than once the earliest unused previous copy is taken.
        for (var i = 0; i < current.Count; i++)
        {
            var norm = Normalize(current[i].Text);
            var hit = -1;
            for (var j = 0; j < previous.Count; j++)
            {
                if (used[j] || previousNorm[j] != norm)
                    continue;
                hit = j;
                break;
            }

            if (hit >= 0)
            {
                used[hit] = true;
                matches[i] = new SceneMatch(current[i].Number, previous[hit].Number, Changed: false);
            }
        }

        // Pass 2 — fuzzy matches for the scenes pass 1 left open: pair each to the most similar unused
        // previous scene above the threshold (a reworded same beat → Changed). Anything still open is new.
        for (var i = 0; i < current.Count; i++)
        {
            if (matches[i] is not null)
                continue;

            var currentTokens = Tokenize(current[i].Text);
            var bestJ = -1;
            var bestScore = SimilarityThreshold;
            for (var j = 0; j < previous.Count; j++)
            {
                if (used[j])
                    continue;
                var score = Jaccard(currentTokens, previousTokens[j]);
                if (score >= bestScore)
                {
                    bestScore = score;
                    bestJ = j;
                }
            }

            if (bestJ >= 0)
            {
                used[bestJ] = true;
                matches[i] = new SceneMatch(current[i].Number, previous[bestJ].Number, Changed: true);
            }
            else
            {
                matches[i] = new SceneMatch(current[i].Number, PreviousNumber: null, Changed: false);
            }
        }

        return matches;
    }

    // Collapse all whitespace to single spaces and lower-case, so formatting-only edits don't read as
    // changes. Matches SceneDetector's whitespace tolerance.
    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    // The distinct lower-cased word tokens of a scene (letters/digits runs), for the Jaccard similarity.
    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWord = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (isWord && start < 0)
                start = i;
            else if (!isWord && start >= 0)
            {
                tokens.Add(text[start..i].ToLowerInvariant());
                start = -1;
            }
        }
        return tokens;
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return 1.0;
        var intersection = 0;
        foreach (var token in a)
            if (b.Contains(token))
                intersection++;
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }
}
