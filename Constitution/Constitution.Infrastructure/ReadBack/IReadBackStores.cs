using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>A question the owner has not answered yet, with the document and page it came from and when it was read.</summary>
public sealed record PendingQuestion(string DocumentName, int PageNumber, Constitution.Domain.Ink.InkResolution.NeedsAsking Question, DateTimeOffset ReadAt);

/// <summary>Where a document's saved result lives until later features have used it. Saving the same document again replaces it.</summary>
public interface IResolutionStore
{
    void Save(DocumentResult result);

    /// <summary>The saved result, or null when there is none.</summary>
    DocumentResult? Load(string documentName);

    /// <summary>Questions saved with a result that the owner has not answered, so they can be put back on the Waiting page after the app restarts.</summary>
    IReadOnlyList<PendingQuestion> PendingQuestions();

    /// <summary>
    /// The owner picked <paramref name="chosen"/> for the question filed under this cause: it is no longer pending, and the
    /// pick is kept with the result that asked it, for whatever uses the answer.
    /// </summary>
    void MarkAnswered(string causeKey, string chosen);

    /// <summary>The picks the owner has made for the questions of this document, by the cause each was filed under.</summary>
    IReadOnlyDictionary<string, string> AnswersFor(string documentName);
}

/// <summary>The skip ledger's file. A ledger that cannot be read is reported by <see cref="SkipLedgerRead.WholeFileUnreadable"/>, and must not be written over.</summary>
public interface ILedgerStore
{
    SkipLedgerRead Load();

    void Save(SkipLedger ledger);
}

/// <summary>Whether pen ink has ever come through the cloud's PDF. Once it has, a document with no ink is the owner's silence and not a missing round trip (R3).</summary>
public interface IInkProof
{
    bool PenMarksSeen { get; }

    void RecordPenMarksSeen();
}

/// <summary>A delivered document waiting to be read, and whether its result is already saved so only the trash is left.</summary>
public sealed record OutstandingDocument(ExpectedDocument Document, bool Saved);

/// <summary>The documents delivered and not yet let go of. Delivery records one after the tablet takes it; the read-back retires it once the page is trashed.</summary>
public interface IExpectedDocuments
{
    IReadOnlyList<OutstandingDocument> Outstanding();

    void Record(ExpectedDocument document);

    /// <summary>The result is saved; only the trash is left to do.</summary>
    void MarkSaved(string documentName);

    void Retire(string documentName);
}

/// <summary>The read-back's trail: one value-free phrase per outcome (see <see cref="ReadBackTrail"/>).</summary>
public interface IReadBackLog
{
    void Outcome(string phrase);
}
