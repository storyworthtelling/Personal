using Codex.Domain.Post;
using Microsoft.Data.Sqlite;

namespace Codex.Infrastructure.Storage;

/// <summary>
/// What each email was judged to be, by source id, in a table of its own. The timeline cache
/// cannot hold this: aged-out mail is deleted from it while still at the source, and every
/// reconcile (thirty days, after every file and delete) re-fetches it unsorted. Without this
/// record each of those messages went back to Claude, and was billed again, every time.
/// </summary>
public sealed class MailJudgementStore
{
    /// <summary>Past the thirty-day reconcile window, so nothing still re-fetched is forgotten.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(60);

    private readonly SqliteConnection connection;

    public MailJudgementStore(SqliteConnection connection)
    {
        this.connection = connection;
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS mailJudgements (
                sourceId TEXT NOT NULL PRIMARY KEY,
                mailClass INTEGER NOT NULL,
                judgedAtUtc TEXT NOT NULL
            );
            """;
        create.ExecuteNonQuery();
    }

    public MailClass? Find(string sourceId)
    {
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT mailClass FROM mailJudgements WHERE sourceId = $id";
        select.Parameters.AddWithValue("$id", sourceId);
        return select.ExecuteScalar() is long value ? (MailClass)value : null;
    }

    /// <summary>The first judgement stands; the owner's corrections apply over it when it is read.</summary>
    public void Remember(string sourceId, MailClass kind, DateTimeOffset judgedAtUtc)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO mailJudgements (sourceId, mailClass, judgedAtUtc) VALUES ($id, $class, $at)
            ON CONFLICT(sourceId) DO NOTHING
            """;
        insert.Parameters.AddWithValue("$id", sourceId);
        insert.Parameters.AddWithValue("$class", (int)kind);
        insert.Parameters.AddWithValue("$at", judgedAtUtc.ToUniversalTime().ToString("O"));
        insert.ExecuteNonQuery();
    }

    public void ForgetBefore(DateTimeOffset cutoffUtc)
    {
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM mailJudgements WHERE judgedAtUtc < $cutoff";
        delete.Parameters.AddWithValue("$cutoff", cutoffUtc.ToUniversalTime().ToString("O"));
        delete.ExecuteNonQuery();
    }
}
