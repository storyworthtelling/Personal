namespace Folio.Domain.Pipeline;

// Why an image request produced no image because the provider blocked the content.
public enum ImageBlockReason
{
    // An adjustable safety category tripped (violence/sexual/etc.). Relaxing that category for the
    // single call may let it through, so the worker parks the panel for the user's approval.
    Safety,

    // Gemini's non-configurable image-output safety filter (finishReason IMAGE_SAFETY). safetySettings
    // can't lower it and there's nothing for the user to usefully approve, but it IS recoverable by
    // rephrasing — so the worker restages the beat to imply the content and, failing that, falls back to
    // a placeholder. It never cancels the book: a single filtered panel shouldn't sink the whole
    // conversion.
    Filtered,

    // A non-adjustable, content-level block: prohibited/copyrighted content (IP look-alikes, recitation,
    // blocklist, PII). No safety threshold can bypass it and rephrasing one panel won't clear an IP
    // resemblance, so the worker cancels the whole book with the reason rather than degrading it.
    Prohibited
}

// Raised by the image client when a generation request is refused on content grounds (as opposed to a
// rate limit or transport error). Carries the reason so the worker can branch — park-for-approval on
// Safety, restage-then-placeholder on Filtered, cancel-the-book on Prohibited — and, for Safety blocks,
// the flagged HARM_CATEGORY_* names so only those categories are relaxed on the approved re-render.
public sealed class ImageContentBlockedException : Exception
{
    public ImageBlockReason Reason { get; }

    // The Gemini HARM_CATEGORY_* names that tripped the block (empty for Filtered and Prohibited blocks,
    // which carry no adjustable category).
    public IReadOnlyList<string> Categories { get; }

    public ImageContentBlockedException(
        string message, ImageBlockReason reason, IReadOnlyList<string>? categories = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
        Categories = categories ?? [];
    }
}
