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
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;
using Xamarin.Google.MLKit.Vision.DocumentScanner;

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
    Theme = "@style/Theme.AppCompat.NoActionBar")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class CameraActivity : AppCompatActivity
{
    private const int DocumentScanRequest = 1;
    private static readonly Color Paper = Color.ParseColor("#FAF8F2");

    private PreviewView? previewView;
    private ImageCapture? imageCapture;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(BuildLayout());

        if (CheckSelfPermission(Android.Manifest.Permission.Camera) == Android.Content.PM.Permission.Granted)
        {
            StartCamera();
        }
        else
        {
            RequestPermissions([Android.Manifest.Permission.Camera], 0);
        }
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
        root.SetBackgroundColor(Paper);

        previewView = new PreviewView(this);
        root.AddView(previewView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        actions.SetPadding(16, 16, 16, 16);
        AddAction(actions, "Capture", TakePhoto);
        AddAction(actions, "Scan doc", StartDocumentScan);
        AddAction(actions, "Scan QR", StartQrScan);
        AddAction(actions, "Photos", OpenGooglePhotos);
        root.AddView(actions);
        return root;
    }

    private void AddAction(LinearLayout row, string label, Action onClick)
    {
        var button = new Button(this) { Text = label };
        button.Click += (_, _) => onClick();
        row.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent)
        {
            Weight = 1,
            MarginEnd = 8,
        });
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

    private void StartQrScan()
    {
        var options = new GmsBarcodeScannerOptions.Builder()
            .SetBarcodeFormats(Barcode.FormatQrCode)
            .Build();
        GmsBarcodeScanning.GetClient(this, options).StartScan()
            .AddOnSuccessListener(new QrResultListener(this))
            .AddOnFailureListener(new FailureToastListener(this, "QR scanner unavailable"));
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

    private class CaptureCallback : ImageCapture.OnImageSavedCallback
    {
        private readonly CameraActivity activity;

        public CaptureCallback(CameraActivity activity)
        {
            this.activity = activity;
        }

        public override void OnImageSaved(ImageCapture.OutputFileResults results) =>
            Toast.MakeText(activity, "Saved.", ToastLength.Short)!.Show();

        public override void OnError(ImageCaptureException exception) =>
            Toast.MakeText(activity, $"Capture failed: {exception.Message}", ToastLength.Long)!.Show();
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
            var value = (result as Barcode)?.RawValue;
            if (value is null)
            {
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
                new AlertDialog.Builder(activity)
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
        var pdf = result?.Pdf;
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
        Toast.MakeText(this, "Scan saved to Downloads.", ToastLength.Short)!.Show();
    }
}
