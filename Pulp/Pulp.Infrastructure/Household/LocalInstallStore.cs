using System.Text.Json;
using Pulp.Domain.Household;

namespace Pulp.Infrastructure.Household;

/// <summary>This install's writer id and the hybrid logical clock's last stamp.</summary>
public sealed record InstallState(string InstallId, HlcTimestamp LastStamp);

/// <summary>
/// What belongs to this install rather than the household, as one small file beside the household:
/// the writer id every record it writes is stamped with (made once, then kept), and the clock's last
/// stamp, which <see cref="HybridLogicalClock"/> must resume from — a fresh clock on a phone whose
/// time was set back would stamp edits older than its own last ones. The stamp on disk only ever
/// rises. Not shared, not in the vault: a second phone restoring this id would make two phones one
/// writer, and their same-moment edits would tie.
/// </summary>
public sealed class LocalInstallStore
{
    public const string FileName = "pulp-install.json";

    private readonly string directory;
    private readonly string path;
    private readonly object gate = new();

    public LocalInstallStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = directory;
        path = Path.Combine(directory, FileName);
    }

    /// <summary>The install's state, making and keeping a new writer id the first time.</summary>
    public InstallState Load()
    {
        lock (gate)
        {
            var state = Read();
            if (state is not null)
            {
                return state;
            }

            var fresh = new InstallState("install-" + Guid.NewGuid().ToString("N"), HlcTimestamp.Zero);
            Write(fresh);
            return fresh;
        }
    }

    /// <summary>Keeps the clock's last stamp, unless the one on disk is already later.</summary>
    public void SaveClock(HlcTimestamp last)
    {
        lock (gate)
        {
            var state = Read() ?? new InstallState("install-" + Guid.NewGuid().ToString("N"), HlcTimestamp.Zero);
            if (last > state.LastStamp)
            {
                Write(state with { LastStamp = last });
            }
        }
    }

    private InstallState? Read()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            var id = root.GetProperty("installId").GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new HouseholdFormatException($"{FileName} names no install id.");
            }

            return new InstallState(id, new HlcTimestamp(root.GetProperty("wall").GetInt64(), root.GetProperty("counter").GetInt32()));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            // Never silently replaced: a new id here would make this phone a second writer.
            throw new HouseholdFormatException($"{FileName} is damaged and this install's writer id cannot be read: {exception.Message}", exception);
        }
    }

    private void Write(InstallState state)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("installId", state.InstallId);
            writer.WriteNumber("wall", state.LastStamp.WallMillis);
            writer.WriteNumber("counter", state.LastStamp.Counter);
            writer.WriteEndObject();
        }

        AtomicFile.Write(directory, path, System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }
}
