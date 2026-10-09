using Pulp.Domain.Core;

namespace Pulp.Domain.Sports;

/// <summary>A league or competition, by the id the sports feed gives it. Compared without case.</summary>
public readonly record struct LeagueId
{
    public LeagueId(string value) => Value = CoreText.Require(value, nameof(LeagueId));

    public string Value { get; }

    public bool Equals(LeagueId other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);

    public override string ToString() => Value;
}

/// <summary>A team, by the id the sports feed gives it. One team can play in several competitions (D185).</summary>
public readonly record struct TeamId
{
    public TeamId(string value) => Value = CoreText.Require(value, nameof(TeamId));

    public string Value { get; }

    public bool Equals(TeamId other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);

    public override string ToString() => Value;
}

/// <summary>One game or event, by the feed's event id.</summary>
public readonly record struct GameId
{
    public GameId(string value) => Value = CoreText.Require(value, nameof(GameId));

    public string Value { get; }

    public bool Equals(GameId other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);

    public override string ToString() => Value;
}
