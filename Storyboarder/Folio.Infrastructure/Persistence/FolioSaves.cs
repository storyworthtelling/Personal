namespace Folio.Infrastructure.Persistence;

/// <summary>
/// How many times this process has saved anything to Folio's database (efficiency audit §6). The desk used to rebuild
/// every row from about ten queries per book every five seconds; it now refreshes when this has moved since its last
/// refresh. The pipeline worker runs in the app's own process, so its saves move it too.
/// </summary>
public static class FolioSaves
{
    private static long count;

    public static long Count => Interlocked.Read(ref count);

    internal static void Moved() => Interlocked.Increment(ref count);
}
