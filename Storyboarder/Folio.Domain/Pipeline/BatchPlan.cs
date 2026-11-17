namespace Folio.Domain.Pipeline;

/// <summary>What to do next with one kind of batchable call for one draft.</summary>
public enum BatchStep
{
    /// <summary>Send the waiting items one call at a time, as before batching existed.</summary>
    OneByOne,

    /// <summary>Submit every waiting item as one batch.</summary>
    Submit,

    /// <summary>Ask whether the submitted batch has ended.</summary>
    Poll,

    /// <summary>The batch was polled a moment ago; wait out the interval.</summary>
    Wait,

    /// <summary>The batch has outlived its window; give up on it and go one at a time.</summary>
    Abandon,
}

/// <summary>When a batch for this kind was submitted, last polled and finished (null while running).</summary>
public sealed record BatchState(DateTimeOffset SubmittedAtUtc, DateTimeOffset? LastPolledAtUtc, DateTimeOffset? CompletedAtUtc);

/// <summary>
/// When unattended calls go to the Message Batches tier (owner's ruling, R39: half the cost of every token,
/// results usually within the hour and always within a day) and when they go one at a time.
///
/// <para>One batch per kind per draft. Whatever it leaves undone (an item that errored, expired, or came
/// back unreadable) goes one at a time afterwards, under the ordinary attempt and park rules, so nothing is
/// resubmitted in a loop and nothing is lost.</para>
/// </summary>
public static class BatchPlan
{
    /// <summary>A single item is not worth the batch's latency.</summary>
    public const int MinimumItems = 2;

    public static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(1);

    /// <summary>The batch tier's window is 24 hours; an hour's grace, then the batch is abandoned.</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(25);

    public static (BatchStep Step, TimeSpan Wait) Decide(int itemsWaiting, BatchState? batch, DateTimeOffset now)
    {
        if (itemsWaiting == 0)
            return (BatchStep.OneByOne, TimeSpan.Zero);
        if (batch is null)
            return (itemsWaiting >= MinimumItems ? BatchStep.Submit : BatchStep.OneByOne, TimeSpan.Zero);
        if (batch.CompletedAtUtc is not null)
            return (BatchStep.OneByOne, TimeSpan.Zero);
        if (now - batch.SubmittedAtUtc > GiveUpAfter)
            return (BatchStep.Abandon, TimeSpan.Zero);
        if (batch.LastPolledAtUtc is not { } polled || now - polled >= PollEvery)
            return (BatchStep.Poll, TimeSpan.Zero);
        return (BatchStep.Wait, PollEvery - (now - polled));
    }
}

/// <summary>One item of a finished batch, matched by its own key: the value, or why there is none.</summary>
public sealed record BatchItem<T>(int SceneNumber, T? Value, string? Failure) where T : class;

/// <summary>The kinds of call Folio batches, as stored on a batch row. Wire names: never rename one.</summary>
public static class BatchKinds
{
    public const string SceneJudgements = "scene-judgements";
    public const string SceneImagePrompts = "scene-image-prompts";

    /// <summary>The section reads on the standard model, after one direct pass has written the cache.</summary>
    public const string ReadsStandard = "reads-standard";

    /// <summary>The reader passes on the strong model, after one direct pass has written the cache.</summary>
    public const string ReadsStrong = "reads-strong";

    /// <summary>
    /// The scene pictures on Gemini's batch tier, one batch after another ("scene-images-1", "-2", …), each
    /// within <see cref="ImageBatchPlan.Budget"/>.
    /// </summary>
    public const string SceneImagesPrefix = "scene-images-";

    /// <summary>
    /// Recorded when Gemini refused a batch, or one came back with no pictures at all: the draft's remaining
    /// pictures go one at a time rather than batch after failing batch.
    /// </summary>
    public const string SceneImagesOff = "scene-images-off";
}

/// <summary>
/// One whole-book pass from a finished batch, by persona id: the stored JSON (a SectionRead or a Review,
/// as a direct pass stores it), or why there is none. A reader's short read is a failure here too.
/// </summary>
public sealed record PassBatchItem(string PersonaId, string? Json, string? Failure);

/// <summary>One scene's picture, as a batch request: its wordless prompt, the reference sheets of whoever is in it, its shape.</summary>
public sealed record SceneImageJob(int SceneNumber, string Prompt, IReadOnlyList<byte[]> References, double? AspectRatio)
{
    /// <summary>
    /// About how many bytes this request adds to an inline batch: every reference sheet goes base64, a third
    /// larger than its bytes, beside the prompt and a little JSON.
    /// </summary>
    public long ApproxBytes => References.Sum(reference => ((long)reference.Length + 2) / 3 * 4) + Prompt.Length * 2L + 2_000;
}

/// <summary>
/// How a book's scene pictures are split into Gemini batches (owner's request: the batch tier for images too).
/// An inline batch is capped at about 20 MB and each request carries its reference sheets, so scenes go in
/// order until the next would pass the budget; a scene too large for any batch on its own goes one at a time.
/// </summary>
public static class ImageBatchPlan
{
    /// <summary>Under the 20 MB inline limit, with room for the batch's own JSON.</summary>
    public const long Budget = 18_000_000;

    public static (IReadOnlyList<SceneImageJob> Chunk, IReadOnlyList<SceneImageJob> TooLarge) Chunk(
        IReadOnlyList<SceneImageJob> jobs, long budget = Budget)
    {
        var chunk = new List<SceneImageJob>();
        var tooLarge = new List<SceneImageJob>();
        long used = 0;
        foreach (var job in jobs)
        {
            if (job.ApproxBytes > budget)
            {
                tooLarge.Add(job);
                continue;
            }
            if (used + job.ApproxBytes > budget)
                break;
            chunk.Add(job);
            used += job.ApproxBytes;
        }
        return (chunk, tooLarge);
    }
}
