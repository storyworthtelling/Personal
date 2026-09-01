namespace Codex.Domain.Diagnostics;

/// <summary>
/// Persists the most recent <see cref="CrashReport"/> to a single file in the
/// crashing app's private directory. Only the latest crash is kept — enough to
/// answer "why did it die last time" without accumulating state on a locked-down
/// device. The directory is injected so the store is unit-testable against a
/// temp folder; on device it is rooted at the app's FilesDir.
///
/// This lives in Domain rather than Infrastructure deliberately: it is
/// dependency-free System.IO (Domain already reads embedded resources) and is
/// consumed by the shared Ui.Android layer, which references only Domain.
/// Homing it in Infrastructure would drag MailKit/SQLite into the UI library
/// and the Camera head, which reference Infrastructure nowhere else.
/// </summary>
public class CrashLogStore
{
    private const string FileName = "crash-log.txt";
    private readonly string filePath;

    public CrashLogStore(string directory)
    {
        filePath = Path.Combine(directory, FileName);
    }

    public bool HasCrash => File.Exists(filePath);

    public void Save(CrashReport report)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(filePath, CrashLogFormatter.Format(report));
    }

    public CrashReport? ReadLatest()
    {
        if (!File.Exists(filePath))
        {
            return null;
        }
        return CrashLogFormatter.TryParse(File.ReadAllText(filePath), out var report) ? report : null;
    }

    public void Clear()
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
