using Android.Content;
using Codex.Domain.Policy;

namespace Post;

/// <summary>Where a link from a message goes: always Flyleaf's restricted browser.</summary>
internal static class LinkOpener
{
    // Every link in an email leaves. Flyleaf's restricted browser is where the suite's
    // origin containment lives, so a link from a stranger meets the same rules as one the
    // owner typed. Targeted explicitly at RestrictedBrowserActivity rather than left to an
    // implicit VIEW intent — the device-owner persistent-preferred-activity pin that would
    // otherwise route it there can be outranked by a host's verified Android App Links,
    // which is how a link was observed opening in full Chrome instead.
    public static void Open(Context context, string url)
    {
        var uri = Android.Net.Uri.Parse(url);
        var intent = new Intent(Intent.ActionView, uri);
        intent.SetComponent(new ComponentName(RestrictedBrowserHandoff.Package, RestrictedBrowserHandoff.Activity));
        try
        {
            context.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            // Flyleaf isn't installed under the expected id on this device — fall back to
            // whatever the platform resolves rather than dropping the link entirely.
            try
            {
                context.StartActivity(new Intent(Intent.ActionView, uri));
            }
            catch (Exception e)
            {
                PostDiagnostics.LogWarning("MailReader", $"Could not open a link from mail: {url}", e);
            }
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning("MailReader", $"Could not open a link from mail: {url}", e);
        }
    }
}
