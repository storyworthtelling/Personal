using Constitution.Domain.Body;
using Constitution.Domain.Health;
using Constitution.Infrastructure.Vault;

namespace Constitution.Infrastructure.Body;

/// <summary>
/// This person's <see cref="HealthChoices"/>, kept in the connections' <c>HealthConnectChoicesJson</c> and published to
/// the personal vault (PC3) when they change. Publishing goes through a delegate (the vault runner's
/// <c>PublishAsync</c> on the device) so this class is tested without a Drive.
/// </summary>
public sealed class HealthChoicesStore
{
    private readonly IConstitutionConnections connections;
    private readonly Func<IReadOnlyCollection<VaultField>, CancellationToken, Task<ConstitutionVaultRunResult>> publish;
    private readonly IBodyLog log;

    public HealthChoicesStore(
        IConstitutionConnections connections,
        Func<IReadOnlyCollection<VaultField>, CancellationToken, Task<ConstitutionVaultRunResult>> publish,
        IBodyLog log)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(log);
        this.connections = connections;
        this.publish = publish;
        this.log = log;
    }

    /// <summary>The saved choices; empty when none, or when what is saved cannot be read.</summary>
    public HealthChoices Load() => HealthChoices.FromJson(connections.HealthConnectChoicesJson);

    /// <summary>Saves the choices, and publishes only when they differ from what was already saved.</summary>
    public async Task SaveAsync(HealthChoices choices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choices);

        var json = choices.ToJson();
        if (string.Equals(json, connections.HealthConnectChoicesJson, StringComparison.Ordinal))
        {
            return;
        }

        connections.HealthConnectChoicesJson = json;
        var result = await publish([VaultField.HealthConnectChoices], cancellationToken).ConfigureAwait(false);
        log.Outcome($"health choices saved; vault publish {result?.Status.ToString() ?? "queued"}");
    }

    /// <summary>Brings the saved choices into line with what the platform grants now, and saves them if that changed anything.</summary>
    public Task ReconcileAsync(IHealthRecordSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        return SaveAsync(Load().Reconciled(source.IsGranted), cancellationToken);
    }
}
