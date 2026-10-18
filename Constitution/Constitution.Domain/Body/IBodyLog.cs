namespace Constitution.Domain.Body;

/// <summary>
/// Where Body's reading and syncing say what happened. A phrase is an outcome ("body read weight: ok,
/// 2 readings"), never a health value, a reading, a package name or a person's name, so the Android
/// head can hand it straight to its diagnostics trail. Counts and exception type names are the most
/// a phrase may carry.
/// </summary>
public interface IBodyLog
{
    /// <summary>Records one short outcome phrase.</summary>
    void Outcome(string phrase);
}
