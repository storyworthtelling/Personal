using Pulp.App;
using Pulp.Domain.Actions;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;

namespace Pulp.Components;

/// <summary>
/// What a tick in the Add to… picker (mockup 11) writes, for every screen that opens it — the title
/// page, Continue watching and Discover. Each change is an edit of the household as it stands when the
/// write lands (<see cref="PulpServices.EditPlaylistAsync"/>, <see cref="PulpServices.RemoveParentPickAsync"/>),
/// never a copy read when the picker opened, so a tick and an untick in one session both land; and an
/// untick's undo is the inverse edit, putting the entry back where it stood. Writes go through the
/// screen's own queue, so an undo never lands ahead of the untick it reverses.
/// </summary>
internal sealed class PickerWrites
{
    private readonly PulpServices services;
    private readonly Action<string, Func<CancellationToken, Task>> enqueue;
    private readonly Action<string, Func<CancellationToken, Task>> enqueueNewPlaylist;
    private readonly ViewerId viewer;
    private readonly TitleId title;
    private readonly PlaylistEntry entry;

    /// <param name="enqueue">The screen's one ordered write queue.</param>
    /// <param name="enqueueNewPlaylist">Where a new playlist's write goes, when the screen does more after it (the title page reopens the picker); else <paramref name="enqueue"/>.</param>
    public PickerWrites(
        PulpServices services,
        Action<string, Func<CancellationToken, Task>> enqueue,
        ViewerId viewer,
        TitleId title,
        PlaylistEntry entry,
        Action<string, Func<CancellationToken, Task>>? enqueueNewPlaylist = null)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.enqueue = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
        this.enqueueNewPlaylist = enqueueNewPlaylist ?? enqueue;
        this.viewer = viewer;
        this.title = title;
        this.entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    /// <summary>The picker's change handler: null for an add (the tick is the success, D257), the undo for a removal.</summary>
    public Action? OnPick(PickerRow row, PickerChange change)
    {
        ArgumentNullException.ThrowIfNull(row);
        switch (change)
        {
            case PickerChange.Add:
            case PickerChange.AddAndAllowList:
                Add(row);
                return null;
            case PickerChange.RemoveUndoFirst:
                return Remove(row);
            case PickerChange.Refused:
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "A picker change no screen handles.");
        }
    }

    /// <summary>A playlist named in place, personal by default (mockup 11), with this title already on it; null for a blank name.</summary>
    public HouseholdPlaylist? NewPlaylist(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var playlist = new HouseholdPlaylist(HouseholdEdits.NewPlaylistId(), viewer, Shared: false, Playlist.Named(name.Trim()).Add(entry));
        enqueueNewPlaylist($"make the playlist {playlist.List.Name} with {title}", token => services.CreatePlaylistAsync(playlist, token));
        return playlist;
    }

    private void Add(PickerRow row)
    {
        if (row.Group == PickerGroup.Playlists)
        {
            var id = new PlaylistId(row.Key);
            var addition = new EntryAddition(entry);
            enqueue($"add {title} to playlist {id}", token => services.EditPlaylistAsync(id, addition.Apply, token));
            return;
        }

        // A parent's pick allow-lists its title for the child (D255), so Add and AddAndAllowList write the same record.
        var child = new ViewerId(row.Key);
        var pick = new ParentPick(title, viewer, DateTimeOffset.UtcNow);
        enqueue($"add {title} to {child}'s Favorites", token => services.AddParentPickAsync(child, pick, token));
    }

    private Action Remove(PickerRow row)
    {
        if (row.Group == PickerGroup.Playlists)
        {
            var id = new PlaylistId(row.Key);
            var removal = new EntryRemoval(entry);
            enqueue($"take {title} off playlist {id}", token => services.EditPlaylistAsync(id, removal.Apply, token));
            return () => enqueue($"put {title} back on playlist {id}", token => services.EditPlaylistAsync(id, removal.Undo, token));
        }

        var child = new ViewerId(row.Key);
        var taken = new ParentPickRemoval(child, title);
        enqueue($"take {title} off {child}'s Favorites", token => services.RemoveParentPickAsync(taken, token));
        return () => enqueue(
            $"put {title} back on {child}'s Favorites",
            token => services.PutBackParentPickAsync(taken, new ParentPick(title, viewer, DateTimeOffset.UtcNow), token));
    }
}
