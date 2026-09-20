using Microsoft.Extensions.DependencyInjection;
using Folio.Domain.Identity;
using Folio.Infrastructure.Services;

namespace Folio.Android.Pages;

/// <summary>
/// Opens a draft's rendered report in whatever reads a PDF on this phone.
///
/// <para><b>Through <c>OpenFileRequest</c>, never a path.</b> Android 7+ throws
/// <c>FileUriExposedException</c> when a <c>file://</c> URI crosses an app boundary, so handing a
/// viewer the path is a crash rather than a failure to open — and a crash in the one code path this
/// repository's tooling cannot run, since the head has no compiler here but CI. Essentials wraps the
/// file in a content URI from the <c>${applicationId}.fileProvider</c> it already declares in the
/// merged manifest, and grants the read permission with it. Measured rather than assumed: that
/// provider is in this head's own merged <c>AndroidManifest.xml</c>, under an authority built from
/// <see cref="FolioIdentity.ApplicationId"/> — which is why the finding's estimate that this needed
/// a provider, a paths resource and a permission flag was overstated. It searched the head's source,
/// where the framework's contribution is not written down.</para>
///
/// <para>One place rather than two, because the desk and the book page both open a report and the
/// thing that is easy to get wrong here is the same for both of them.</para>
/// </summary>
public static class ReportFile
{
    /// <summary>
    /// Opens the report for this draft. False means there was nothing on the device to open — the
    /// report has been delivered and cleared, or has not been rendered yet — which is the caller's
    /// to say on its own page rather than this one's to announce.
    /// </summary>
    public static async Task<bool> OpenAsync(Guid draftId, string title)
    {
        string? path;
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            path = await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .GetAnalysisPdfPathAsync(draftId, CancellationToken.None);
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return false;
        }

        await Launcher.OpenAsync(new OpenFileRequest
        {
            Title = $"{title} — revision report",
            File = new ReadOnlyFile(path),
        });
        return true;
    }
}
