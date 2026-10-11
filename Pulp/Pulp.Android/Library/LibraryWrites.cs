using Pulp.App;
using Pulp.Domain.Household;

namespace Pulp.Library;

/// <summary>
/// The household writes one screen makes, run one after another off the UI thread through a
/// <see cref="HouseholdWriteQueue"/>. An undo can arrive while its act is still being written, so each
/// write waits for the one before it — an undo never lands ahead of what it reverses. A write that
/// fails is logged to Diagnostics under <paramref name="area"/> and
/// <paramref name="after"/> still runs so the page re-reads what is really there.
/// </summary>
internal sealed class LibraryWrites(PulpServices services, Action after, string area = "library")
{
    private readonly HouseholdWriteQueue queue = new(
        (what, exception) => services.Diagnostics.Warn(area, "A household write could not land; the page shows what the household file holds.", exception),
        after);

    public void Enqueue(string what, Func<CancellationToken, Task> write) => _ = queue.Enqueue(what, write);
}
