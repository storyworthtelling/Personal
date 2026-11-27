namespace Watch.Domain.Dash;

/// <summary>A phone that advertises Post's relay (<see cref="WatchIdentity.PostCapability"/>), as Play services reports it.</summary>
public sealed record RelayNode(string? Id, bool Nearby);

/// <summary>
/// Which phone the wrist asks (W-5.1.1, R3): one near over Bluetooth before one reached through the cloud, and among
/// equals the same one each time, whatever order Play services lists them in. None is said, not waited for.
/// </summary>
public static class RelayNodes
{
    public static string? Choose(IEnumerable<RelayNode> nodes) =>
        nodes
            .Where(node => !string.IsNullOrEmpty(node.Id))
            .OrderByDescending(node => node.Nearby)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .Select(node => node.Id)
            .FirstOrDefault();
}
