using Codex.Domain.Diagnostics;
using Android.Content;
using Android.Views;
using Android.Widget;
using AndroidX.Camera.Core;
using AndroidX.Camera.Video;
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
        // Measured to the ratio the sensor is reading out, never to the space left over.
        var window = new RatioFrame(Host)
        {
            Background = PaperUi.Outline(Host, PaperTheme.PaperShade, PaperTheme.Ink, 0),
        };
        window.SetRatio(shell.Camera.Ratio);
        window.AddView(previewView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        column.AddView(window, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        column.AddView(QuietControls());

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

    // §7 keeps one primary action per screen, so the settings that change what the shutter
    // does are a quiet row under the window rather than four more buttons in the bar.
    //
    // Ratio and flash are §8 <see cref="PaperUi.Segmented"/> switches rather than the old
    // tap-to-cycle button: a single label showing only the current state ("Flash off") gave
    // no hint that a tap would move to "on" and not "auto", and cycling meant overshooting
    // back around. A segmented control shows every state and the current one at once, and
    // tapping a state jumps straight to it.
    private View QuietControls()
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        var camera = shell.Camera;
        var gap = PaperTheme.Dp(Host, DesignTokens.Space.X2);

        var ratio = PaperUi.Segmented(Host, ["9:16", "3:4"],
            index => { camera.Rebind(ratio: index == 0 ? CaptureRatio.NineSixteen : CaptureRatio.ThreeFour); Invalidate(); },
            camera.Ratio == CaptureRatio.ThreeFour ? 1 : 0);
        row.AddView(ratio, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        row.AddView(PaperUi.QuietButton(Host,
            camera.Lens == Lens.Front ? "Front" : "Back",
            () => { camera.Rebind(lens: CaptureFormat.NextLens(camera.Lens)); Invalidate(); }),
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            { MarginStart = gap, MarginEnd = gap });

        // Absent rather than disabled on the front lens: this phone has no front-facing
        // flash, and a control offering states that do nothing is worse than one that is
        // not there.
        if (CaptureFormat.HasFlash(camera.Lens))
        {
            var flash = PaperUi.Segmented(Host, ["Off", "On", "Auto"],
                index => { camera.Rebind(flash: (Flash)index); Invalidate(); },
                (int)camera.Flash);
            row.AddView(flash, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        }

        return row;
    }

    public override View BuildBottomBar() => shell.Camera.IsRecording
        ? PaperUi.BottomBar(Host,
            PaperUi.DestructiveButton(Host, "Stop", StopVideo))
        : PaperUi.BottomBar(Host,
            PaperUi.PrimaryButton(Host, "Capture", TakePhoto),
            PaperUi.SecondaryButton(Host, "Video", StartVideo),
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

        // The reversed spine. A capture does NOT go to the camera roll here: it goes to
        // Plate's own private folder and waits in the Darkroom until the owner chooses it.
        // Writing it into MediaStore at the shutter would put it straight into Google Photos
        // and leave nothing to choose between, which is the whole point of the room.
        //
        // The Photos fix has not been abandoned, only moved — the DCIM/Camera insert now
        // happens at send time, in DarkroomArea.
        var shooting = TapWatch.Action(PlateKeyActions.Shutter, TimeSpan.FromSeconds(10));
        var file = shell.DarkroomFiles.NewFile("jpg");
        var options = new ImageCapture.OutputFileOptions.Builder(file).Build();
        capture.TakePicture(options, ContextCompat.GetMainExecutor(Host),
            new CaptureCallback(this, file, shooting));
    }

    private void StartVideo()
    {
        if (shell.Camera.Video is not { } video)
        {
            ShowStatus("The camera is not ready yet.");
            return;
        }

        // A video is a moment like any other, so it files in the same camera roll the photos
        // do — splitting it out would put half of what Plate captures outside the folder
        // Google Photos actually backs up.
        var entry = CaptureFiling.Video(DateTime.Now);
        var values = new ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, entry.DisplayName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, entry.MimeType);
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, entry.RelativePath);

        var options = new MediaStoreOutputOptions.Builder(
            Host.ContentResolver!, Android.Provider.MediaStore.Video.Media.ExternalContentUri!)
            .SetContentValues(values)
            .Build();

        var pending = ((Recorder)video.Output!).PrepareRecording(Host, options);
        if (Host.CheckSelfPermission(Android.Manifest.Permission.RecordAudio)
            == Android.Content.PM.Permission.Granted)
        {
            // Sound is the point of most of these. Without the permission the recording
            // still runs, silently, rather than refusing to start.
            pending = pending.WithAudioEnabled();
        }

        shell.Camera.Recording = pending.Start(
            ContextCompat.GetMainExecutor(Host), new RecordListener(this));
        ShowStatus("Recording…");
        Invalidate();
    }

    private void StopVideo()
    {
        shell.Camera.Recording?.Stop();
        shell.Camera.Recording = null;
        Invalidate();
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

    private sealed class RecordListener(CameraArea area)
        : Java.Lang.Object, AndroidX.Core.Util.IConsumer
    {
        public void Accept(Java.Lang.Object? value)
        {
            if (value is not VideoRecordEvent.Finalize finished)
            {
                return;
            }

            area.Host.RunOnUiThread(() =>
            {
                area.shell.Camera.Recording = null;
                area.ShowStatus(finished.HasError
                    ? $"That recording did not save: {finished.Cause?.Message}"
                    : $"Video saved to your camera roll · {TimeFormats.Clock12(DateTime.Now)}");
                area.Invalidate();
            });
        }
    }

    private sealed class CaptureCallback(CameraArea area, Java.IO.File file, KeyAction shooting)
        : Java.Lang.Object, ImageCapture.IOnImageSavedCallback
    {
        public void OnImageSaved(ImageCapture.OutputFileResults? results) =>
            area.Host.RunOnUiThread(() =>
            {
                area.shell.Kept(DarkroomItem.Photo(file.Name!, DateTime.Now));
                shooting.Succeeded();
                // §7 bans a success toast, so the shutter's only answer is this line. It
                // says where the picture went, and it did NOT go to the camera roll.
                area.ShowStatus($"In the Darkroom · {TimeFormats.Clock12(DateTime.Now)}");
            });

        // §7: a failure surfaces inline, in the same line the success uses. It used to be a
        // toast, which is the one place the owner cannot go back and read.
        public void OnError(ImageCaptureException? exception)
        {
            shooting.Failed($"capture error {exception?.ImageCaptureError}: {exception?.Message}");
            area.Host.RunOnUiThread(() => area.ShowStatus(
                $"That one did not save: {exception?.Message}"));
        }
    }
}
