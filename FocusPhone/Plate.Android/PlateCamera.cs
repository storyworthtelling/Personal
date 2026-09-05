using Android.Content;
using AndroidX.AppCompat.App;
using AndroidX.Camera.Core;
using AndroidX.Camera.Core.ResolutionSelector;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.Video;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using Codex.Domain.Plate;

namespace Plate;

/// <summary>
/// The one thing in Plate that holds the camera open, and the one place that knows which
/// lens, ratio and flash are in force.
///
/// <para>Two areas want the sensor — the Camera area for its preview and shutter, the Scan
/// area for the single frame a code read needs — and it cannot be bound twice. So neither
/// area owns it: the shell does, and an area asks for a preview when it becomes the one
/// showing and lets go when it stops. Holding it open behind another tab drains the battery
/// and locks out every other app that wants a camera, which on this phone includes the
/// document scanner one tab across.</para>
///
/// <para><b>Ratio is a capture setting, not a display one.</b> It is set on the use cases
/// through a ResolutionSelector so the sensor really is reading out at that shape — the
/// window is then measured to match (<see cref="RatioFrame"/>) rather than the picture being
/// cropped to fit a window someone chose for its looks.</para>
/// </summary>
public sealed class PlateCamera(AppCompatActivity host)
{
    private ProcessCameraProvider? provider;
    private PreviewView? boundTo;

    /// <summary>The capture use case, or null when nothing is bound.</summary>
    public ImageCapture? Capture { get; private set; }

    /// <summary>The video use case, or null when nothing is bound.</summary>
    public VideoCapture? Video { get; private set; }

    public CaptureRatio Ratio { get; private set; } = CaptureFormat.Default;

    public Lens Lens { get; private set; } = Lens.Back;

    public Flash Flash { get; private set; } = Flash.Off;

    /// <summary>Whether a recording is running. Owned here because the use case is.</summary>
    public Recording? Recording { get; set; }

    public bool IsRecording => Recording is not null;

    /// <summary>
    /// Binds a preview into <paramref name="into"/> alongside the shutter. Safe to call
    /// again — the previous binding is dropped first, which is what lets a second area take
    /// the camera over, and what lets a lens or ratio change take effect.
    /// </summary>
    public void BindPreview(PreviewView into, Action? onReady = null)
    {
        if (host.CheckSelfPermission(Android.Manifest.Permission.Camera)
            != Android.Content.PM.Permission.Granted)
        {
            return;
        }

        boundTo = into;
        var providerFuture = ProcessCameraProvider.GetInstance(host);
        providerFuture.AddListener(new Java.Lang.Runnable(() =>
        {
            provider = providerFuture.Get() as ProcessCameraProvider;
            if (provider is null || boundTo is null)
            {
                return;
            }

            var selector = new ResolutionSelector.Builder()
                .SetAspectRatioStrategy(Ratio == CaptureRatio.ThreeFour
                    ? AspectRatioStrategy.Ratio43FallbackAutoStrategy!
                    : AspectRatioStrategy.Ratio169FallbackAutoStrategy!)
                .Build();

            var preview = new Preview.Builder().SetResolutionSelector(selector).Build()!;
            preview.SurfaceProvider = boundTo.SurfaceProvider;

            var capture = new ImageCapture.Builder().SetResolutionSelector(selector).Build()!;
            capture.FlashMode = FlashModeFor(Flash);

            // The recorder picks its own resolution from the quality selector; forcing the
            // photo ratio onto it is how a video ends up letterboxed in its own file.
            var video = VideoCapture.WithOutput(new Recorder.Builder().Build())!;

            provider.UnbindAll();
            provider.BindToLifecycle(host, SelectorFor(Lens), preview, capture, video);
            Capture = capture;
            Video = video;
            onReady?.Invoke();
        }), ContextCompat.GetMainExecutor(host));
    }

    /// <summary>Re-binds with a new lens, ratio or flash. A no-op when nothing is bound.</summary>
    public void Rebind(CaptureRatio? ratio = null, Lens? lens = null, Flash? flash = null)
    {
        Ratio = ratio ?? Ratio;
        Lens = lens ?? Lens;
        Flash = flash ?? Flash;

        // A lens with no flash cannot be left holding one, or turning the phone around would
        // silently arm a flash that fires when you turn it back.
        if (!CaptureFormat.HasFlash(Lens))
        {
            Flash = Flash.Off;
        }

        if (boundTo is not null)
        {
            BindPreview(boundTo);
        }
    }

    /// <summary>Lets the sensor go. Idempotent — an area that was never showing may call it.</summary>
    public void Release()
    {
        Recording?.Stop();
        Recording = null;
        provider?.UnbindAll();
        Capture = null;
        Video = null;
        boundTo = null;
    }

    private static CameraSelector SelectorFor(Lens lens) => lens == Lens.Front
        ? CameraSelector.DefaultFrontCamera!
        : CameraSelector.DefaultBackCamera!;

    private static int FlashModeFor(Flash flash) => flash switch
    {
        Flash.On => ImageCapture.FlashModeOn,
        Flash.Auto => ImageCapture.FlashModeAuto,
        _ => ImageCapture.FlashModeOff,
    };
}
