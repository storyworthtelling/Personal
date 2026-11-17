using Constitution.Domain.Voice;

namespace Constitution.Infrastructure.Voice;

/// <summary>
/// Something that turns a seat's request into its line in the owner's voice: <see cref="SeatComposer"/>, and a fake
/// in the cache's tests. Null when there is no voice, and the seat hides; never an error. Cancellation is the
/// caller leaving.
/// </summary>
public interface ISeatComposer
{
    Task<SeatLine?> ComposeAsync(SeatRequest request, CancellationToken cancellationToken);
}
