using Index.Android.Platforms.Android;
using Index.Infrastructure.Usage;
using Microsoft.Extensions.Logging;

namespace Index.Android;

/// <summary>
/// Brings Pulp's usage file (Pulp D320) in from the household's Drive, on both installs: the broadcast reaches
/// only a phone Pulp also lives on, and this is how the line under a charge appears on the other one. Best
/// effort and quiet. Only a file that opened and read is kept; a wrong passphrase, a garbled file, no file or no
/// network leaves the last good copy exactly as it was. Throttled, because the page asks on every appearance.
/// </summary>
public sealed class PulpUsageSync(DrivePulpUsageSource source, ILogger<PulpUsageSync> logger)
{
    private readonly PulpUsageThrottle throttle = new();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!throttle.TryBegin(DateTime.UtcNow))
        {
            return;
        }

        var worked = false;
        try
        {
            var result = await source.ReadAsync(SettingsStore.VaultPassphrase, cancellationToken);
            worked = PulpUsageThrottle.Worked(result.Status);
            if (result.Status == PulpUsageReadStatus.Loaded && result.Ledger is not null)
            {
                PulpUsageStore.SaveShared(result.Ledger);
            }
            else
            {
                logger.LogInformation("Pulp usage file not used: {Status}", result.Status);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading Pulp's usage file failed; the last good copy stands.");
        }
        finally
        {
            // Only a read that worked earns the long wait; a failed or cancelled one is asked for again in two minutes.
            throttle.End(DateTime.UtcNow, worked);
        }
    }
}
