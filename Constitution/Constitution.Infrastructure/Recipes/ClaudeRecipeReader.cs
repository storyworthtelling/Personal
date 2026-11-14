using System.Text.Json;
using Constitution.Domain.Recipes.Screens;
using Constitution.Infrastructure.Meals;
using Microsoft.Extensions.Options;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Voice.Claude;
using Storyworthtelling.Voice.Health;

namespace Constitution.Infrastructure.Recipes;

/// <summary>
/// A recipe read off photographs of its pages by the vision model over the Messages API (D176). One request: the fixed instruction
/// (<see cref="RecipePhotoPrompt.System"/>, cached), every page in the order it was taken, and the fixed question.
///
/// <para><b>What leaves the phone:</b> the page photographs, each with its EXIF (place, time, device) taken out first
/// (<see cref="JpegScrub"/>) and its size bounded, and the two fixed texts. Nothing about the household goes with them. The pages
/// are held in locals for the length of the call and nothing is stored on this object.</para>
///
/// <para><b>It never guesses.</b> No key is <see cref="RecipePhotoRead.Unavailable"/> before anything is sent; a page that is not
/// a whole JPEG, or is too big, stops the read before anything is sent; a reply cut off at the limit or making no sense is
/// <see cref="RecipePhotoRead.Unreadable"/> (<see cref="RecipePhotoReply"/>). Transport and service failures are exceptions carrying
/// the status, and the credential-health report is told only what the service said about the key.</para>
/// </summary>
public sealed class ClaudeRecipeReader(HttpClient http, IOptions<ClaudeVisionOptions> options, ICredentialHealthSink? health = null)
    : IRecipePhotoReader
{
    /// <summary>The largest page that is sent. The service takes five megabytes an image; this leaves room on a mobile upload.</summary>
    public const int LargestPageBytes = 4_000_000;

    // A long recipe is a few hundred lines; this leaves room without letting a runaway reply run on.
    private const int MaximumReplyTokens = 8000;
    private const int LongestReason = 200;

    private readonly ClaudeVisionOptions settings = options.Value;
    private readonly ClaudeMessagesClient claude = new(http, () => options.Value.ApiKey, health);

    public async Task<RecipePhotoRead> ReadAsync(IReadOnlyList<byte[]> pages, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return new RecipePhotoRead.Unavailable(RecipePhotoText.NoKeySentence);
        }

        if (pages.Count == 0)
        {
            return new RecipePhotoRead.Unreadable(RecipePhotoText.PageUnreadableSentence);
        }

        var content = new List<ClaudeBlock>(pages.Count + 1);
        foreach (var page in pages)
        {
            if (page is null || page.Length == 0 || page.Length > LargestPageBytes || JpegScrub.WithoutMetadata(page) is not { } picture)
            {
                return new RecipePhotoRead.Unreadable(RecipePhotoText.PageUnreadableSentence);
            }

            content.Add(ClaudeBlock.Image("image/jpeg", Convert.ToBase64String(picture)));
        }

        content.Add(ClaudeBlock.Text(RecipePhotoPrompt.Question(pages.Count)));
        var request = new ClaudeRequest(settings.Model, MaximumReplyTokens, [ClaudeBlock.Text(RecipePhotoPrompt.System, cache: true)], content);

        ClaudeReply reply;
        try
        {
            reply = await claude.SendAsync(request, cancellation).ConfigureAwait(false);
        }
        catch (ClaudeRequestException refused) when (refused.StatusCode is { } status)
        {
            throw new HttpRequestException(
                $"The recipe read was refused by the service: {(int)status} ({status}){ReasonFrom(refused.ResponseBody ?? string.Empty)}", refused, status);
        }
        catch (JsonException)
        {
            return RecipePhotoReply.Parse(null);
        }

        if (reply.StopReason == "max_tokens")
        {
            return new RecipePhotoRead.Unreadable(RecipePhotoText.TooLongSentence);
        }

        return RecipePhotoReply.Parse(reply.Text);
    }

    // The service's own words for why, when it gave some, capped; never the request.
    private static string ReasonFrom(string body)
    {
        try
        {
            using var parsed = JsonDocument.Parse(body);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object
                && parsed.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                && message.GetString() is { Length: > 0 } reason)
            {
                return ": " + (reason.Length > LongestReason ? reason[..LongestReason] : reason);
            }
        }
        catch (JsonException)
        {
            // Not a JSON error body (a gateway's plain text, say); the status alone has to do.
        }

        return string.Empty;
    }
}
