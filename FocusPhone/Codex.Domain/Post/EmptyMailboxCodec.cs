using System.Text.Json;

namespace Codex.Domain.Post;

/// <summary>
/// Persists the empty-Mailbox pool's state as JSON. The ladder counts visits across
/// launches, so a state that did not survive the process would never reach its second
/// rung — but it is still only decoration, so anything unreadable decodes to a fresh
/// state rather than being an error the empty screen has to report.
/// </summary>
public static class EmptyMailboxCodec
{
    private sealed record Stored(
        Dictionary<string, int> Rungs,
        Dictionary<string, DateTimeOffset> RetiredUntilUtc);

    public static string Encode(EmptyMailboxState state) =>
        JsonSerializer.Serialize(new Stored(
            new Dictionary<string, int>(state.Rungs),
            new Dictionary<string, DateTimeOffset>(state.RetiredUntilUtc)));

    public static EmptyMailboxState Decode(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return EmptyMailboxState.Fresh;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Stored>(stored);
            if (parsed is null)
            {
                return EmptyMailboxState.Fresh;
            }
            return new EmptyMailboxState(
                parsed.Rungs ?? [],
                parsed.RetiredUntilUtc ?? []);
        }
        catch (JsonException)
        {
            return EmptyMailboxState.Fresh;
        }
    }
}
