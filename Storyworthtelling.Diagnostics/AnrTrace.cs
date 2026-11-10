namespace Storyworthtelling.Diagnostics;

/// <summary>
/// The main thread's section of an ANR thread dump (audit 08 §36). Android keeps the dump with the exit record, and the
/// main thread's frames are what say where the app was stuck; every other thread is left behind. The frames are code
/// locations, not values.
/// </summary>
public static class AnrTrace
{
    public const int DefaultCap = 4096;

    /// <summary>ApplicationExitInfo's reason number for an ANR.</summary>
    public const int Reason = 6;

    // A dump is every thread of the process; the main thread comes early, so a bounded read keeps the excerpt.
    private const int MostRead = 512 * 1024;

    /// <summary>The main thread's section from the dump Android kept, or empty when there is none to read.</summary>
    public static string FromStream(Stream? dump, int cap = DefaultCap)
    {
        if (dump is null)
        {
            return string.Empty;
        }
        using (dump)
        {
            var buffer = new char[MostRead];
            using var reader = new StreamReader(dump);
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            return MainThread(new string(buffer, 0, read), cap);
        }
    }

    public static string MainThread(string dump, int cap = DefaultCap)
    {
        var lines = dump.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith("\"main\" ", StringComparison.Ordinal));
        if (start < 0)
        {
            return string.Empty;
        }
        var end = Array.FindIndex(lines, start + 1, line => line.Length == 0);
        var section = string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
        return section.Length <= cap ? section : string.Concat(section.AsSpan(0, cap), "\n… cut");
    }
}
