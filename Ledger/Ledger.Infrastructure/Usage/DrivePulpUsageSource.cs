using Index.Domain.Usage;
using Microsoft.Extensions.Logging;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Index.Infrastructure.Usage;

public enum PulpUsageReadStatus
{
    Loaded,
    Absent,
    NeedsPassphrase,
    WrongPassphrase,
    Corrupt,
    Unreadable,
}

/// <summary>What reading Pulp's usage file came to. Only <see cref="PulpUsageReadStatus.Loaded"/> carries a ledger; every other status means "keep what you had".</summary>
public sealed record PulpUsageReadResult(PulpUsageReadStatus Status, PulpUsageLedger? Ledger = null);

/// <summary>
/// Pulp's usage file over the owner's household Drive (Pulp D320), opened with the vault passphrase through the
/// same <see cref="VaultEnvelope"/> path every other sealed file uses. Read-only, and it reads nothing else: this
/// is the one place both installs pick the usage up, so it must work on an install with no banks and no share of
/// the owner's vault section. Constructed over an <see cref="IDriveTextFile"/> built for THIS file name; never
/// resolved bare from the container, which already holds the household snapshot's. Never throws past the seam.
/// </summary>
public sealed class DrivePulpUsageSource
{
    public const string FileName = "storyworthtelling-pulp-usage.json";

    private readonly IDriveTextFile file;
    private readonly ILogger<DrivePulpUsageSource>? logger;

    public DrivePulpUsageSource(IDriveTextFile file, ILogger<DrivePulpUsageSource>? logger = null)
    {
        this.file = file;
        this.logger = logger;
    }

    public async Task<PulpUsageReadResult> ReadAsync(string? passphrase, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return new PulpUsageReadResult(PulpUsageReadStatus.NeedsPassphrase);
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
        catch (Exception exception)
        {
            // Still a status and never a throw, but not a silent one: why Drive could not be read is the only trace of it.
            logger?.LogWarning(exception, "Pulp's usage file could not be read from Drive.");
            return new PulpUsageReadResult(PulpUsageReadStatus.Unreadable);
        }

        var (status, plaintext) = VaultEnvelope.Read(content, passphrase);
        switch (status)
        {
            case SecretsVaultStatus.Empty:
                return new PulpUsageReadResult(PulpUsageReadStatus.Absent);
            case SecretsVaultStatus.WrongPassphrase:
                return new PulpUsageReadResult(PulpUsageReadStatus.WrongPassphrase);
            case SecretsVaultStatus.Loaded:
                return PulpUsageFile.Decode(plaintext) is { } ledger
                    ? new PulpUsageReadResult(PulpUsageReadStatus.Loaded, ledger)
                    : new PulpUsageReadResult(PulpUsageReadStatus.Corrupt);
            default:
                return new PulpUsageReadResult(PulpUsageReadStatus.Corrupt);
        }
    }
}
