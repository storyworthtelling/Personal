using Microsoft.Extensions.Logging;
using Index.Domain.Household;
using Storyworthtelling.Voice;

namespace Index.Tests.Voice;

/// <summary>
/// The fakes and fixtures the voice-seat tests share. The fixtures are deliberately full of the things a
/// seat must never say — bank names, balances to the cent, a goal, a mask — so that "the prompt carries
/// none of them" is a statement about a prompt that had every chance to.
/// </summary>
internal static class VoiceTestSupport
{
    public const string Sentinel = "SENTINEL-PROFILE-REGISTER";

    /// <summary>Peak 100,000.50 in Nov 2025, this week 88,000.00 (twelve percent down); a week in August 2026
    /// at 90,000.00; three loans already paid off. Names and balances are the real-looking kind.</summary>
    public static HouseholdSnowballHistory TwelvePercentDown() => History(
        [new(2025, 11, 7), new(2026, 1, 9), new(2026, 8, 7), new(2026, 9, 4), new(2026, 9, 25)],
        ("Redwood Bank — Bluebird", false, [60000.50m, 58000m, 54000m, 53000.25m, 52800m]),
        ("Meridian Card", false, [40000m, 39000m, 36000m, 35500m, 35200m]),
        ("Harbor Auto Finance", true, [0m, 0m, 0m, 0m, 0m]),
        ("Northfield Student Loans 2", true, [0m, 0m, 0m, 0m, 0m]),
        ("Cedar Hollow Bank", true, [0m, 0m, 0m, 0m, 0m]));

    public static HouseholdSnowballHistory History(
        DateOnly[] weeks, params (string Name, bool PaidOff, decimal[] Balances)[] loans) =>
        new(weeks, loans.Select(loan =>
            new HouseholdSnowballLoanHistory(loan.Name, IsMortgage: false, loan.PaidOff, loan.Balances)).ToList());

    /// <summary>One loan whose total runs: week 1 at 'then', the August week at 'then', now at 'now' — for the
    /// Accounts trend, where only the August-to-now change matters.</summary>
    public static HouseholdSnowballHistory TotalsThenNow(decimal august, decimal now) => History(
        [new(2026, 7, 10), new(2026, 8, 7), new(2026, 9, 4), new(2026, 9, 25)],
        ("Redwood Bank — Bluebird", false, [august, august, (august + now) / 2m, now]));

    /// <summary>Two savings accounts with balances, a goal and bank names — a combined pace of 219.50.</summary>
    public static IReadOnlyList<HouseholdSavingsAccount> SavingsAccounts() =>
    [
        new("Redwood Bank", "Savings", 8200m, 14000m, 187.50m, 12),
        new("Lakeside CU", "Share savings", 1350m, null, 32m, 12),
    ];

    public static VoiceProfile Profile() =>
        VoiceProfile.Empty with { Register = Sentinel, DocumentsAnalyzed = 1 };
}

internal sealed class FakeProfileStore(VoiceProfile profile) : IVoiceProfileStore
{
    public Task<VoiceProfile> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(profile);

    public Task SaveAsync(VoiceProfile saved, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeConfigStore(VoiceConfig config) : IVoiceConfigStore
{
    public Task<VoiceConfig> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(config);

    public Task SaveAsync(VoiceConfig saved, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Records every request the composer would send, and answers (or fails) as told.</summary>
internal sealed class RecordingVoiceClient : IVoiceClient
{
    public List<MessageRequest> Requests { get; } = [];

    public string Reply { get; set; } = "Twelve percent down since last November's peak.";

    public Exception? Failure { get; set; }

    public Task<VoiceProfile> RefineAsync(
        VoiceProfile existing, string documentText, Guid projectId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A seat never refines the voice.");

    public Task<string> ComposeAsync(VoiceProfile profile, MessageRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Failure is null ? Task.FromResult(Reply) : Task.FromException<string>(Failure);
    }
}

/// <summary>Keeps every formatted log message and every structured value, so a test can prove nothing of
/// the prompt, the profile or the sentence reached the log.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(formatter(state, exception));
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            Entries.AddRange(values.Select(pair => $"{pair.Key}={pair.Value}"));
        }
        if (exception is not null)
        {
            Entries.Add(exception.ToString());
        }
    }
}
