using global::Android.App;
using global::Android.Content;

namespace Folio.Android;

// Thin wrapper over the Android Storage Access Framework so backup/restore can let the user pick a
// destination/source (Downloads, an SD card, their own Drive via the system picker) without any new
// permission or Google OAuth scope. StartActivityForResult results are bridged back to an awaitable
// via MainActivity.OnActivityResult -> HandleResult.
internal static class SafFilePicker
{
    private const int CreateRequestCode = 42101;
    private const int OpenRequestCode = 42102;

    private static TaskCompletionSource<global::Android.Net.Uri?>? pending;

    // Prompts for a location to create a new document and returns the chosen Uri (null if cancelled).
    public static Task<global::Android.Net.Uri?> CreateDocumentAsync(Activity activity, string mimeType, string suggestedName)
    {
        var tcs = new TaskCompletionSource<global::Android.Net.Uri?>();
        pending = tcs;

        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(mimeType);
        intent.PutExtra(Intent.ExtraTitle, suggestedName);
        activity.StartActivityForResult(intent, CreateRequestCode);
        return tcs.Task;
    }

    // Prompts the user to pick an existing document and returns the chosen Uri (null if cancelled).
    public static Task<global::Android.Net.Uri?> OpenDocumentAsync(Activity activity, string mimeType)
    {
        var tcs = new TaskCompletionSource<global::Android.Net.Uri?>();
        pending = tcs;

        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(mimeType);
        activity.StartActivityForResult(intent, OpenRequestCode);
        return tcs.Task;
    }

    // Returns true when the result belonged to a SAF pick (so MainActivity stops dispatching it).
    public static bool HandleResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != CreateRequestCode && requestCode != OpenRequestCode)
            return false;

        var tcs = pending;
        pending = null;
        tcs?.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
        return true;
    }
}
