using Constitution.Domain.Voice;

namespace Constitution.Infrastructure.Voice;

/// <summary>
/// Something that turns a seat's request into its line: <see cref="SeatComposer"/>, and a fake in the
/// cache's tests. Never hidden, never blank, never an error; cancellation is the caller leaving.
/// </summary>
public interface ISeatComposer
{
    Task<SeatLine> ComposeAsync(SeatRequest request, CancellationToken cancellationToken);
}
