using Android.Content;
using Pulp.Domain.ChildView;

namespace Pulp.Shell;

/// <summary>
/// Keeps the child's view on this phone, so a phone handed to a child is still the child's after
/// Android reclaims Pulp and starts it again. Per install and private, never in the household file:
/// who is watching this phone right now is not a household fact. <see cref="ChildViewSessionCodec"/>
/// decides the shape; anything unreadable is no session.
/// </summary>
public sealed class ChildViewStore
{
    private const string PrefsName = "pulp-child-view";
    private const string SessionKey = "session";

    private readonly ISharedPreferences prefs;

    public ChildViewStore(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)
            ?? throw new InvalidOperationException($"The {PrefsName} preferences could not be opened.");
    }

    public ChildViewSession? Load() => ChildViewSessionCodec.Decode(prefs.GetString(SessionKey, null));

    /// <summary>Written at once (Commit, not Apply): the view must survive the process dying right after the switch.</summary>
    public void Save(ChildViewSession? session)
    {
        var edit = prefs.Edit()!;
        if (session is null)
        {
            edit.Remove(SessionKey);
        }
        else
        {
            edit.PutString(SessionKey, ChildViewSessionCodec.Encode(session));
        }

        edit.Commit();
    }
}
