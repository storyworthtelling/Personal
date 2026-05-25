namespace NytRemarkable.Android;

public static class CredentialStore
{
    public static string NytSessionCookie
    {
        get => Preferences.Get(nameof(NytSessionCookie), string.Empty);
        set => Preferences.Set(nameof(NytSessionCookie), value);
    }

    public static string NytApiKey
    {
        get => Preferences.Get(nameof(NytApiKey), string.Empty);
        set => Preferences.Set(nameof(NytApiKey), value);
    }

    public static string RemarkableDeviceToken
    {
        get => Preferences.Get(nameof(RemarkableDeviceToken), string.Empty);
        set => Preferences.Set(nameof(RemarkableDeviceToken), value);
    }

    public static string LastDeliveryResult
    {
        get => Preferences.Get(nameof(LastDeliveryResult), "Never delivered");
        set => Preferences.Set(nameof(LastDeliveryResult), value);
    }

    public static bool IsConfigured =>
        !string.IsNullOrEmpty(NytSessionCookie) &&
        !string.IsNullOrEmpty(NytApiKey) &&
        !string.IsNullOrEmpty(RemarkableDeviceToken);
}
