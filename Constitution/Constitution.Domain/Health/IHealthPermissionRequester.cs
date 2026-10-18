using Constitution.Domain.Body;

namespace Constitution.Domain.Health;

/// <summary>
/// The seam a signal's chart will ask through the first time it opens (C-7.3.5). It is only the seam: nothing in
/// the reader or the sync calls it, because reading is never a reason to put a permission dialog on the screen.
/// The Android head's implementation, when it exists, shows Health Connect's own request for the signal's
/// permissions and says only that Constitution reads.
/// </summary>
public interface IHealthPermissionRequester
{
    /// <summary>Asks for the signal's read permissions; true when the signal is readable afterwards.</summary>
    Task<bool> RequestAsync(BodySignal signal, CancellationToken cancellationToken);
}
