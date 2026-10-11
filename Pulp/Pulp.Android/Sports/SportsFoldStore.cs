using Android.Content;
using Pulp.Domain.Sports;

namespace Pulp.Sports;

/// <summary>
/// What the viewer opened or folded on the Sports tab (D241), remembered on this phone only, like
/// "Hide watched" (D292). The file name and key prefix are frozen in <see cref="SportsFoldPreference"/>.
/// </summary>
internal sealed class SportsFoldStore
{
    private readonly ISharedPreferences preferences;

    public SportsFoldStore(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        preferences = context.GetSharedPreferences(SportsFoldPreference.FileName, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Pulp could not open its Sports fold preferences.");
    }

    public FoldMemory Load()
    {
        var stored = new Dictionary<string, bool>();
        foreach (var key in preferences.All!.Keys.Where(key => key.StartsWith(SportsFoldPreference.KeyPrefix, StringComparison.Ordinal)))
        {
            stored[key] = preferences.GetBoolean(key, false);
        }

        return SportsFoldPreference.FromPreferences(stored);
    }

    public void Save(FoldMemory memory)
    {
        var editor = preferences.Edit()!;
        foreach (var (key, open) in SportsFoldPreference.ToPreferences(memory))
        {
            editor.PutBoolean(key, open);
        }

        editor.Apply();
    }
}
