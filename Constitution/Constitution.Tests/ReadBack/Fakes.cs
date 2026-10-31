using Constitution.Domain.Notices;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Delivery;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Tests.ReadBack;

/// <summary>One shared record of what was called, in order, so a test can say "the trash came last".</summary>
internal sealed class CallLog
{
    public List<string> Calls { get; } = [];

    public void Add(string call) => Calls.Add(call);

    public int IndexOf(string call) => Calls.IndexOf(call);
}

internal sealed class FakeExpected(CallLog calls, params ExpectedDocument[] documents) : IExpectedDocuments
{
    private readonly List<OutstandingDocument> held = documents.Select(document => new OutstandingDocument(document, false)).ToList();

    public List<string> Retired { get; } = [];

    public List<string> Saved { get; } = [];

    public IReadOnlyList<OutstandingDocument> Outstanding() => held.ToList();

    public void Record(ExpectedDocument document) => held.Add(new OutstandingDocument(document, false));

    public void MarkSaved(string documentName)
    {
        calls.Add("MarkSaved");
        Saved.Add(documentName);
        var index = held.FindIndex(item => item.Document.Name == documentName);
        held[index] = held[index] with { Saved = true };
    }

    public void Retire(string documentName)
    {
        calls.Add("Retire");
        Retired.Add(documentName);
        held.RemoveAll(item => item.Document.Name == documentName);
    }
}

internal sealed class FakeTablet(CallLog calls) : IAnnotatedPageSource
{
    private readonly Dictionary<string, TabletDocument> onTablet = [];

    public byte[] Pdf { get; } = [37, 80, 68, 70];

    public Exception? DownloadFails { get; set; }

    /// <summary>Fails the download of particular titles only.</summary>
    public Func<string, Exception?>? DownloadFailsFor { get; set; }

    public List<string> Downloaded { get; } = [];

    public Exception? LocateFails { get; set; }

    public Exception? TrashFails { get; set; }

    public List<string> Trashed { get; } = [];

    public int Downloads { get; private set; }

    /// <summary>Puts a document on the tablet under the name delivery would have given it.</summary>
    public void Has(ExpectedDocument document) => onTablet[document.Name] = new TabletDocument("id-" + document.Name, document.Name, Pdf);

    /// <summary>Takes the document off the tablet again.</summary>
    public void Forget(ExpectedDocument document) => onTablet.Remove(document.Name);

    public Task<TabletDocument?> Download(string title)
    {
        calls.Add("Download");
        Downloads++;
        Downloaded.Add(title);
        if (DownloadFails is not null)
        {
            throw DownloadFails;
        }

        if (DownloadFailsFor?.Invoke(title) is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(onTablet.GetValueOrDefault(title));
    }

    public Task<string?> Locate(string title)
    {
        calls.Add("Locate");
        if (LocateFails is not null)
        {
            throw LocateFails;
        }

        return Task.FromResult(onTablet.TryGetValue(title, out var found) ? found.Id : null);
    }

    public Task Trash(string documentId)
    {
        calls.Add("Trash");
        if (TrashFails is not null)
        {
            throw TrashFails;
        }

        Trashed.Add(documentId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeVision(CallLog calls) : IInkVision
{
    public Func<ExpectedDocument, InkDocumentRead>? Reply { get; set; }

    public Exception? Fails { get; set; }

    public int Reads { get; private set; }

    public byte[]? LastPdf { get; private set; }

    public ExpectedDocument? LastDocument { get; private set; }

    public Task<InkDocumentRead> Read(ExpectedDocument document, byte[] annotatedPdf, CancellationToken cancellation)
    {
        calls.Add("Read");
        Reads++;
        LastPdf = annotatedPdf;
        LastDocument = document;
        if (Fails is not null)
        {
            throw Fails;
        }

        return Task.FromResult(Reply!(document));
    }
}

internal sealed class FakeResults(CallLog calls) : IResolutionStore
{
    private readonly Dictionary<string, DocumentResult> saved = [];

    public Exception? SaveFails { get; set; }

    public int Saves { get; private set; }

    public void Save(DocumentResult result)
    {
        calls.Add("SaveResult");
        if (SaveFails is not null)
        {
            throw SaveFails;
        }

        Saves++;
        saved[result.Name] = result;
    }

    public DocumentResult? Load(string documentName) => saved.GetValueOrDefault(documentName);

    /// <summary>Loses a saved result, as a store the app could not read would.</summary>
    public void Forget(string documentName) => saved.Remove(documentName);

    public IReadOnlyList<PendingQuestion> PendingQuestions() => [];

    public void MarkAnswered(string causeKey, string chosen)
    {
    }

    public IReadOnlyDictionary<string, string> AnswersFor(string documentName) => new Dictionary<string, string>();
}

internal sealed class FakeLedger(CallLog calls) : ILedgerStore
{
    public SkipLedger Held { get; private set; } = SkipLedger.Empty;

    public bool Unreadable { get; set; }

    /// <summary>Entries the file held that this version could not read, as a ledger with an entry of a kind it does not know would report.</summary>
    public int UnreadableEntries { get; set; }

    public Exception? SaveFails { get; set; }

    public int Saves { get; private set; }

    public SkipLedgerRead Load()
    {
        calls.Add("LoadLedger");
        return Unreadable ? new SkipLedgerRead(SkipLedger.Empty, 0, true) : new SkipLedgerRead(Held, UnreadableEntries, false);
    }

    public void Save(SkipLedger ledger)
    {
        calls.Add("SaveLedger");
        if (SaveFails is not null)
        {
            throw SaveFails;
        }

        Saves++;
        Held = ledger;
    }
}

internal sealed class FakeProof(CallLog calls, bool seen = false) : IInkProof
{
    public InkProof Current { get; set; } = seen ? InkProof.EstablishedEarlier : InkProof.None;

    public bool PenMarksSeen => Current.IsEstablished;

    public List<InkSighting> Recorded { get; } = [];

    public InkProof Record(InkSighting sighting)
    {
        calls.Add("RecordProof");
        Recorded.Add(sighting);
        Current = Current.Record(sighting);
        return Current;
    }
}

internal sealed class FakeLog : IReadBackLog
{
    public List<string> Phrases { get; } = [];

    public void Outcome(string phrase) => Phrases.Add(phrase);
}

internal sealed class FakeBoard(CallLog calls) : INoticeBoardAccess
{
    public NoticeBoard Board { get; } = new();

    public Exception? Fails { get; set; }

    public T With<T>(Func<NoticeBoard, T> use)
    {
        calls.Add("Board");
        if (Fails is not null)
        {
            throw Fails;
        }

        return use(Board);
    }
}
