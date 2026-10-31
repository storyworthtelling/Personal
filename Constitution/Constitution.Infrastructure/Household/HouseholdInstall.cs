using System.Text.Json;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Household;

/// <summary>
/// What this install knows about itself and keeps between launches: the id it writes records under, the last stamp its clock
/// gave (so a restart never stamps older than it did), and the household role the vault last said for an account.
/// </summary>
public sealed record HouseholdInstall(string InstallId, HlcTimestamp LastStamp, RememberedRole? Role);

/// <summary>Where the install's own small facts are kept. Not the household: nothing here is shared.</summary>
public interface IHouseholdInstallStore
{
    HouseholdInstall Load();

    void SaveClock(HlcTimestamp stamp);

    void SaveRole(HouseholdFileRole role, string account);
}

/// <summary>
/// The install's facts as one small file in a private folder the caller names (the head gives it the no-backup folder). A file
/// that cannot be read is set aside beside itself and a new install id begun: the id and the clock are re-derived (the clock is
/// moved past everything the household holds before each write) and the role is read from the vault again, so nothing is lost
/// that matters, and the damaged file is kept for a look rather than overwritten.
/// </summary>
public sealed class FileHouseholdInstallStore : IHouseholdInstallStore
{
    private const string FileName = "household-install.json";

    private readonly object gate = new();

    public FileHouseholdInstallStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        FilePath = Path.Combine(directory, FileName);
    }

    public string FilePath { get; }

    public HouseholdInstall Load()
    {
        lock (gate)
        {
            return LoadLocked();
        }
    }

    public void SaveClock(HlcTimestamp stamp)
    {
        lock (gate)
        {
            var current = LoadLocked();
            Write(new Stored(current.InstallId, stamp.WallMillis, stamp.Counter, current.Role?.Role.ToString(), current.Role?.Account));
        }
    }

    public void SaveRole(HouseholdFileRole role, string account)
    {
        var remembered = new RememberedRole(role, account);
        lock (gate)
        {
            var current = LoadLocked();
            Write(new Stored(current.InstallId, current.LastStamp.WallMillis, current.LastStamp.Counter, remembered.Role.ToString(), remembered.Account));
        }
    }

    private HouseholdInstall LoadLocked()
    {
        if (File.Exists(FilePath))
        {
            if (ReadStored() is { } stored)
            {
                return From(stored);
            }

            File.Move(FilePath, FilePath + ".damaged", overwrite: true);
        }

        var fresh = new Stored(Guid.NewGuid().ToString("N"), 0, 0, null, null);
        Write(fresh);
        return From(fresh);
    }

    // A file whose shape is wrong reads as nothing; the caller sets it aside rather than writing over it.
    private Stored? ReadStored()
    {
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
            return stored is not null && !string.IsNullOrWhiteSpace(stored.InstallId) ? stored : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HouseholdInstall From(Stored stored)
    {
        RememberedRole? role = null;
        if (Enum.TryParse<HouseholdFileRole>(stored.Role, out var parsed) && parsed != HouseholdFileRole.Unknown && !string.IsNullOrWhiteSpace(stored.Account))
        {
            role = new RememberedRole(parsed, stored.Account);
        }

        return new HouseholdInstall(stored.InstallId, new HlcTimestamp(stored.Wall, stored.Counter), role);
    }

    private void Write(Stored stored)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var staging = FilePath + ".tmp";
        File.WriteAllText(staging, JsonSerializer.Serialize(stored));
        File.Move(staging, FilePath, overwrite: true);
    }

    private sealed record Stored(string InstallId, long Wall, int Counter, string? Role, string? Account);
}
