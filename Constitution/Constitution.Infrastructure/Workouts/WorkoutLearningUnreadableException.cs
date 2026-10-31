namespace Constitution.Infrastructure.Workouts;

/// <summary>A learned record exists and cannot be read in full, so it was left as it is and nothing was written over it.</summary>
public sealed class WorkoutLearningUnreadableException(string record)
    : InvalidOperationException($"The {record} could not be read in full, so it was left untouched.");
