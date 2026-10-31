using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// Each document's saved result in a file of its own in one folder of the app's private storage. This is what
/// "saved" means before a page is trashed: later features read the answers from here, and a question the owner has not
/// answered waits here until the app can put it back on the Waiting page.
///
/// <para><b>Answered is per result.</b> Answering a question marks the questions with that cause in the results that
/// hold them <i>now</i>; the same question asked of a later document is a new question. A result saved again (the page
/// was read a second time) keeps the answers already given. An unreadable file is set aside (see
/// <see cref="StoredFiles"/>), and the page it was for is then read again.</para>
///
/// <para>The files hold the owner's answers (words they wrote, ticks, numbers) and stay in private storage that is
/// excluded from cloud backup. Nothing here is logged.</para>
/// </summary>
public sealed class FileResolutionStore : IResolutionStore
{
    private static readonly object FileLock = new();

    private readonly string folder;

    public FileResolutionStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        this.folder = folder;
    }

    public void Save(DocumentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (FileLock)
        {
            var path = PathFor(result.Name);
            var answered = ReadFile(path)?.Answered ?? new Dictionary<string, string>();
            AtomicFile.WriteAllText(path, DocumentResultCodec.Encode(result, answered));
        }
    }

    public DocumentResult? Load(string documentName)
    {
        lock (FileLock)
        {
            return ReadFile(PathFor(documentName))?.Result;
        }
    }

    public IReadOnlyList<PendingQuestion> PendingQuestions()
    {
        lock (FileLock)
        {
            return AllFiles()
                .SelectMany(stored => stored.Result.Pages.SelectMany(page => page.Resolutions
                    .OfType<Constitution.Domain.Ink.InkResolution.NeedsAsking>()
                    .Where(question => !stored.Answered.ContainsKey(question.CauseKey))
                    .Select(question => new PendingQuestion(stored.Result.Name, page.Number, question, stored.Result.ReadAt))))
                .OrderBy(pending => pending.ReadAt)
                .ThenBy(pending => pending.DocumentName, StringComparer.Ordinal)
                .ThenBy(pending => pending.PageNumber)
                .ToList();
        }
    }

    public void MarkAnswered(string causeKey, string chosen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(causeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(chosen);

        lock (FileLock)
        {
            foreach (var path in Files())
            {
                var stored = ReadFile(path);
                if (stored is null || stored.Answered.ContainsKey(causeKey))
                {
                    continue;
                }

                if (stored.Result.Questions.Any(question => question.CauseKey == causeKey))
                {
                    var answered = new Dictionary<string, string>(stored.Answered, StringComparer.Ordinal) { [causeKey] = chosen };
                    AtomicFile.WriteAllText(path, DocumentResultCodec.Encode(stored.Result, answered));
                }
            }
        }
    }

    public IReadOnlyDictionary<string, string> AnswersFor(string documentName)
    {
        lock (FileLock)
        {
            return ReadFile(PathFor(documentName))?.Answered ?? new Dictionary<string, string>();
        }
    }

    private string PathFor(string documentName) => Path.Combine(folder, StoredFiles.NameFor(documentName) + ".json");

    private IEnumerable<string> Files() =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal).ToList() : [];

    private IEnumerable<DocumentResultCodec.Stored> AllFiles() => Files().Select(ReadFile).OfType<DocumentResultCodec.Stored>().ToList();

    private static DocumentResultCodec.Stored? ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return DocumentResultCodec.Decode(File.ReadAllText(path));
        }
        catch (FormatException)
        {
            StoredFiles.SetAside(path);
            return null;
        }
    }
}
