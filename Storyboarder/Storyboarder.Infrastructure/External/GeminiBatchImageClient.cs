using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Imaging;
using Storyboarder.Infrastructure.Configuration;

namespace Storyboarder.Infrastructure.External;

// Renders panels through the Gemini Batch API (~50% cheaper, separate quota pool, up-to-24h turnaround,
// work runs server-side). It mirrors the synchronous GeminiImageClient's request/response shape — same
// model, same generateContent body, same "image is an inlineData part" result — but wraps the panels as
// one batch job and reads results back on poll.
//
// ⚠ NOT LIVE-VERIFIED. The exact Batch REST envelope below (the batchGenerateContent request wrapper and
// the polled Operation's result nesting) is reconstructed from Google's docs and should be confirmed on the
// first real run. Batch mode is now on by default, so that first run IS the verification: the parsing is
// written defensively so a shape mismatch degrades to "no image → falls back to synchronous render" rather
// than throwing, and a bad submit likewise fails the job into the synchronous path. See BATCHING.md.
//
// Reference sheets go through the Files API: each distinct sheet is uploaded ONCE per submission and
// referenced by URI in every request (BATCHING.md, complication #1). Inlining the base64 bytes per request
// — a dozen sheets across ~2000 panels — would blow the ~20 MB inline batch ceiling many times over; the
// same sheet recurs across most panels, so uploading it once and pointing every request at the same URI is
// both correct at scale and far less wasteful. ⚠ The Files API upload protocol below is likewise
// reconstructed from Google's docs and NOT live-verified — see the class warning and BATCHING.md.
public sealed class GeminiBatchImageClient : IGeminiBatchClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string UploadUrl = "https://generativelanguage.googleapis.com/upload/v1beta/files";

    private readonly HttpClient httpClient;
    private readonly GeminiOptions options;

    public GeminiBatchImageClient(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<string> SubmitAsync(IReadOnlyList<BatchPanelRequest> requests, CancellationToken cancellationToken)
    {
        // Upload each DISTINCT reference sheet once (keyed by content hash, since the same sheet's bytes
        // recur across every panel that cites that character/setting) and remember its URI. Every request
        // then references the sheet by URI instead of carrying the bytes.
        var uriByHash = new Dictionary<string, string>();
        foreach (var request in requests)
            foreach (var reference in request.References)
            {
                var hash = Hash(reference);
                if (!uriByHash.ContainsKey(hash))
                    uriByHash[hash] = await UploadReferenceAsync(reference, cancellationToken);
            }

        var inlined = new JsonArray();
        foreach (var request in requests)
            inlined.Add(new JsonObject
            {
                // metadata.key correlates each result back to its panel on poll.
                ["metadata"] = new JsonObject { ["key"] = request.PanelId.ToString() },
                ["request"] = BuildGenerateContentBody(request, uriByHash)
            });

        var payload = new JsonObject
        {
            ["batch"] = new JsonObject
            {
                ["displayName"] = $"storyboarder-{Guid.NewGuid():N}",
                ["inputConfig"] = new JsonObject
                {
                    ["requests"] = new JsonObject { ["requests"] = inlined }
                }
            }
        };

        var url = $"{BaseUrl}/models/{options.Model}:batchGenerateContent?key={Uri.EscapeDataString(options.ApiKey)}";
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
        };

        var response = await httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini batch submit failed: {(int)response.StatusCode}: {body}");

        // The create call returns a long-running Operation whose "name" is the job handle we poll.
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } jobId)
            return jobId;
        throw new InvalidOperationException($"Gemini batch submit returned no job name: {body}");
    }

    public async Task<BatchJobStatus> PollAsync(string jobId, CancellationToken cancellationToken)
    {
        // The job id is the operation resource name (e.g. "batches/abc" or "operations/abc").
        var url = $"{BaseUrl}/{jobId}?key={Uri.EscapeDataString(options.ApiKey)}";
        var response = await httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini batch poll failed: {(int)response.StatusCode}: {body}");

        return ParsePoll(body);
    }

    // Reads a polled Operation into a job status. Not done -> Running. Done with an operation-level error
    // -> Failed (every panel falls back to synchronous rendering). Done with results -> Succeeded, with a
    // per-panel outcome parsed from each inlined response (image present -> success; anything else,
    // including a content block, -> a failure the caller re-renders synchronously).
    internal static BatchJobStatus ParsePoll(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var done = root.TryGetProperty("done", out var doneProperty) && doneProperty.ValueKind == JsonValueKind.True;
        if (!done)
            return BatchJobStatus.StillRunning;

        // A whole-operation error: nothing to reconcile per panel, so the job failed.
        if (root.TryGetProperty("error", out _))
            return BatchJobStatus.Broken;

        if (!root.TryGetProperty("response", out var response))
            return BatchJobStatus.Broken;

        var inlined = FindInlinedResponses(response);
        if (inlined is not { ValueKind: JsonValueKind.Array })
            return BatchJobStatus.Broken;

        var outcomes = new List<BatchPanelOutcome>();
        foreach (var item in inlined.Value.EnumerateArray())
        {
            var key = ReadKey(item);
            if (key is null)
                continue;

            var image = item.TryGetProperty("response", out var perResponse)
                ? TryExtractImage(perResponse)
                : null;

            outcomes.Add(image is not null
                ? new BatchPanelOutcome(key.Value, image, null)
                : new BatchPanelOutcome(key.Value, null, ShortError(item)));
        }

        return new BatchJobStatus(BatchJobState.Succeeded, outcomes);
    }

    // The inlined responses can sit either directly under response.inlinedResponses (an array) or nested
    // one level (response.inlinedResponses.inlinedResponses) depending on the API surface — tolerate both.
    private static JsonElement? FindInlinedResponses(JsonElement response)
    {
        if (!response.TryGetProperty("inlinedResponses", out var level1))
            return null;
        if (level1.ValueKind == JsonValueKind.Array)
            return level1;
        if (level1.TryGetProperty("inlinedResponses", out var level2) && level2.ValueKind == JsonValueKind.Array)
            return level2;
        return null;
    }

    private static Guid? ReadKey(JsonElement item)
    {
        if (item.TryGetProperty("metadata", out var metadata) &&
            metadata.TryGetProperty("key", out var key) &&
            Guid.TryParse(key.GetString(), out var panelId))
            return panelId;
        return null;
    }

    private static string? ShortError(JsonElement item)
    {
        if (item.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message))
            return message.GetString();
        // No explicit error: a content block (finishReason/blockReason) or an empty candidate. The caller
        // only needs to know it didn't render; the synchronous re-render re-surfaces the real reason.
        return "Batch produced no image for this panel.";
    }

    // Extracts the first inlineData image from a GenerateContentResponse (identical to the sync client).
    private static byte[]? TryExtractImage(JsonElement generateContentResponse)
    {
        if (!generateContentResponse.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("inlineData", out var inline) &&
                    inline.TryGetProperty("data", out var data) &&
                    data.GetString() is { } base64)
                {
                    return Convert.FromBase64String(base64);
                }
            }
        }
        return null;
    }

    // Uploads one reference sheet to the Files API and returns its file URI. Uses the single-request "raw"
    // upload protocol (the whole body is the image bytes; the JSON reply carries file.uri). ⚠ Not
    // live-verified — if the real API needs the resumable start/upload handshake instead, this is the one
    // method to adjust; the submit path degrades to a failed job → synchronous fallback on any mismatch.
    private async Task<string> UploadReferenceAsync(byte[] reference, CancellationToken cancellationToken)
    {
        var url = $"{UploadUrl}?key={Uri.EscapeDataString(options.ApiKey)}";
        using var content = new ByteArrayContent(reference);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        message.Headers.Add("X-Goog-Upload-Protocol", "raw");

        var response = await httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini Files API upload failed: {(int)response.StatusCode}: {body}");

        var uri = ExtractFileUri(body);
        return uri ?? throw new InvalidOperationException($"Gemini Files API upload returned no file uri: {body}");
    }

    // Reads file.uri (or a top-level uri) from a Files API upload reply.
    private static string? ExtractFileUri(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("file", out var file) && file.TryGetProperty("uri", out var nested) &&
            nested.GetString() is { Length: > 0 } fileUri)
            return fileUri;
        if (root.TryGetProperty("uri", out var flat) && flat.GetString() is { Length: > 0 } topUri)
            return topUri;
        return null;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // The generateContent body for one panel — the same shape GeminiImageClient sends synchronously (text
    // prompt + reference images + IMAGE modality at the panel's aspect ratio). Unlike the sync client the
    // references are fileData URIs (uploaded once via the Files API), not inlined bytes, so the batch stays
    // under the inline size ceiling.
    private static JsonObject BuildGenerateContentBody(BatchPanelRequest request, IReadOnlyDictionary<string, string> uriByHash)
    {
        var prompt = PanelImagePrompt.Build(request.ArtStyle, request.VisualPrompt, request.References.Count > 0);
        var parts = new JsonArray { new JsonObject { ["text"] = prompt } };
        foreach (var reference in request.References)
            parts.Add(new JsonObject
            {
                ["fileData"] = new JsonObject
                {
                    ["mimeType"] = "image/png",
                    ["fileUri"] = uriByHash[Hash(reference)]
                }
            });

        var generationConfig = new JsonObject { ["responseModalities"] = new JsonArray("IMAGE") };
        if (request.AspectRatio is { } ratio)
            generationConfig["imageConfig"] = new JsonObject { ["aspectRatio"] = NearestAspectRatio(ratio) };

        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["parts"] = parts }),
            ["generationConfig"] = generationConfig
        };
    }

    private static readonly (string Label, double Ratio)[] SupportedAspectRatios =
    {
        ("21:9", 21.0 / 9), ("16:9", 16.0 / 9), ("3:2", 3.0 / 2), ("4:3", 4.0 / 3), ("5:4", 5.0 / 4),
        ("1:1", 1.0), ("4:5", 4.0 / 5), ("3:4", 3.0 / 4), ("2:3", 2.0 / 3), ("9:16", 9.0 / 16),
    };

    private static string NearestAspectRatio(double ratio) =>
        SupportedAspectRatios.MinBy(candidate => Math.Abs(candidate.Ratio - ratio)).Label;
}
