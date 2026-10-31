using Microsoft.Extensions.Logging;
using Storyworthtelling.Voice;

namespace Constitution.Tests.Voice;

/// <summary>
/// The fakes and fixtures the voice-seat tests share. Everything here is invented; the facts are
/// the kind of already-reduced sentences a seat is handed (the page-18 examples), never a figure
/// or a name the seat must not say.
/// </summary>
internal static class VoiceTestSupport
{
    public const string Sentinel = "SENTINEL-PROFILE-REGISTER";

    public const string Reply = "Strength at five-thirty, and soup waiting after.";

    public static VoiceProfile Profile() =>
        VoiceProfile.Empty with { Register = Sentinel, DocumentsAnalyzed = 1 };
}

internal sealed class FakeProfileStore(VoiceProfile profile) : IVoiceProfileStore
{
    public Exception? Failure { get; set; }

    public Task<VoiceProfile> LoadAsync(CancellationToken cancellationToken) =>
        Failure is null ? Task.FromResult(profile) : Task.FromException<VoiceProfile>(Failure);

    public Task SaveAsync(VoiceProfile saved, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeConfigStore(VoiceConfig config) : IVoiceConfigStore
{
    public Exception? Failure { get; set; }

    public Task<VoiceConfig> LoadAsync(CancellationToken cancellationToken) =>
        Failure is null ? Task.FromResult(config) : Task.FromException<VoiceConfig>(Failure);

    public Task SaveAsync(VoiceConfig saved, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Records every request the composer would send, and answers (or fails) as told.</summary>
internal sealed class RecordingVoiceClient : IVoiceClient
{
    public List<MessageRequest> Requests { get; } = [];

    public string Reply { get; set; } = VoiceTestSupport.Reply;

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

/// <summary>Keeps every formatted log message and structured value, so a test can prove nothing of
/// the facts, the fallback, the profile or the sentence reached the log.</summary>
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
