using Storyworthtelling.Voice.Claude;
using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Handwriting;

/// <summary>
/// The one call that sends a written-on page to Claude (audit 01 §12): the caller's standing instruction as the
/// prompt-cached system block, then the page as a PDF document, then the question for this page. The shared extractor,
/// Foreword's journal transcriber and Constitution's ink read each built this request by hand. What the reply means is
/// still each reader's own: notes, two journal regions, or a box at a time.
/// </summary>
public sealed class PageReader
{
    private readonly ClaudeMessagesClient claude;
    private readonly Func<string> model;

    /// <param name="apiKey">Read on every call, so a key rotated in the vault reaches the next page.</param>
    /// <param name="model">Read on every call, for the same reason.</param>
    public PageReader(
        HttpClient http, Func<string?> apiKey, Func<string> model,
        ICredentialHealthSink? health = null, string credentialId = SharedCredentialIds.Claude)
    {
        claude = new ClaudeMessagesClient(http, apiKey, health, credentialId);
        this.model = model;
    }

    public bool HasKey => claude.HasKey;

    /// <summary>The reply's text. Failures are the shared client's (<see cref="ClaudeRequestException"/>).</summary>
    public async Task<string> ReadAsync(
        byte[] page, string instruction, string question, int maxTokens, CancellationToken cancellationToken)
    {
        var request = new ClaudeRequest(model(), maxTokens,
            [ClaudeBlock.Text(instruction, cache: true)],
            [ClaudeBlock.Pdf(Convert.ToBase64String(page)), ClaudeBlock.Text(question)]);
        var reply = await claude.SendAsync(request, cancellationToken);
        return reply.Text;
    }
}
