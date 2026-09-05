using Android.Content;
using Android.Views;
using Android.Widget;
using AndroidX.Camera.Core;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using Codex.Domain.Design;
using Codex.Domain.Plate;
using Codex.Domain.Time;
using Codex.Ui;

namespace Plate;

/// <summary>
/// The camera: a live preview and a shutter, and nothing else on the page.
///
/// <para>The preview is a <b>framed window on paper</b> (§12) rather than a full-bleed
/// viewfinder — a picture belongs inside a frame in this suite, and a preview bled to the
/// screen edge would be the one surface pretending the phone is not made of paper. The
/// window always carries the ratio the sensor is actually capturing: never letterboxed
/// inside itself, never stretched, never cropped to fill the well, because the alternative
/// is a preview that lies about the frame being composed.</para>
/// </summary>
public sealed class CameraArea(PlateActivity host) : PlateArea(host)
{
    private readonly PlateActivity shell = host;

    public override BinderArea Tab =>
        new("Camera", Resource.Drawable.area_camera, PaperTheme.PlateMark, PaperTheme.PlateFill);

    private PreviewView? previewView;
    private TextView? statusLine;
    private bool showing;

    public override View BuildView()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        previewView = new PreviewView(Host);
        var window = new FrameLayout(Host)
        {
            Background = PaperUi.Outline(Host, PaperTheme.PaperShade, PaperTheme.Ink, 0),
        };
        window.AddView(previewView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        column.AddView(window, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // Quiet inline status. §7 bans success toasts, so the shutter's only answer is this
        // line appearing under the window.
        statusLine = PaperUi.Secondary(Host, "");
        statusLine.Visibility = ViewStates.Gone;
        var gap = PaperTheme.Dp(Host, DesignTokens.Space.X3);
        statusLine.SetPadding(0, gap, 0, gap);
        column.AddView(statusLine);

        if (showing)
        {
            shell.Camera.BindPreview(previewView);
        }
        return column;
    }

    public override View BuildBottomBar() =>
        PaperUi.BottomBar(Host,
            PaperUi.PrimaryButton(Host, "Capture", TakePhoto),
            PaperUi.SecondaryButton(Host, "Photos", OpenGooglePhotos));

    public override void OnShown(bool nowShowing)
    {
        showing = nowShowing;
        if (nowShowing && previewView is not null)
        {
            shell.Camera.BindPreview(previewView);
        }
        else if (!nowShowing)
        {
            shell.Camera.Release();
        }
    }

    public override void Pause() => shell.Camera.Release();

    public override void OnPermissionsResult()
    {
        if (showing && previewView is not null)
        {
            shell.Camera.BindPreview(previewView);
        }
    }

    private void TakePhoto()
    {
        if (shell.Camera.Capture is not { } capture)
        {
            ShowStatus("The camera is not ready yet.");
            return;
        }

        // WHERE this lands is the phase's live bug, and it is decided in Codex.Domain
        // (CaptureFiling) rather than here: without RELATIVE_PATH the Images collection
        // files under its default Pictures/, and Google Photos backs up DCIM/Camera while
        // treating every other device folder as opt-in — so every photo this app ever took
        // was on the phone and outside backup.
        var entry = CaptureFiling.Photo(DateTime.Now);
        var values = new ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, entry.DisplayName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, entry.MimeType);
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, entry.RelativePath);
        var options = new ImageCapture.OutputFileOptions.Builder(
            Host.ContentResolver!, Android.Provider.MediaStore.Images.Media.ExternalContentUri!,
            values).Build();

        capture.TakePicture(options, ContextCompat.GetMainExecutor(Host), new CaptureCallback(this));
    }

    private void OpenGooglePhotos()
    {
        var intent = Host.PackageManager!.GetLaunchIntentForPackage("com.google.android.apps.photos");
        if (intent is null)
        {
            ShowStatus("Google Photos is not installed.");
            return;
        }
        Host.StartActivity(intent);
    }

    private void ShowStatus(string message)
    {
        if (statusLine is null)
        {
            return;
        }
        statusLine.Text = message;
        statusLine.Visibility = ViewStates.Visible;
    }

    private sealed class CaptureCallback(CameraArea area)
        : Java.Lang.Object, ImageCapture.IOnImageSavedCallback
    {
        public void OnImageSaved(ImageCapture.OutputFileResults? results) =>
            area.Host.RunOnUiThread(() => area.ShowStatus(
                $"Photo saved to your camera roll · {TimeFormats.Clock12(DateTime.Now)}"));

        // §7: a failure surfaces inline, in the same line the success uses. It used to be a
        // toast, which is the one place the owner cannot go back and read.
        public void OnError(ImageCaptureException? exception) =>
            area.Host.RunOnUiThread(() => area.ShowStatus(
                $"That one did not save: {exception?.Message}"));
    }
}
