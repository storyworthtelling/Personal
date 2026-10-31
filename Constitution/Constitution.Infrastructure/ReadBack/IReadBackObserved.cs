using Constitution.Domain.ReadBack;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// Told by <see cref="ReadBackService"/> of a read whose page is being KEPT, not settled: some line is still waiting for its deadline (a meal sheet
/// with untouched lines), or a box was not read. What the household has already said on it (a tick, a written no) was said on time, and is to be
/// written down now with this morning's reading time rather than on the morning the page is finally let go, when the same ink would read as late.
/// A failure keeps the page, as it is kept anyway, and is reported at the persist step. Implemented beside <see cref="IReadBackSettled"/> by the hook
/// that has something to say; the hook passed to the service is asked whether it is one.
/// </summary>
public interface IReadBackObserved
{
    void Observed(ExpectedDocument document, DocumentResult result);
}
