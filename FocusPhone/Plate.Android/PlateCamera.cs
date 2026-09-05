using AndroidX.AppCompat.App;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;

namespace Plate;

/// <summary>
/// The one thing in Plate that holds the camera open.
///
/// <para>Two areas want it — the Camera area for its preview and shutter, the Scan area for
/// the single frame a QR read needs — and a sensor cannot be bound twice. So neither area
/// owns it: the shell does, and an area asks for a preview when it becomes the one showing
/// and lets go when it stops. Holding the sensor open behind another tab drains the battery
/// and locks out every other app that wants a camera, which on this phone includes the
/// document scanner one tab across.</para>
/// </summary>
public sealed class PlateCamera(AppCompatActivity host)
{
    private ProcessCameraProvider? provider;

    /// <summary>
    /// The capture use case, or null when nothing is bound. Callers check it rather than
    /// assume: binding is asynchronous, and the shutter can be tapped before it lands.
    /// </summary>
    public ImageCapture? Capture { get; private set; }

    /// <summary>
    /// Binds a preview into <paramref name="into"/> alongside the shutter. Safe to call
    /// again — the previous binding is dropped first, which is what lets a second area take
    /// the camera over without the two fighting for it.
    /// </summary>
    public void BindPreview(PreviewView into, Action? onReady = null)
    {
        if (host.CheckSelfPermission(Android.Manifest.Permission.Camera)
            != Android.Content.PM.Permission.Granted)
        {
            return;
        }

        var providerFuture = ProcessCameraProvider.GetInstance(host);
        providerFuture.AddListener(new Java.Lang.Runnable(() =>
        {
            provider = providerFuture.Get() as ProcessCameraProvider;
            if (provider is null)
            {
                return;
            }

            var preview = new Preview.Builder().Build();
            preview.SurfaceProvider = into.SurfaceProvider;
            Capture = new ImageCapture.Builder().Build();
            provider.UnbindAll();
            provider.BindToLifecycle(host, CameraSelector.DefaultBackCamera!, preview, Capture);
            onReady?.Invoke();
        }), ContextCompat.GetMainExecutor(host));
    }

    /// <summary>Lets the sensor go. Idempotent — an area that was never showing may call it.</summary>
    public void Release()
    {
        provider?.UnbindAll();
        Capture = null;
    }
}
