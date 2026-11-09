using System.Text.Json;

namespace Codex.Domain.Plate;

/// <summary>
/// The scan tray written down, so pages gathered over several trips through Google's scanner survive Android
/// killing Plate while the scanner is in front (error-handling §8). A tray with no pages is not kept, and
/// anything that does not read is no tray rather than an exception.
/// </summary>
public static class ScanTrayCodec
{
    private sealed record Stored(ScanDestination Destination, List<StoredPage> Pages);

    private sealed record StoredPage(string Reference, DateTime ScannedAt);

    public static string Write(ScanTray tray) =>
        JsonSerializer.Serialize(new Stored(tray.Destination, tray.Pages.Select(p => new StoredPage(p.Reference, p.ScannedAt)).ToList()));

    public static ScanTray? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(text);
            if (stored?.Pages is not { Count: > 0 } pages)
            {
                return null;
            }

            return ScanTray.Restored(stored.Destination, pages.Select(p => new ScanPage(p.Reference, p.ScannedAt)).ToList());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
