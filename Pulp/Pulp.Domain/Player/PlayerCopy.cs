namespace Pulp.Domain.Player;

/// <summary>What the one player screen is showing (D315).</summary>
public enum PlayerKind
{
    Trailer,
    Highlights,
}

/// <summary>The words of the player screen per kind; the player itself is the same posture either way.</summary>
public static class PlayerCopy
{
    public static string FallbackHeader(PlayerKind kind) => kind switch
    {
        PlayerKind.Trailer => "Trailer",
        PlayerKind.Highlights => "Highlights",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A player kind with no header decided."),
    };

    public static string Status(PlayerKind kind) => kind switch
    {
        PlayerKind.Trailer => "Tap play to start the trailer.",
        PlayerKind.Highlights => "Tap play to start the highlights.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A player kind with no status decided."),
    };

    /// <summary>The kind from the name an intent carried; anything unrecognised is a trailer, the original and plainest use.</summary>
    public static PlayerKind Parse(string? name) =>
        Enum.TryParse<PlayerKind>(name, ignoreCase: false, out var kind) && Enum.IsDefined(kind) ? kind : PlayerKind.Trailer;
}
