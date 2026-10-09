namespace Pulp.Domain.Library;

public enum ProgressUnit
{
    Episode,
    Minute,
}

/// <summary>The thin pencil mark's count (D150): "E5 of 8", "46 of 118 min", "2 of 14 episodes".</summary>
public sealed record ProgressMark(int At, int Of, ProgressUnit Unit);
