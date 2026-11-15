namespace Storyworthtelling.Updates;

/// <summary>
/// The one Settings anatomy in all nine apps (audit W8.4b, report 02 §9; owner's ruling: folding groups
/// everywhere, drawn as Pulp's ruled rows): Connections (the accounts, keys and pairings that need work),
/// the Vault, the app's own groups, Diagnostics, and under them the build line (<see cref="BuildLine"/>).
/// It sits beside the build line because every head already references this library.
/// </summary>
public static class SettingsAnatomy
{
    public const string Connections = "Connections";
    public const string Vault = "Vault";
    public const string Diagnostics = "Diagnostics";

    /// <summary>The groups in the suite's order. An app with nothing to connect starts at the Vault.</summary>
    public static IReadOnlyList<string> Arrange(bool hasConnections, IEnumerable<string> appGroups)
    {
        ArgumentNullException.ThrowIfNull(appGroups);
        var own = appGroups.ToList();
        if (own.FirstOrDefault(IsShared) is { } taken)
        {
            throw new ArgumentException($"\"{taken}\" is one of the suite's own groups; an app group needs a name of its own.", nameof(appGroups));
        }

        var order = new List<string>();
        if (hasConnections)
        {
            order.Add(Connections);
        }

        order.Add(Vault);
        order.AddRange(own);
        order.Add(Diagnostics);
        return order;
    }

    /// <summary>
    /// Whether groups as drawn follow the suite's order: Connections first if there is one, then the Vault,
    /// the app's own, and Diagnostics last.
    /// </summary>
    public static bool IsInOrder(IReadOnlyList<string> drawn)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        var start = drawn.Count > 0 && drawn[0] == Connections ? 1 : 0;
        if (drawn.Count < start + 2 || drawn[start] != Vault || drawn[^1] != Diagnostics)
        {
            return false;
        }

        return drawn.Skip(start + 1).Take(drawn.Count - start - 2).All(group => !IsShared(group));
    }

    private static bool IsShared(string group) => group is Connections or Vault or Diagnostics;
}
