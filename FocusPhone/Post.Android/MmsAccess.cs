using Android.Content;

namespace Post;

/// <summary>
/// The one thing both halves of MMS transport (<see cref="MmsDownloader"/> and
/// <see cref="MmsSender"/>) need before handing a Uri to <c>SmsManager</c>: the platform's
/// own MMS transport process needs an explicit grant to read or write it, per
/// <c>SmsManager.sendMultimediaMessage</c>/<c>downloadMultimediaMessage</c>'s own
/// documentation — it is not implied by Post holding the SMS role, because that grant covers
/// the SYSTEM'S content providers (<c>content://mms</c> itself), not a Uri from Post's own
/// FileProvider (the outgoing PDU file) or a fresh row Post just inserted.
/// </summary>
internal static class MmsAccess
{
    // The AOSP MMS transport service's package name, stable across mainstream Android
    // versions and OEMs since it is part of the base telephony framework rather than a
    // carrier's own app. Not verified against a real device — see the phase notes.
    private const string MmsServicePackage = "com.android.mms.service";

    public static void GrantToPlatform(Context context, Android.Net.Uri uri, ActivityFlags flags) =>
        context.GrantUriPermission(MmsServicePackage, uri, flags);
}
