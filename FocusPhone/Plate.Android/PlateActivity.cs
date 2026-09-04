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
using Codex.Domain.Plate;
using Codex.Domain.Time;
using Codex.Ui;
using Storyworthtelling.Paper;

namespace Plate;

/// <summary>
/// Photo capture (CameraX preview + capture to MediaStore so Google Photos
/// backs everything up), ML Kit document scanning, and QR scanning. QR
/// results that are URLs fire a plain VIEW intent, which the device-owner
/// policy routes into the launcher's restricted browser.
/// </summary>
[Activity(
    Label = "Plate",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper.AppCompat")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class PlateActivity : AppCompatActivity
{
    private const int DocumentScanRequest = 1;

    // Google's scanner returns page rasters at roughly letter width; declaring them at 200
    // DPI puts a scanned page on paper at about the size it was scanned from, rather than
    // PdfSharp's default 96 DPI, which would print it half again too large.
    private const int ScanDpi = 200;

    private PreviewView? previewView;
    private ImageCapture? imageCapture;
    private TextView? statusLine;
    private FrameLayout? actionSlot;

    // Null until the owner picks a door; the pages gathered so far once they have.
    private ScanTray? scan;

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

        // The bar is swapped rather than the screen: picking a scan destination and gathering
        // pages are steps within the camera, not places to go. §7 bans the dialog that would
        // otherwise ask.
        actionSlot = new FrameLayout(this);
        root.AddView(actionSlot, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        ShowCaptureBar();
        return root;
    }

    private void SetActions(params View[] actions)
    {
        actionSlot!.RemoveAllViews();
        actionSlot.AddView(PaperUi.BottomBar(this, actions));
    }

    private void ShowCaptureBar()
    {
        scan = null;
        SetActions(
            PaperUi.PrimaryButton(this, "Capture", TakePhoto),
            PaperUi.SecondaryButton(this, "Scan doc", ShowScanDestinations),
            PaperUi.SecondaryButton(this, "Scan QR", StartQrScan),
            PaperUi.SecondaryButton(this, "Photos", OpenGooglePhotos));
    }

    // Where a scan is going is picked before the scanner opens, so the owner is never left
    // holding a finished document with nowhere obvious to put it.
    private void ShowScanDestinations()
    {
        ShowStatus("Where is this going?");
        SetActions(
            PaperUi.SecondaryButton(this, "Drive", () => BeginScan(ScanDestination.Drive)),
            PaperUi.SecondaryButton(this, "Post", () => BeginScan(ScanDestination.Post)),
            PaperUi.SecondaryButton(this, "reMarkable", () => BeginScan(ScanDestination.Remarkable)),
            PaperUi.SecondaryButton(this, "Back", () =>
            {
                HideStatus();
                ShowCaptureBar();
            }));
    }

    private void ShowScanBar()
    {
        var pages = scan!.Count;
        SetActions(
            PaperUi.PrimaryButton(this, pages > 0 ? "Save" : "Scan pages", SaveScan),
            PaperUi.SecondaryButton(this, "Add pages", StartDocumentScan),
            PaperUi.SecondaryButton(this, "Discard", () =>
            {
                HideStatus();
                ShowCaptureBar();
            }));
    }

    private void ShowStatus(string message)
    {
        statusLine!.Text = message;
        statusLine.Visibility = ViewStates.Visible;
    }

    private void HideStatus()
    {
        statusLine!.Visibility = ViewStates.Gone;
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
            ContentResolver!, Android.Provider.MediaStore.Images.Media.ExternalContentUri!, values).Build();

        imageCapture.TakePicture(options, ContextCompat.GetMainExecutor(this),
            new CaptureCallback(this));
    }

    private void BeginScan(ScanDestination destination)
    {
        scan = ScanTray.For(destination);
        StartDocumentScan();
    }

    private void StartDocumentScan()
    {
        // JPEG only. Plate no longer takes the scanner's finished PDF: that PDF is capped at
        // one run's worth of pages, and the owner wants to scan much more than that. The page
        // rasters are asked for one run at a time and gathered in the tray until the document
        // is whole; PdfVolumeComposer makes the PDF at the end.
        var options = new GmsDocumentScannerOptions.Builder()
            .SetGalleryImportAllowed(false)
            .SetPageLimit(ScanTray.PagesPerScannerRun)
            .SetResultFormats(GmsDocumentScannerOptions.ResultFormatJpeg)
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
        private readonly PlateActivity activity;

        public CaptureCallback(PlateActivity activity)
        {
            this.activity = activity;
        }

        public void OnImageSaved(ImageCapture.OutputFileResults? results) =>
            activity.RunOnUiThread(() => activity.ShowStatus($"Photo saved to your camera roll · {TimeFormats.Clock12(DateTime.Now)}"));

        public void OnError(ImageCaptureException? exception) =>
            Toast.MakeText(activity, $"Capture failed: {exception?.Message}", ToastLength.Long)!.Show();
    }

    // Captures a still frame to the cache, then decodes any QR code in it.
    private class QrCaptureCallback : Java.Lang.Object, ImageCapture.IOnImageSavedCallback
    {
        private readonly PlateActivity activity;
        private readonly Java.IO.File file;

        public QrCaptureCallback(PlateActivity activity, Java.IO.File file)
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
        private readonly PlateActivity activity;

        public QrResultListener(PlateActivity activity)
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

            // What the code MEANS is decided in Codex.Domain.Plate.QrScan, where it can be
            // tested without a camera. This used to end in an AlertDialog for anything that
            // was not a link, which §7 bans: results surface inline, never in a dialog.
            var reading = QrScan.Read(value);
            switch (reading.Action)
            {
                case QrAction.OpenInBrowser:
                    // A plain VIEW intent; the device-owner persistent preferred activity
                    // routes it into the launcher's RestrictedBrowserActivity. Only http
                    // and https ever reach here — see QrScan.
                    activity.StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(reading.Value)));
                    break;
                case QrAction.ShowText:
                    activity.ShowStatus(reading.Value);
                    break;
                default:
                    activity.ShowStatus("No code in that frame.");
                    break;
            }
        }
    }

    private class IntentSenderListener : Java.Lang.Object, Android.Gms.Tasks.IOnSuccessListener
    {
        private readonly PlateActivity activity;

        public IntentSenderListener(PlateActivity activity)
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
        private readonly PlateActivity activity;
        private readonly string message;

        public FailureToastListener(PlateActivity activity, string message)
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
        scan = (scan ?? ScanTray.For(ScanDestination.Drive)).Add(PageUris(result));
        ShowStatus(scan.Count == 1 ? "1 page held." : $"{scan.Count} pages held.");
        ShowScanBar();
    }

    // ML Kit hands back a nullable list of nullable pages; a cancelled run is the ordinary
    // way to see it. The tray takes null for "no pages" so this can stay a straight read.
    private static IEnumerable<string>? PageUris(GmsDocumentScanningResult? result)
    {
        var pages = result?.Pages;
        if (pages is null)
        {
            return null;
        }

        var uris = new List<string>();
        foreach (var page in pages)
        {
            if (page?.ImageUri is not null)
            {
                uris.Add(page.ImageUri.ToString()!);
            }
        }
        return uris;
    }

    private void SaveScan()
    {
        if (scan is not { HasPages: true })
        {
            StartDocumentScan();
            return;
        }

        // §8: loading is a status sentence, not a spinner. Composing a long document takes
        // long enough to notice, and the sentence is what says so.
        ShowStatus($"Composing {scan.Count} pages…");

        var pageImages = new List<byte[]>(scan.Count);
        foreach (var page in scan.Pages)
        {
            using var input = ContentResolver!.OpenInputStream(Android.Net.Uri.Parse(page)!)!;
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            pageImages.Add(buffer.ToArray());
        }

        var pdf = new PdfVolumeComposer().Compose(pageImages, ScanDpi);

        // The other half of the live bug: this used to write into MediaStore.Downloads,
        // which the owner reported as "scans vanish into Downloads". A scan is a document
        // and files with documents. Documents/ is not a legal RELATIVE_PATH for the
        // Downloads collection (that one must sit under Download/), so the insert goes to
        // the Files collection instead.
        var entry = CaptureFiling.Scan("Scan", DateTime.Now);
        var values = new ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, entry.DisplayName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, entry.MimeType);
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, entry.RelativePath);
        var target = ContentResolver!.Insert(
            Android.Provider.MediaStore.Files.GetContentUri("external")!, values);
        if (target is null)
        {
            ShowStatus("Could not file that scan.");
            return;
        }

        using (var output = ContentResolver.OpenOutputStream(target)!)
        {
            output.Write(pdf, 0, pdf.Length);
        }

        ShowStatus($"Scan saved to Documents · {TimeFormats.Clock12(DateTime.Now)}");
        ShowCaptureBar();
    }
}
