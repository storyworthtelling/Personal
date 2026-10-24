namespace Constitution.ReadBack;

/// <summary>
/// Where the read-back keeps its files, as arithmetic on the app's no-backup folder. It holds no Android type so a test can
/// read it. The weekly run's delivery side asks this class for the expected-documents file rather than spelling the path
/// again, so delivery records a page where the read-back will look for its ink and the two cannot drift apart.
/// </summary>
public static class ReadBackPaths
{
    public const string FolderName = "read-back";
    public const string ExpectedFile = "expected.json";
    public const string ResultsFolder = "results";
    public const string LedgerFile = "skip-ledger.json";
    public const string ProofFile = "ink-proof.json";
    public const string StatusFile = "status.json";

    public static string Folder(string noBackupRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noBackupRoot);
        return Path.Combine(noBackupRoot, FolderName);
    }

    public static string Expected(string noBackupRoot) => Path.Combine(Folder(noBackupRoot), ExpectedFile);

    public static string Proof(string noBackupRoot) => Path.Combine(Folder(noBackupRoot), ProofFile);

    public static string Results(string noBackupRoot) => Path.Combine(Folder(noBackupRoot), ResultsFolder);

    public static string Ledger(string noBackupRoot) => Path.Combine(Folder(noBackupRoot), LedgerFile);

    public static string Status(string noBackupRoot) => Path.Combine(Folder(noBackupRoot), StatusFile);
}
