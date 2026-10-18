using Constitution.Domain.Credentials;
using Constitution.Domain.Delivery;
using Constitution.Domain.Notices;
using Storyworthtelling.Remarkable;

namespace Constitution.Infrastructure.Delivery;

/// <summary>
/// Puts each document on the tablet, at the account root, under <see cref="DeliveredName"/> (C-2.2.2).
/// The client it is given is already bound to this phone's own pairing (D85: the device token in the
/// vault, read live by the shared library), so there is no token here and none is read or logged.
///
/// <para><b>A failure is a notice, never a second route.</b> A refused token or a failed upload raises
/// the Problem notice (<see cref="NoticeKind.ReMarkableDelivery"/>, "Pair again") under the reMarkable
/// credential's cause, so re-pairing (<see cref="ConnectionNotices.Clear"/>) and a later success both
/// take it away. The document is kept in memory only, to be sent again by <see cref="RetryPending"/>;
/// nothing is written to the phone, shared, or opened in a viewer, because a copy of the owner's
/// pages on the phone is exactly what this device exists to avoid.</para>
///
/// <para>Not held across a process death: if Android reclaims the process while a document waits, the
/// next scheduled run builds and delivers it again, which costs a render, not a page.</para>
/// </summary>
public sealed class DeliveryService(IRemarkableClient remarkable, INoticeBoardAccess notices, Func<DateTimeOffset> clock)
{
    private readonly Dictionary<string, byte[]> pending = [];
    private readonly object gate = new();

    /// <summary>How many documents are waiting for the tablet to take them.</summary>
    public int PendingCount
    {
        get
        {
            lock (gate)
            {
                return pending.Count;
            }
        }
    }

    /// <summary>Uploads one document at the root. A failure is reported in the result and on the board, not thrown.</summary>
    public async Task<DeliveryResult> Deliver(DeliveryKind kind, DateOnly date, string? subject, byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var name = DeliveredName.For(kind, date, subject);
        var failure = await TryUpload(name, pdf);
        if (failure is null)
        {
            Settled(name);
            return new DeliveryResult(name, true, null);
        }

        lock (gate)
        {
            pending[name] = pdf;
        }

        notices.With(board => ConnectionNotices.Apply(board, CredentialIds.Remarkable, ConnectionHealth.Rejected, clock()));
        return new DeliveryResult(name, false, failure);
    }

    /// <summary>Sends every waiting document again, once the pairing may have been fixed. Returns how many the tablet took.</summary>
    public async Task<int> RetryPending()
    {
        KeyValuePair<string, byte[]>[] waiting;
        lock (gate)
        {
            waiting = pending.ToArray();
        }

        var delivered = 0;
        foreach (var (name, pdf) in waiting)
        {
            if (await TryUpload(name, pdf) is null)
            {
                Settled(name);
                delivered++;
            }
        }

        return delivered;
    }

    // Only the failures delivery can name come back; anything else (a bug, a caller's cancellation) propagates with its stack.
    private async Task<Exception?> TryUpload(string name, byte[] pdf)
    {
        try
        {
            await remarkable.Upload(name, pdf);
            return null;
        }
        catch (HttpRequestException refused)
        {
            return refused;
        }
        catch (TaskCanceledException timedOut) when (timedOut.InnerException is TimeoutException)
        {
            return timedOut;
        }
    }

    private void Settled(string name)
    {
        bool anyWaiting;
        lock (gate)
        {
            pending.Remove(name);
            anyWaiting = pending.Count > 0;
        }

        if (!anyWaiting)
        {
            notices.With(board => ConnectionNotices.Apply(board, CredentialIds.Remarkable, ConnectionHealth.Working, clock()));
        }
    }
}
