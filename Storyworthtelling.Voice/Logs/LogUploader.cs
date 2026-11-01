using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Logs;

/// <summary>How one upload run went, for the worker's own log line and the Diagnostics status sentence.</summary>
public sealed record LogUploadResult(
    int Planned, int Confirmed, LogUploadRefusal? Refusal, string? Error, int Trashed = 0, string? TrashError = null)
{
    /// <summary>Every planned batch reached Drive and read back, and the run was not refused.</summary>
    public bool Succeeded => Refusal is null && Error is null && Confirmed == Planned;

    public string? StatusSentence(int consecutiveFailedWeeks) => LogUploadStatus.Sentence(Refusal, consecutiveFailedWeeks);
}

/// <summary>
/// Carries <see cref="LogUploadPlan"/> out against a Drive folder (LOG-LOOP-PLAN.md §3.3) — the body of
/// every head's <c>LogUploadWorker</c> and of <i>Send logs now</i>. It seals the live logs, builds each
/// batch, seals it with VaultEnvelope under the derived log key (never the passphrase), creates it, reads
/// it back by id, and deletes exactly the segments of batches whose read-back matched. Any failure stops
/// the run and leaves everything in place for the next one.
/// </summary>
public sealed class LogUploader(IDriveLogFolder folder, LogOutbox outbox, LogInstall install)
{
    // A revision past this is not a plausible crash-and-retry history; stop probing rather than loop.
    private const int MaxRevisionProbe = 20;

    public async Task<LogUploadResult> RunAsync(IEnumerable<string> liveLogPaths, string? passphrase, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return new LogUploadResult(0, 0, LogUploadRefusal.WaitingForPassphrase, null);
        }

        var (trashed, trashError) = await TrashProcessedAsync(cancellationToken);

        outbox.SealAll(liveLogPaths);
        var run = LogUploadPlan.Decide(outbox.Segments(), LogKeyState.Ready, install.App, install.InstallId);
        if (run.Batches.Count == 0)
        {
            return new LogUploadResult(0, 0, run.Refusal, null, trashed, trashError);
        }

        var key = LogKey.Derive(passphrase, LogKey.Epoch);
        var outcomes = new List<BatchOutcome>();
        string? error = null;
        foreach (var planned in run.Batches)
        {
            try
            {
                outcomes.Add(new BatchOutcome(planned, await UploadAsync(planned, key, cancellationToken)));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Stop at the first failure: the next batch would go the same way, and what is left
                // stays sealed for tomorrow. The cause travels with the result for the worker's log line.
                error = $"{planned.Name}: {e.GetType().Name}: {e.Message}";
                outcomes.Add(new BatchOutcome(planned, false));
                break;
            }
        }

        outbox.Delete(LogUploadPlan.ToClear(outcomes));
        var confirmed = outcomes.Count(o => o.Confirmed);
        if (error is null && confirmed < run.Batches.Count)
        {
            error = $"{run.Batches.Count - confirmed} of {run.Batches.Count} batches did not read back the same.";
        }
        return new LogUploadResult(run.Batches.Count, confirmed, null, error, trashed, trashError);
    }

    // Her phone's files are hers, so the reader cannot trash them; it lists what it processed and this phone
    // trashes its own (§5.6). Only this install's files, only those listed. A failure here is reported and
    // never stops the upload: the files simply wait for next week's list.
    private async Task<(int Trashed, string? Error)> TrashProcessedAsync(CancellationToken cancellationToken)
    {
        var mine = $"{LogUploadPlan.NamePrefix}{install.App}.{install.InstallId}.";
        var trashed = 0;
        try
        {
            foreach (var name in (await folder.ProcessedNamesAsync(cancellationToken)).Where(n => n.StartsWith(mine, StringComparison.Ordinal)))
            {
                await folder.TrashOwnAsync(name, cancellationToken);
                trashed++;
            }
            return (trashed, null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (trashed, $"{e.GetType().Name}: {e.Message}");
        }
    }

    private async Task<bool> UploadAsync(PlannedBatch planned, string key, CancellationToken cancellationToken)
    {
        var batch = LogBatch.Build(install, planned.Segments, outbox.ReadText);
        var localSha = LogBatchCodec.Sha256(batch);

        var existing = new List<RemoteCopy>();
        for (var revision = 1; revision <= MaxRevisionProbe; revision++)
        {
            var name = revision == 1 ? planned.Name : LogUploadPlan.RevisionName(planned.Name, revision);
            var id = await folder.FindAsync(name, cancellationToken);
            if (id is null)
            {
                break;
            }
            existing.Add(new RemoteCopy(name, ShaOf(await folder.ReadAsync(id, cancellationToken), key)));
        }

        var action = LogUploadPlan.ForBatch(planned.Name, localSha, existing);
        if (action.ClearsWithoutReadBack)
        {
            return true;
        }

        var createdId = await folder.CreateAsync(action.Name, VaultEnvelope.Write(LogBatchCodec.Encode(batch), key), cancellationToken);
        var readBack = await folder.ReadAsync(createdId, cancellationToken);
        return LogUploadPlan.Confirmed(localSha, ShaOf(readBack, key));
    }

    // The content hash of a sealed batch, or null when it does not decrypt or does not decode.
    private static string? ShaOf(string? sealedContent, string key)
    {
        var (status, plaintext) = VaultEnvelope.Read(sealedContent, key);
        if (status != SecretsVaultStatus.Loaded || plaintext is null)
        {
            return null;
        }
        try
        {
            return LogBatchCodec.Sha256(LogBatchCodec.Decode(plaintext));
        }
        catch (LogBatchFormatException)
        {
            return null;
        }
    }
}
