namespace Index.Domain.Diagnostics;

/// <summary>
/// Keeps the most recent fault in one file in the app's private directory, until the owner has
/// seen it. Score's <c>CrashLogStore</c> is the pattern: a crash that kills the process cannot be
/// shown then, so it is shown on the next launch and cleared. The directory is injected so the
/// store is testable against a temp folder; on device it is the app's files directory.
/// </summary>
public sealed class PendingCrashStore
{
    private const string FileName = "pending-crash.txt";
    private readonly string directory;
    private readonly string filePath;

    public PendingCrashStore(string directory)
    {
        this.directory = directory;
        filePath = Path.Combine(directory, FileName);
    }

    public bool HasCrash => File.Exists(filePath);

    public void Save(string report)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(filePath, report);
    }

    public string? ReadText() => File.Exists(filePath) ? File.ReadAllText(filePath) : null;

    public void Clear()
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
