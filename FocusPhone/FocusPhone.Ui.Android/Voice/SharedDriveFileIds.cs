using Storyworthtelling.Voice;

namespace FocusPhone.Ui.Voice;

// Drive-by-ID (#13) for the FocusPhone heads. Unlike the MAUI apps (which register the id-store in DI and
// seed/snapshot it through a VaultSync), the native heads construct their Drive stores ad-hoc in several
// helper classes across a long-lived process. This gives them one shared, process-wide id cache the helpers
// all pass to their DriveTextFile, so a well-known file the owner renames/moves is still found by its stable
// id. The launcher process is long-lived, so in-session capture is durable; seeding from the vault at launch
// picks up the ids the MAUI apps persisted for the shared files (voice profile + secrets vault).
public static class SharedDriveFileIds
{
    public static InMemoryDriveFileIdStore Store { get; } = new();

    // Copies any ids the vault carries into the shared store (called after a vault load). Safe to call
    // repeatedly; later reads re-capture ids by name if a file wasn't in the vault yet.
    public static void SeedFrom(IReadOnlyDictionary<string, string> vaultFileIds)
    {
        foreach (var (name, id) in vaultFileIds)
            Store.Set(name, id);
    }
}
