using Android.Content;
using Pulp.Domain.Friday;
using Pulp.Domain.Settings;

namespace Pulp.Settings;

/// <summary>
/// The stack's size (D106), kept on this phone only: the stack is this phone's adult's (D23), so the
/// other phone's choice is never read here. The file and keys are frozen in
/// <see cref="StackSizePreference"/>; nothing stored, or anything out of bounds, reads as three and two.
/// </summary>
internal sealed class StackSizeStore
{
    private const int Absent = -1;

    private readonly ISharedPreferences preferences;

    public StackSizeStore(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        preferences = context.GetSharedPreferences(StackSizePreference.FileName, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Pulp could not open its stack-size preferences.");
    }

    public StackSizes Load() =>
        StackSizeChoice.Read(Stored(StackSizePreference.FilmsKey), Stored(StackSizePreference.ShowsKey));

    public void Save(StackSizes sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        preferences.Edit()!
            .PutInt(StackSizePreference.FilmsKey, sizes.Films)!
            .PutInt(StackSizePreference.ShowsKey, sizes.Shows)!
            .Apply();
    }

    private int? Stored(string key)
    {
        var value = preferences.GetInt(key, Absent);
        return value == Absent ? null : value;
    }
}
