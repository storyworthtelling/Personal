using System.Text.Json;
using Constitution.Domain.Ink;

namespace Constitution.Domain.ReadBack;

/// <summary>
/// Turns the vision read's reply into per-page reads. A reply that cannot be read is pages that were not read,
/// never an exception and never an empty page; a box the reply spoils (answered twice, no true-or-false mark) is a
/// box it did not report. Anything the reply says about a page or a box the document does not ask about is ignored.
/// </summary>
public static class InkReply
{
    public static InkDocumentRead Parse(string? reply, ExpectedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var json = ObjectIn(reply);
        if (json is null)
        {
            return Unread(document, "The reply was not a readable answer.");
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("targets", out var targets)
                || targets.ValueKind != JsonValueKind.Array)
            {
                return Unread(document, "The reply had no list of boxes.");
            }

            var strokes = StrokesIn(root);
            var answers = Answers(targets, document);
            var pages = new Dictionary<int, InkPageRead>();
            foreach (var page in document.Pages)
            {
                pages[page.Number] = answers.TryGetValue(page.Number, out var reads) && reads.Count > 0
                    ? InkPageRead.Read(reads)
                    : InkPageRead.NotRead($"The reply had nothing for page {page.Number}.");
            }

            return new InkDocumentRead(pages, strokes);
        }
        catch (JsonException)
        {
            // The braces looked like an object and were not one; that is a reply that could not be read, which is a page not read.
            return Unread(document, "The reply was not a readable answer.");
        }
    }

    private static InkDocumentRead Unread(ExpectedDocument document, string why) =>
        new(document.Pages.ToDictionary(page => page.Number, _ => InkPageRead.NotRead(why)), PenStrokes.Unknown);

    // The first '{' to the last '}': tolerates prose and code fences around the object.
    private static string? ObjectIn(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return null;
        }

        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        return start >= 0 && end > start ? reply[start..(end + 1)] : null;
    }

    private static PenStrokes StrokesIn(JsonElement root)
    {
        if (root.TryGetProperty("penStrokes", out var word) && word.ValueKind == JsonValueKind.String)
        {
            return word.GetString()?.Trim().ToLowerInvariant() switch
            {
                "seen" => PenStrokes.Seen,
                "none" => PenStrokes.None,
                _ => PenStrokes.Unknown,
            };
        }

        return PenStrokes.Unknown;
    }

    private static Dictionary<int, List<InkTargetRead>> Answers(JsonElement targets, ExpectedDocument document)
    {
        var asked = document.Pages.ToDictionary(
            page => page.Number, page => page.AskedTargets.Select(target => target.Id).ToHashSet(StringComparer.Ordinal));
        var seen = new Dictionary<(int Page, string Id), List<InkTargetRead>>();

        foreach (var entry in targets.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("page", out var pageValue) || pageValue.ValueKind != JsonValueKind.Number || !pageValue.TryGetInt32(out var page)
                || !entry.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String
                || !entry.TryGetProperty("marked", out var marked) || marked.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !asked.TryGetValue(page, out var ids)
                || idValue.GetString() is not { } id || !ids.Contains(id))
            {
                continue;
            }

            var read = new InkTargetRead(
                id, marked.ValueKind == JsonValueKind.True, ConfidenceOf(entry), TextOf(entry, "text"), TextOf(entry, "alternative"));
            if (!seen.TryGetValue((page, id), out var list))
            {
                seen[(page, id)] = list = [];
            }

            list.Add(read);
        }

        // A box answered twice has two answers that cannot both be the owner's; neither is taken.
        var byPage = new Dictionary<int, List<InkTargetRead>>();
        foreach (var ((page, _), reads) in seen.Where(pair => pair.Value.Count == 1))
        {
            if (!byPage.TryGetValue(page, out var list))
            {
                byPage[page] = list = [];
            }

            list.Add(reads[0]);
        }

        return byPage;
    }

    // Missing or not a number is no confidence at all, which the resolver asks about; out of range is pulled to the nearest end.
    private static double ConfidenceOf(JsonElement entry)
    {
        if (entry.TryGetProperty("confidence", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            && !double.IsNaN(number))
        {
            return Math.Clamp(number, 0d, 1d);
        }

        return 0d;
    }

    private static string? TextOf(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }
}
