using System.Text.Json;
using Pulp.Domain.Core;
using Pulp.Domain.Household;

namespace Pulp.Infrastructure.Household;

/// <summary>
/// This install's writer id, the hybrid logical clock's last stamp, and — once Release D's first run
/// records it — whose phone this is (<see cref="Pulp.Domain.People.ThisPhone"/> decides whether it still holds).
/// </summary>
public sealed record InstallState(string InstallId, HlcTimestamp LastStamp, ViewerId? Viewer = null, RememberedRole? Role = null);

/// <summary>
/// What belongs to this install rather than the household, as one small file beside the household:
/// the writer id every record it writes is stamped with (made once, then kept), and the clock's last
/// stamp, which <see cref="HybridLogicalClock"/> must resume from — a fresh clock on a phone whose
/// time was set back would stamp edits older than its own last ones. The stamp on disk only ever
/// rises. Not shared, not in the vault: a second phone restoring this id would make two phones one
/// writer, and their same-moment edits would tie.
///
/// <para>The recorded viewer lives here for the same reason (Foreword's <c>InstallOptions</c>
/// lesson): "whose phone is this" is a fact about the install, and a copy in the shared household
/// file would be restored onto the other adult's phone, which would then show them the first adult's
/// private lists. Optional — a file written before it existed reads as matched to nobody.</para>
///
/// <para>So is the last household role the vault said (<see cref="RememberedRole"/>), with the account it
/// was read under: it keeps a phone the household is shared to from acting as the owner's during the
/// moments after a cold start before the vault has been read. Install-local for the same reason as the
/// viewer; optional, and an unreadable value reads as no memory rather than a damaged install.</para>
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

    /// <summary>Records whose phone this is, or clears it with null. The writer id and the clock are kept.</summary>
    public void SaveViewer(ViewerId? viewer)
    {
        lock (gate)
        {
            var state = Read() ?? new InstallState("install-" + Guid.NewGuid().ToString("N"), HlcTimestamp.Zero);
            Write(state with { Viewer = viewer });
        }
    }

    /// <summary>Remembers the role the vault said for this account. Unknown and anonymous roles are refused, not stored.</summary>
    public void SaveRole(HouseholdFileRole role, string account)
    {
        var remembered = new RememberedRole(role, account);
        lock (gate)
        {
            var state = Read() ?? new InstallState("install-" + Guid.NewGuid().ToString("N"), HlcTimestamp.Zero);
            if (state.Role != remembered)
            {
                Write(state with { Role = remembered });
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

            ViewerId? viewer = root.TryGetProperty("viewer", out var recorded) && recorded.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(recorded.GetString())
                    ? new ViewerId(recorded.GetString()!)
                    : null;
            return new InstallState(id, new HlcTimestamp(root.GetProperty("wall").GetInt64(), root.GetProperty("counter").GetInt32()), viewer, ReadRole(root));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            // Never silently replaced: a new id here would make this phone a second writer.
            throw new HouseholdFormatException($"{FileName} is damaged and this install's writer id cannot be read: {exception.Message}", exception);
        }
    }

    private static RememberedRole? ReadRole(JsonElement root)
    {
        if (!root.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("roleAccount", out var account) || account.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(account.GetString())
            || !Enum.TryParse<HouseholdFileRole>(role.GetString(), out var parsed)
            || !Enum.IsDefined(parsed)
            || parsed == HouseholdFileRole.Unknown)
        {
            return null;
        }

        return new RememberedRole(parsed, account.GetString()!);
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
            if (state.Viewer is { } viewer)
            {
                writer.WriteString("viewer", viewer.Value);
            }

            if (state.Role is { } remembered)
            {
                writer.WriteString("role", remembered.Role.ToString());
                writer.WriteString("roleAccount", remembered.Account);
            }

            writer.WriteEndObject();
        }

        AtomicFile.Write(directory, path, System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }
}
