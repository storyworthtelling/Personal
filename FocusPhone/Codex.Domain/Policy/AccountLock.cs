namespace Codex.Domain.Policy;

/// <summary>An account type the device knows, and the package whose authenticator registers it.</summary>
public record AccountAuthenticator(string Type, string Package);

/// <summary>
/// How accounts are held outside an Admin session. Accounts are added and removed only in Admin mode
/// (owner's ruling, audit W7), which the device-wide <c>no_modify_accounts</c> restriction enforces.
/// An app that signs in by registering an account of its own (Cigna) cannot sign in under that
/// restriction, and Android has no per-app exception to it, so while such an app is installed the
/// restriction gives way to a per-type lock (<c>setAccountManagementDisabled</c>) on every other
/// account type the device knows. A type any non-exempt package also registers is not exempt.
/// </summary>
public static class AccountLock
{
    // The trailing dot keeps look-alike namespaces out. Cigna's own apps (myCigna, Envoy) sign in by
    // adding a Cigna account; the exact package id is not confirmed, so the namespace is matched.
    public static readonly IReadOnlyList<string> ExemptPackagePrefixes = ["com.cigna."];

    public static bool IsExempt(string package) =>
        ExemptPackagePrefixes.Any(prefix => package.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// Whether the device-wide restriction applies, and which account types to lock one by one
    /// instead. In Admin mode neither: accounts may be changed freely.
    /// </summary>
    public static (bool RestrictAll, IReadOnlySet<string> LockedTypes) Decide(
        bool adminModeActive, IReadOnlyCollection<AccountAuthenticator> authenticators)
    {
        if (adminModeActive)
        {
            return (false, new HashSet<string>());
        }

        var exemptTypes = authenticators
            .GroupBy(a => a.Type, StringComparer.Ordinal)
            .Where(g => g.All(a => IsExempt(a.Package)))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (exemptTypes.Count == 0)
        {
            return (true, new HashSet<string>());
        }

        var locked = authenticators
            .Select(a => a.Type)
            .Where(t => !exemptTypes.Contains(t))
            .ToHashSet(StringComparer.Ordinal);
        return (false, locked);
    }
}
