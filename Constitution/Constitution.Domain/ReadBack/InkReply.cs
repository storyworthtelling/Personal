using System.Text.Json;
using Constitution.Domain.Ink;

namespace Constitution.Domain.ReadBack;

/// <summary>
/// Turns the vision read's reply into per-page reads. A reply that cannot be read is pages that were not read,
/// never an exception and never an empty page; a box the reply spoils (answered twice, no true-or-false mark) is a
/// box it did not report. Anything the reply says about a page or a box the document does not ask about is ignored.
///
/// <para><b>A box is answered under its page-qualified id</b> (<see cref="InkInstruction.QualifiedId"/>), because every exercise
/// page prints the same ids. An id with no page in it is not an answer; an entry whose page number and qualified id disagree
/// (a model counting pages from the cover) spoils both boxes it could mean, and they come back not read.</para>
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
        var spoiled = new HashSet<(int Page, string Id)>();

        foreach (var entry in targets.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("page", out var pageValue) || pageValue.ValueKind != JsonValueKind.Number || !pageValue.TryGetInt32(out var page)
                || !entry.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String
                || !entry.TryGetProperty("marked", out var marked) || marked.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                continue;
            }

            // The same ids sit on every exercise page, so only the id qualified by its page says which box was meant. An id
            // that is not spelled that way says nothing about a page, and is not an answer.
            if (!TryUnqualify(idValue.GetString(), out var idPage, out var id))
            {
                continue;
            }

            // The page field and the id disagree: a model that counts its pages from the cover credits a mark to the page after
            // the one it is on. Which of the two is right cannot be told, so neither box is read.
            if (idPage != page)
            {
                spoiled.Add((idPage, id));
                spoiled.Add((page, id));
                continue;
            }

            if (!asked.TryGetValue(page, out var ids) || !ids.Contains(id))
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
        foreach (var ((page, _), reads) in seen.Where(pair => pair.Value.Count == 1 && !spoiled.Contains(pair.Key)))
        {
            if (!byPage.TryGetValue(page, out var list))
            {
                byPage[page] = list = [];
            }

            list.Add(reads[0]);
        }

        return byPage;
    }

    // "p2:done" is page 2, box "done"; only that exact spelling (a plain page number, no sign, no leading zero) is taken.
    private static bool TryUnqualify(string? qualified, out int page, out string id)
    {
        page = 0;
        id = string.Empty;
        if (qualified is not { Length: > 3 } || qualified[0] != 'p')
        {
            return false;
        }

        var colon = qualified.IndexOf(':', StringComparison.Ordinal);
        if (colon < 2 || colon == qualified.Length - 1)
        {
            return false;
        }

        var digits = qualified.AsSpan(1, colon - 1);
        if (digits[0] == '0' || !digits.ToString().All(char.IsAsciiDigit)
            || !int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out page))
        {
            return false;
        }

        id = qualified[(colon + 1)..];
        return true;
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
