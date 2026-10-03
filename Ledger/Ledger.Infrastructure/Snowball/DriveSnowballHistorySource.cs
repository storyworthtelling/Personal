using Index.Domain.Model;
using Storyworthtelling.Voice;

namespace Index.Infrastructure.Snowball;

/// <summary>
/// <see cref="ISnowballHistorySource"/> over the owner's own Drive: the recorded Debt Snowball history,
/// sealed with the vault passphrase by <c>Tools/IndexHistorySeal</c> and uploaded to the Drive root as
/// <c>storyworthtelling-index-snowball-history.json</c>. The real figures are therefore never in the
/// repository or in the APK — they are read here, once, on the owner's install.
///
/// <para>Constructed over an <see cref="IDriveTextFile"/> that was itself built for THIS file name. It must
/// never be resolved from the container: a bare <c>IDriveTextFile</c> is already registered for the
/// household snapshot, and handing that one here would read the wrong file (guarded by
/// <c>SnowballHistoryNotInTheRepoTests</c>).</para>
/// </summary>
public sealed class DriveSnowballHistorySource : ISnowballHistorySource
{
    public const string FileName = "storyworthtelling-index-snowball-history.json";

    private readonly IDriveTextFile file;

    public DriveSnowballHistorySource(IDriveTextFile file)
    {
        this.file = file;
    }

    public async Task<SnowballHistoryReadResult> ReadAsync(string? passphrase, CancellationToken cancellationToken)
    {
        // Without a passphrase nothing can be opened, so Drive is not even asked.
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return SnowballHistoryReadResult.NeedsPassphrase;
        }

        string? content;
        try
        {
            content = await file.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Drive being unreachable (offline, signed out, a quota) is "cannot tell right now": the reason
            // travels in the result for the log, and nothing is thrown past the seam.
            return SnowballHistoryReadResult.Unreadable($"{ex.GetType().Name}: {ex.Message}");
        }

        return SnowballHistorySealer.Open(content, passphrase);
    }
}
