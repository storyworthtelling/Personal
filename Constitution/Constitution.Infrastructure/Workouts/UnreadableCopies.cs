namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// Where a workout file that cannot be read goes: renamed beside where it was, not deleted and not written over. <b>A second file set
/// aside never replaces the first.</b> The first keeps <c>.unreadable</c> and later ones are numbered (<c>.unreadable.2</c>, <c>.unreadable.3</c>),
/// because a store that carries on empty and is damaged again must not destroy the only copy of what the first damage left.
/// </summary>
internal static class UnreadableCopies
{
    // Far more than a phone will ever see; reaching it means something is failing in a loop, and that is worth a loud stop.
    private const int MostCopies = 1000;

    public static void SetAside(string path)
    {
        for (var copy = 1; copy <= MostCopies; copy++)
        {
            var target = copy == 1 ? path + ".unreadable" : $"{path}.unreadable.{copy}";
            try
            {
                File.Move(path, target, overwrite: false);
                return;
            }
            catch (IOException) when (File.Exists(target))
            {
                // That name is taken by an earlier copy, which is kept; the next number is tried.
            }
        }

        throw new IOException($"{MostCopies} unreadable copies of '{Path.GetFileName(path)}' are already kept; this one was not set aside.");
    }
}
