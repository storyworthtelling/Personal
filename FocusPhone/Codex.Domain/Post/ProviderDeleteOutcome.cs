namespace Codex.Domain.Post;

/// <summary>
/// Whether a delete from the system's own store (the call log, the SMS and MMS provider) is finished. A delete that
/// removed a row is. One that removed nothing is finished too when the row is no longer there, because the system may
/// have removed it first and then nothing is owed; only a row that is still present means the delete failed.
/// </summary>
public static class ProviderDeleteOutcome
{
    public static bool Settled(int rowsDeleted, Func<bool> rowStillExists) =>
        rowsDeleted > 0 || !rowStillExists();
}
