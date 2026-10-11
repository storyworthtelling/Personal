using Pulp.App;
using Pulp.Domain.Discover;

namespace Pulp.Discover;

/// <summary>
/// The kept week (<see cref="DiscoverPageWeek"/>) as one file in the app's own files directory. This
/// phone's alone: the stack is the device owner's (D23), re-derivable from the catalogue, and not
/// household state, so it never goes into the shared household file. A file that will not read is
/// logged and treated as no week, and the next deal replaces it. <see cref="Save"/> throws on an I/O
/// failure; the caller decides what that costs (a dealt week is still shown).
/// </summary>
internal sealed class DiscoverWeekStore(string directory, PulpDiagnostics diagnostics)
{
    public const string FileName = "pulp-discover-week.json";

    private readonly string path = Path.Combine(directory, FileName);
    // Static: the file is one per app, so every instance shares its gate and two saves can never
    // interleave on the one temporary file. DiscoverArea keeps a single instance besides.
    private static readonly object Gate = new();

    public DiscoverPageWeek? Load()
    {
        string text;
        lock (Gate)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            text = File.ReadAllText(path);
        }

        var read = DiscoverPageWeekCodec.Read(text);
        if (read.Week is null)
        {
            diagnostics.Warn("discover", read.Problem ?? "The kept week could not be read; it will be dealt again.");
        }

        return read.Week;
    }

    public void Save(DiscoverPageWeek week)
    {
        var text = DiscoverPageWeekCodec.Write(week);
        lock (Gate)
        {
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
    }
}
