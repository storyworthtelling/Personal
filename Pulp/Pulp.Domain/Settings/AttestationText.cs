namespace Pulp.Domain.Settings;

/// <summary>
/// The service page's PIN-lock attestation (D210), in plain words. The tick is the owner's statement
/// that every adult profile on the service asks for a PIN, and the sentence beside it says what follows
/// from it, so it is never read as a preference: children may start titles only on ticked services.
/// </summary>
public static class AttestationText
{
    public const string Toggle = "Adult profiles are PIN-locked";

    public static string Statement(string serviceName) =>
        $"Tick this only if every adult profile on {Name(serviceName)} asks for a PIN. Pulp opens titles for "
        + "children only on services ticked here; on any other, nothing starts while a child is watching.";

    public static string KidsOnly(string serviceName) =>
        $"{Name(serviceName)} is a kids-only app, so children can start titles there without a tick.";

    private static string Name(string serviceName) =>
        string.IsNullOrWhiteSpace(serviceName)
            ? throw new ArgumentException("The attestation names the service it is about.", nameof(serviceName))
            : serviceName.Trim();
}
