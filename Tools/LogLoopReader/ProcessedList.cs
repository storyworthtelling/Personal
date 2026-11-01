using System.Text.Json;
using Storyworthtelling.Diagnostics;

namespace LogLoopReader;

/// <summary>
/// The processed list (LOG-LOOP-PLAN.md §5.6): the session cannot trash a file it does not own, so her phone's
/// finished files are named here instead and her phone trashes its own on its next upload. Names only, never a
/// line; only files the session marked done and read as hers; a name stays listed until its file has gone from
/// the folder, so a phone that missed a week still finds it.
/// </summary>
public static class ProcessedList
{
    /// <summary>The file her phone reads (Storyworthtelling.Voice's DriveLogFolder.ProcessedListName).</summary>
    public const string FileName = "swt-log-processed.json";

    /// <param name="manifest">This run's manifest: every file the folder held, with its state.</param>
    /// <param name="phoneOf">A file's phone, from its decrypted batch header; null when it could not be read.</param>
    /// <param name="previous">The names the list held before this run.</param>
    public static IReadOnlyList<string> Names(IEnumerable<Manifest.Entry> manifest, Func<string, string?> phoneOf, IEnumerable<string> previous)
    {
        var entries = manifest.ToList();
        var inFolder = entries.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var doneHers = entries
            .Where(e => e.State == "done" && e.Status == "Decrypted" && phoneOf(e.Name) == LogPhone.Partner)
            .Select(e => e.Name);
        return previous.Where(inFolder.Contains).Concat(doneHers)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    public static string Json(IReadOnlyList<string> names) => JsonSerializer.Serialize(new { names });

    /// <summary>The names an earlier list held; an unreadable or absent list holds none.</summary>
    public static IReadOnlyList<string> Previous(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("names", out var names) && names.ValueKind == JsonValueKind.Array
                ? names.EnumerateArray().Select(n => n.GetString()).OfType<string>().ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
