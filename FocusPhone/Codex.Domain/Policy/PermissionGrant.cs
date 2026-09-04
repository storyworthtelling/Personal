namespace Codex.Domain.Policy;

/// <summary>
/// A runtime permission the device owner grants to a managed app on every policy
/// pass. Used for the hard-restricted permissions (READ_SMS, READ_CALL_LOG) that
/// the installer only whitelists — whitelisting un-restricts them, but they still
/// have to be granted, and a device-owner grant is the only path that is
/// dialog-free, survives silent reinstalls through the update channel, and is
/// pinned against Android's auto-reset of unused-app permissions.
/// </summary>
public record PermissionGrant(string Package, string Permission);
