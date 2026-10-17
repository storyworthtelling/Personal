using Android.App;
using Android.Window;

namespace Constitution.Waiting;

/// <summary>
/// Back from the Waiting page turns it back to the page underneath. Registered only while the page
/// is open, through the back-invoked dispatcher, because with predictive back a plain
/// <c>OnBackPressed</c> override is no longer called.
/// </summary>
internal sealed class BackToPage(Activity activity, Action close) : Java.Lang.Object, IOnBackInvokedCallback
{
    private bool registered;

    public void Register()
    {
        if (registered)
        {
            return;
        }

        activity.OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(IOnBackInvokedDispatcher.PriorityDefault, this);
        registered = true;
    }

    public void Unregister()
    {
        if (!registered)
        {
            return;
        }

        activity.OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(this);
        registered = false;
    }

    public void OnBackInvoked() => close();
}
