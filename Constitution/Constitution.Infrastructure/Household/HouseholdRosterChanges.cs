using Constitution.Domain.Household;
using Constitution.Domain.Settings;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Household;

/// <summary>
/// What a roster change came to, as the screen needs it: whether it was made, the plain sentence when it was not, and for a removal
/// the handle the undo window holds.
/// </summary>
public sealed record RosterChangeResult(bool Done, string? Sentence, RemovedPerson? Removed = null);

/// <summary>
/// The person sheet's writes, taken through <see cref="HouseholdFileService.EditAsync(Func{HouseholdDocument, HlcTimestamp, string, HouseholdDocument}, CancellationToken)"/>
/// so they use the phone's copy as it stands, this install's clock and id, and the service's own refusal while the file cannot be opened
/// (G14: a file that will not open is never written over; its sentence is what comes back). The decisions are the Domain's
/// (<see cref="RosterRemoval"/>, <see cref="PersonSheetEdit"/>); this only carries them to the file. A refusal made inside the edit
/// stops it before anything is saved, so a refused change writes nothing and asks for no sync.
/// </summary>
public sealed class HouseholdRosterChanges(HouseholdFileService service)
{
    private sealed class RefusedInsideTheEdit(string sentence) : Exception(sentence);

    public async Task<RosterChangeResult> RemoveAsync(PersonId id, CancellationToken cancellationToken)
    {
        var editor = new RosterEditor(service.RosterRole);
        RemovedPerson? removed = null;
        var refused = await TryEditAsync(
            (document, stamp, writer) =>
            {
                var result = RosterRemoval.Remove(editor, document, id, stamp, writer);
                removed = result.Removed;
                return result.Refusal is { } refusal ? throw new RefusedInsideTheEdit(refusal.Sentence) : result.Document;
            },
            cancellationToken);
        return refused is null ? new RosterChangeResult(true, null, removed) : new RosterChangeResult(false, refused);
    }

    public async Task<RosterChangeResult> RestoreAsync(RemovedPerson removed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(removed);
        var editor = new RosterEditor(service.RosterRole);
        var refused = await TryEditAsync(
            (document, stamp, writer) =>
            {
                var result = RosterRemoval.Restore(editor, document, removed, stamp, writer);
                return result.Outcome switch
                {
                    PersonRestoreOutcome.Restored => result.Document,
                    PersonRestoreOutcome.AlreadyBack => throw new RefusedInsideTheEdit($"{removed.Name} is already on the roster again."),
                    _ => throw new RefusedInsideTheEdit(result.Refusal!.Sentence),
                };
            },
            cancellationToken);
        return refused is null ? new RosterChangeResult(true, null) : new RosterChangeResult(false, refused);
    }

    public async Task<RosterChangeResult> SaveAsync(PersonId id, string name, bool onPickup, string? googleAddress, CancellationToken cancellationToken)
    {
        var editor = new RosterEditor(service.RosterRole);
        var refused = await TryEditAsync(
            (document, stamp, writer) =>
            {
                var saved = PersonSheetEdit.Apply(editor, document, id, name, onPickup, googleAddress, stamp, writer);
                if (saved.Refusal is { } refusal)
                {
                    throw new RefusedInsideTheEdit(refusal.Sentence);
                }

                return saved.Problems.Count > 0
                    ? throw new RefusedInsideTheEdit(string.Join(' ', saved.Problems.Select(PersonSheetModel.Describe)))
                    : saved.Document;
            },
            cancellationToken);
        return refused is null ? new RosterChangeResult(true, null) : new RosterChangeResult(false, refused);
    }

    // Returns the sentence the change was refused with, or null when it was made. Only the two refusals this class knows are
    // caught; any other failure (a disk, the caller leaving) propagates with its stack.
    private async Task<string?> TryEditAsync(Func<HouseholdDocument, HlcTimestamp, string, HouseholdDocument> edit, CancellationToken cancellationToken)
    {
        try
        {
            await service.EditAsync(edit, cancellationToken);
            return null;
        }
        catch (RefusedInsideTheEdit refusal)
        {
            return refusal.Message;
        }
        catch (HouseholdUnreadableException unreadable)
        {
            return unreadable.Message;
        }
    }
}
