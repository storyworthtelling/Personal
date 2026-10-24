namespace Constitution.WorkoutDelivery;

/// <summary>Where the weekly run keeps its own files, inside the app's no-backup folder. No Android type, so a test can read it.</summary>
public static class WorkoutWeekPaths
{
    public const string FolderName = "workout-week";
    public const string DeliveredFile = "delivered.json";

    public static string Folder(string noBackupRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noBackupRoot);
        return Path.Combine(noBackupRoot, FolderName);
    }

    public static string Delivered(string noBackupRoot) => Path.Combine(Folder(noBackupRoot), DeliveredFile);
}
