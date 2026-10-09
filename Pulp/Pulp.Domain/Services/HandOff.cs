using Pulp.Domain.Core;

namespace Pulp.Domain.Services;

/// <summary>
/// A title's link on one service, as the availability source gave it: <c>link</c> always,
/// <c>videoLink</c> where the source has one (finding §2.4).
/// </summary>
public sealed record HandOffLink(TitleId Title, ServiceId Service, Uri Link, Uri? VideoLink);

/// <summary>
/// The intent a hand-off sends, as plain data for the head to turn into an Android <c>Intent</c>:
/// <c>ACTION_VIEW</c>, the deep link, and <c>setPackage</c>. It has no public constructor and no
/// setters, so the only way to hold one is <see cref="HandOff.For"/>, and that always fills the
/// package (G8). Without it, Flyleaf's restricted browser — the locked phone's default web handler —
/// would catch the https link and show the Blocked page.
/// </summary>
public sealed class HandOffIntent
{
    /// <summary>Android's <c>Intent.ACTION_VIEW</c>.</summary>
    public const string ViewAction = "android.intent.action.VIEW";

    internal HandOffIntent(Uri data, string package)
    {
        Data = data;
        Package = package;
    }

    public string Action => ViewAction;

    public Uri Data { get; }

    /// <summary>The service app's package; never empty.</summary>
    public string Package { get; }
}

/// <summary>Builds the explicit-package intent for handing a title to its service (finding §2.4).</summary>
public static class HandOff
{
    /// <summary>
    /// The intent that opens <paramref name="title"/> in <paramref name="service"/>'s app: the video
    /// link when there is one, else the title link, aimed at the service's primary package. A link
    /// for another service, or one that is not https, is a caller's mistake and throws.
    /// </summary>
    public static HandOffIntent For(HandOffLink title, StreamingService service)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(service);
        if (!title.Service.Equals(service.Id))
        {
            throw new ArgumentException(
                $"A link for '{title.Service}' cannot be handed to '{service.Id}'; it would open the wrong app or nothing.",
                nameof(title));
        }

        RequireHttps(title.Link, nameof(HandOffLink.Link));
        if (title.VideoLink is not null)
        {
            RequireHttps(title.VideoLink, nameof(HandOffLink.VideoLink));
        }

        return new HandOffIntent(title.VideoLink ?? title.Link, service.PrimaryPackage);
    }

    private static void RequireHttps(Uri link, string which)
    {
        if (!link.IsAbsoluteUri || link.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                $"The {which} '{link}' is not an https link; a hand-off only sends the service's own https links.",
                which);
        }
    }
}
