namespace Codex.Domain.Voice;

/// <summary>
/// Which kind of thing the voice says next.
///
/// Two rules, and the second is the one that matters. Weights are <b>shares of what
/// is on offer</b>, not of everything that exists — a category that is not eligible
/// right now lifts the others rather than producing silence — and the voice never
/// says the same kind of thing twice running, because a weighted draw that repeats
/// reads as a rut rather than as chance.
///
/// The roll is passed in rather than drawn here, so the choice is pure and the
/// distribution is testable. The caller supplies <c>Random.Shared.NextDouble()</c>.
/// </summary>
public static class VoicePicker
{
    public static string? Pick(
        IReadOnlyList<string> eligible,
        IReadOnlyDictionary<string, int> weights,
        string? lastCategory,
        double roll)
    {
        if (eligible.Count == 0)
        {
            return null;
        }

        // Drop what was just said — unless it is all there is, where silence would be
        // a worse answer than a repeat.
        var candidates = eligible.Where(category => category != lastCategory).ToList();
        if (candidates.Count == 0)
        {
            candidates = [.. eligible];
        }

        var shares = candidates
            .Select(category => weights.TryGetValue(category, out var weight) && weight > 0 ? weight : 0)
            .ToList();

        // Nothing weighted: spread evenly rather than answering nothing at all.
        var total = shares.Sum();
        if (total == 0)
        {
            shares = [.. candidates.Select(_ => 1)];
            total = candidates.Count;
        }

        var target = Math.Clamp(roll, 0d, 1d) * total;
        var running = 0d;
        for (var i = 0; i < candidates.Count; i++)
        {
            running += shares[i];
            if (target < running)
            {
                return candidates[i];
            }
        }

        // A roll of exactly 1 lands past every share; the last candidate is its share.
        return candidates[^1];
    }
}
