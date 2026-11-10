namespace Storyworthtelling.Diagnostics;

/// <summary>
/// The sources every app shares (audit 08 §31). Pulp wrote "vault" where the others wrote "Vault", and a contract line
/// went under its app's name, so the digest could not group one fault across apps. A source is one word, which the
/// reader splits a line on, so a name with a space is joined with a hyphen.
/// </summary>
public static class LogSources
{
    public const string Vault = "Vault";
    public const string Drive = "Drive";
    public const string Worker = "Worker";
    public const string Permission = "Permission";
    public const string Crash = "Crash";
    public const string Exit = "Exit";

    /// <summary>A call to an outside service: <c>Http:plaid</c>.</summary>
    public static string Http(string service) => "Http:" + Word(service);

    /// <summary>A cross-app contract's send or receipt: <c>Contract:usage</c>.</summary>
    public static string Contract(string name) => "Contract:" + Word(name);

    private static string Word(string name) => string.Join('-', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
