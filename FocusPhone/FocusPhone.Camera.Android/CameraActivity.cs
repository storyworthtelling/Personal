using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using Google.MLKit.Vision.Documentscanner;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.Common;
using Codex.Domain.Design;
using Codex.Domain.Time;
using Codex.Ui;

namespace FocusPhone.Camera;

/// <summary>
/// Photo capture (CameraX preview + capture to MediaStore so Google Photos
/// backs everything up), ML Kit document scanning, and QR scanning. QR
/// results that are URLs fire a plain VIEW intent, which the device-owner
/// policy routes into the launcher's restricted browser.
/// </summary>
[Activity(
    Label = "Camera",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper.AppCompat")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class CameraActivity : AppCompatActivity
{
    private const int DocumentScanRequest = 1;

    private PreviewView? previewView;
    private ImageCapture? imageCapture;
    private TextView? statusLine;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        SetContentView(BuildLayout());

        if (CheckSelfPermission(Android.Manifest.Permission.Camera) == Android.Content.PM.Permission.Granted)
        {
            StartCamera();
        }
        else
        {
            RequestPermissions([Android.Manifest.Permission.Camera], 0);
        }

        CrashReporter.ShowPendingCrash(this);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
        Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (grantResults.FirstOrDefault() == Android.Content.PM.Permission.Granted)
        {
            StartCamera();
        }
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        previewView = new PreviewView(this);
        root.AddView(previewView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // Quiet inline status replaces success toasts (DESIGN.md §7).
        statusLine = PaperUi.Secondary(this, "");
        statusLine.Gravity = GravityFlags.CenterHorizontal;
        statusLine.Visibility = ViewStates.Gone;
        root.AddView(statusLine, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        root.AddView(PaperUi.BottomBar(this,
            PaperUi.PrimaryButton(this, "Capture", TakePhoto),
            PaperUi.SecondaryButton(this, "Scan doc", StartDocumentScan),
            PaperUi.SecondaryButton(this, "Scan QR", StartQrScan),
            PaperUi.SecondaryButton(this, "Photos", OpenGooglePhotos)));
        return root;
    }

    private void ShowStatus(string message)
    {
        statusLine!.Text = message;
        statusLine.Visibility = ViewStates.Visible;
    }

    private void StartCamera()
    {
        var providerFuture = ProcessCameraProvider.GetInstance(this);
        providerFuture.AddListener(new Java.Lang.Runnable(() =>
        {
            var provider = (ProcessCameraProvider)providerFuture.Get()!;
            var preview = new Preview.Builder().Build();
            preview.SurfaceProvider = previewView!.SurfaceProvider;
            imageCapture = new ImageCapture.Builder().Build();
            provider.UnbindAll();
            provider.BindToLifecycle(this, CameraSelector.DefaultBackCamera!, preview, imageCapture);
        }), ContextCompat.GetMainExecutor(this));
    }

    private void TakePhoto()
    {
        if (imageCapture is null)
        {
            return;
        }

        var values = new ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName,
            $"FocusPhone_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "image/jpeg");
        var options = new ImageCapture.OutputFileOptions.Builder(
            ContentResolver!, Android.Provider.MediaStore.Images.Media.ExternalContentUri!, values).Build();

        imageCapture.TakePicture(options, ContextCompat.GetMainExecutor(this),
            new CaptureCallback(this));
    }

    private void StartDocumentScan()
    {
        var options = new GmsDocumentScannerOptions.Builder()
            .SetGalleryImportAllowed(false)
            .SetResultFormats(GmsDocumentScannerOptions.ResultFormatJpeg, GmsDocumentScannerOptions.ResultFormatPdf)
            .SetScannerMode(GmsDocumentScannerOptions.ScannerModeFull)
            .Build();
        var scanner = GmsDocumentScanning.GetClient(options);
        scanner.GetStartScanIntent(this)
            .AddOnSuccessListener(new IntentSenderListener(this))
            .AddOnFailureListener(new FailureToastListener(this, "Document scanner unavailable"));
    }

    // These bindings expose the ML Kit barcode *processing* API (not the turnkey
    // GMS code-scanner UI), so QR scanning captures a still frame and decodes it.
    private void StartQrScan()
    {
        if (imageCapture is null)
        {
            return;
        }
        var file = new Java.IO.File(CacheDir, "qr-scan.jpg");
        var options = new ImageCapture.OutputFileOptions.Builder(file).Build();
        imageCapture.TakePicture(options, ContextCompat.GetMainExecutor(this),
            new QrCaptureCallback(this, file));
    }

    private void OpenGooglePhotos()
    {
        var intent = PackageManager!.GetLaunchIntentForPackage("com.google.android.apps.photos");
        if (intent is not null)
        {
            StartActivity(intent);
        }
        else
        {
            Toast.MakeText(this, "Google Photos is not installed.", ToastLength.Short)!.Show();
        }
    }

    private class CaptureCallback : Java.Lang.Object, ImageCapture.IOnImageSavedCallback
    {
        private readonly CameraActivity activity;

        public CaptureCallback(CameraActivity activity)
        {
            this.activity = activity;
        }

        public void OnImageSaved(ImageCapture.OutputFileResults? results) =>
            activity.RunOnUiThread(() => activity.ShowStatus($"Photo saved · {TimeFormats.Clock12(DateTime.Now)}"));

        public void OnError(ImageCaptureException? exception) =>
            Toast.MakeText(activity, $"Capture failed: {exception?.Message}", ToastLength.Long)!.Show();
    }

    // Captures a still frame to the cache, then decodes any QR code in it.
    private class QrCaptureCallback : Java.Lang.Object, ImageCapture.IOnImageSavedCallback
    {
        private readonly CameraActivity activity;
        private readonly Java.IO.File file;

        public QrCaptureCallback(CameraActivity activity, Java.IO.File file)
        {
            this.activity = activity;
            this.file = file;
        }

        public void OnImageSaved(ImageCapture.OutputFileResults? results)
        {
            var image = InputImage.FromFilePath(activity, Android.Net.Uri.FromFile(file)!);
            var options = new BarcodeScannerOptions.Builder()
                .SetBarcodeFormats(Barcode.FormatQrCode)
                .Build();
            BarcodeScanning.GetClient(options).Process(image)
                .AddOnSuccessListener(new QrResultListener(activity))
                .AddOnFailureListener(new FailureToastListener(activity, "QR scan failed"));
        }

        public void OnError(ImageCaptureException? exception) =>
            Toast.MakeText(activity, $"QR capture failed: {exception?.Message}", ToastLength.Long)!.Show();
    }

    private class QrResultListener : Java.Lang.Object, Android.Gms.Tasks.IOnSuccessListener
    {
        private readonly CameraActivity activity;

        public QrResultListener(CameraActivity activity)
        {
            this.activity = activity;
        }

        public void OnSuccess(Java.Lang.Object? result)
        {
            string? value = null;
            if (result is System.Collections.IEnumerable barcodes)
            {
                foreach (var item in barcodes)
                {
                    if (item is Barcode barcode && barcode.RawValue is not null)
                    {
                        value = barcode.RawValue;
                        break;
                    }
                }
            }
            if (value is null)
            {
                Toast.MakeText(activity, "No QR code found.", ToastLength.Short)!.Show();
                return;
            }
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https")
            {
                // The device-owner persistent preferred activity routes this
                // into the launcher's RestrictedBrowserActivity.
                activity.StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(value)));
            }
            else
            {
                new Android.App.AlertDialog.Builder(activity)
                    .SetTitle("Scanned")!
                    .SetMessage(value)!
                    .SetPositiveButton("OK", (_, _) => { })!
                    .Show();
            }
        }
    }

    private class IntentSenderListener : Java.Lang.Object, Android.Gms.Tasks.IOnSuccessListener
    {
        private readonly CameraActivity activity;

        public IntentSenderListener(CameraActivity activity)
        {
            this.activity = activity;
        }

        public void OnSuccess(Java.Lang.Object? result)
        {
            if (result is IntentSender sender)
            {
                activity.StartIntentSenderForResult(sender, DocumentScanRequest, null, 0, 0, 0);
            }
        }
    }

    private class FailureToastListener : Java.Lang.Object, Android.Gms.Tasks.IOnFailureListener
    {
        private readonly CameraActivity activity;
        private readonly string message;

        public FailureToastListener(CameraActivity activity, string message)
        {
            this.activity = activity;
            this.message = message;
        }

        public void OnFailure(Java.Lang.Exception e) =>
            Toast.MakeText(activity, $"{message}: {e.Message}", ToastLength.Long)!.Show();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != DocumentScanRequest || resultCode != Result.Ok)
        {
            return;
        }

        var result = GmsDocumentScanningResult.FromActivityResultIntent(data);
        var pdf = result?.GetPdf();
        if (pdf?.Uri is null)
        {
            return;
        }

        // Persist the scan into MediaStore Downloads so Drive/Photos can see it.
        var values = new ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName,
            $"Scan_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "application/pdf");
        var target = ContentResolver!.Insert(
            Android.Provider.MediaStore.Downloads.ExternalContentUri!, values);
        if (target is null)
        {
            return;
        }
        using var input = ContentResolver.OpenInputStream(pdf.Uri)!;
        using var output = ContentResolver.OpenOutputStream(target)!;
        input.CopyTo(output);
        ShowStatus($"Scan saved to Downloads · {TimeFormats.Clock12(DateTime.Now)}");
    }
}
