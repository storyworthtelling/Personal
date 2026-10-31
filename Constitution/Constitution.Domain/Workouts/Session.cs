using Constitution.Domain.Equipment;

namespace Constitution.Domain.Workouts;

/// <summary>One exercise by its plain name, and the one piece of equipment it needs, if any (an <see cref="EquipmentCatalogue"/> id).</summary>
public sealed record Exercise
{
    public Exercise(string name, string? equipmentId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An exercise needs a name.", nameof(name));
        }

        if (equipmentId is not null && !EquipmentCatalogue.All.Any(item => item.Id == equipmentId))
        {
            throw new ArgumentException($"The checklist has no equipment with the id '{equipmentId}'.", nameof(equipmentId));
        }

        Name = name.Trim();
        EquipmentId = equipmentId;
    }

    public string Name { get; }

    public string? EquipmentId { get; }
}

/// <summary>
/// What a workout is, apart from when it happens: a kind, a length, whether it makes the person sweat
/// (so the calendar leaves room to shower), and the exercises by plain name. The equipment it needs is
/// <i>derived</i> from the exercises, never stated separately, so the two cannot disagree.
/// </summary>
public sealed record Session
{
    public Session(SessionKind kind, TimeSpan duration, bool isSweaty, IReadOnlyList<Exercise> exercises)
    {
        ArgumentNullException.ThrowIfNull(exercises);
        if (exercises.Count == 0)
        {
            throw new ArgumentException("A session needs at least one exercise.", nameof(exercises));
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "A session takes some time.");
        }

        Kind = kind;
        Duration = duration;
        IsSweaty = isSweaty;
        Exercises = exercises.ToList().AsReadOnly();
        var needed = Exercises.Select(exercise => exercise.EquipmentId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (kind == SessionKind.Treadmill)
        {
            needed.Add(EquipmentCatalogue.TreadmillId);
        }

        EquipmentNeeded = EquipmentCatalogue.All.Select(item => item.Id).Where(needed.Contains).ToList().AsReadOnly();
    }

    public SessionKind Kind { get; }

    public TimeSpan Duration { get; }

    public bool IsSweaty { get; }

    public IReadOnlyList<Exercise> Exercises { get; }

    /// <summary>Every equipment id the session uses, once each, in the checklist's order.</summary>
    public IReadOnlyList<string> EquipmentNeeded { get; }
}
