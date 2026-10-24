using Constitution.Domain.Budgets;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.Workouts;

namespace Constitution.Infrastructure.Budgets;

/// <summary>
/// The latest-per-month budgets in one JSON file. A file that cannot be read is renamed beside where it was
/// (<see cref="UnreadableCopies"/>), never deleted and never written over, so the bytes survive for the owner; what is
/// received next starts a fresh file. A missing file is simply nothing known yet. A disk that cannot be read or written throws
/// with its own message rather than being taken for an empty file.
/// </summary>
public sealed class FileMonthlyBudgets : IMonthlyBudgets
{
    public const string FileName = "monthly-budgets.json";

    private static readonly object FileLock = new();

    private readonly string path;

    public FileMonthlyBudgets(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public MonthlyBudgets Load()
    {
        lock (FileLock)
        {
            return Read();
        }
    }

    public void Receive(BudgetPayload payload, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(payload);
        lock (FileLock)
        {
            var kept = Read();
            var next = kept.Apply(payload, receivedAt);
            if (!ReferenceEquals(next, kept))
            {
                AtomicFile.WriteAllText(path, MonthlyBudgetsCodec.Encode(next));
            }
        }
    }

    private MonthlyBudgets Read()
    {
        if (!File.Exists(path))
        {
            return MonthlyBudgets.None;
        }

        try
        {
            return MonthlyBudgetsCodec.Decode(File.ReadAllText(path));
        }
        catch (FormatException)
        {
            UnreadableCopies.SetAside(path);
            return MonthlyBudgets.None;
        }
    }
}
