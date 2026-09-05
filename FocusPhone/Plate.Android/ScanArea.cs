using Android.App;
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
using Google.MLKit.Vision.Documentscanner;
using Storyworthtelling.Paper;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.Common;

namespace Plate;

/// <summary>
/// Scanning: three destinations chosen before the scanner opens, and a fourth door for a
/// code, which belongs to no other app and acts rather than files.
///
/// <para><b>Plate assembles the document; Google only scans a page.</b> ML Kit's scanner
/// caps a run at <see cref="ScanTray.PagesPerScannerRun"/> pages and the owner wants longer,
/// so the finished PDF it offers is refused and the per-page rasters are taken instead,
/// gathered across as many runs as it takes and composed here with the shared
/// <see cref="PdfVolumeComposer"/>.</para>
/// </summary>
public sealed class ScanArea(PlateActivity host) : PlateArea(host)
{
    public const int DocumentScanRequest = 1;

    // Google's scanner returns page rasters at roughly letter width; declaring them at 200
    // DPI puts a scanned page on paper at about the size it was scanned from, rather than
    // PdfSharp's default 96 DPI, which would print it half again too large.
    private const int ScanDpi = 200;

    private readonly PlateActivity shell = host;

    public override BinderArea Tab =>
        new("Scan", Resource.Drawable.area_scan, PaperTheme.ReferenceMark, PaperTheme.ReferenceFill);

    /// <summary>Which of the area's three faces is showing.</summary>
    private enum Face
    {
        /// <summary>The doors: pick where this is going before the scanner opens.</summary>
        Doors,

        /// <summary>The page tray: what has been gathered, in the order it will bind.</summary>
        Tray,

        /// <summary>A live frame, waiting for a code to appear in it.</summary>
        Code,
    }

    private Face face = Face.Doors;
    private ScanTray? tray;
    private TextView? statusLine;
    private PreviewView? codePreview;
    private QrReading reading;

    public override View BuildView()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var gap = PaperTheme.Dp(Host, DesignTokens.Space.X3);

        switch (face)
        {
            case Face.Tray:
                BuildTray(column);
                break;
            case Face.Code:
                BuildCode(column, gap);
                break;
            default:
                BuildDoors(column);
                break;
        }

        statusLine = PaperUi.Secondary(Host, "");
        statusLine.Visibility = ViewStates.Gone;
        statusLine.SetPadding(0, gap, 0, gap);
        column.AddView(statusLine);
        return column;
    }

    private void BuildDoors(LinearLayout column)
    {
        column.AddView(Door("Scan to Drive", "Filed beside your other paper", ScanDestination.Drive));
        column.AddView(Door("Scan to Post", "Attached to a new message", ScanDestination.Post));
        column.AddView(Door("Scan to the reMarkable", "Straight to the tablet",
            ScanDestination.Remarkable));

        // Set apart from the three, because a code belongs to no other app and it acts
        // rather than files.
        var rule = PaperUi.Secondary(Host, "— — —");
        rule.Gravity = GravityFlags.CenterHorizontal;
        column.AddView(rule);
        column.AddView(Door("Read a QR code", "Opens it, or shows you what it says", null));
    }

    private void BuildTray(LinearLayout column)
    {
        var held = tray!;
        column.AddView(PaperUi.Secondary(Host,
            $"{DestinationSentence(held.Destination)} · {PagesSoFar(held.Count)}"));

        for (var i = 0; i < held.Pages.Count; i++)
        {
            column.AddView(PageRow(i, held.Pages[i]));
        }

        column.AddView(PaperUi.Secondary(Host,
            "Long-press a page to move or remove it."));
    }

    private View PageRow(int index, ScanPage page)
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        row.SetMinimumHeight(PaperTheme.Dp(Host, DesignTokens.Shape.ListRowMinHeightDp));
        row.AddView(PaperUi.Body(Host, $"Page {index + 1}"));
        row.AddView(PaperUi.Secondary(Host, $"scanned {TimeFormats.Clock12(page.ScannedAt)}"));
        row.LongClickable = true;
        row.LongClick += (_, _) => ShowPageActions(index);
        return row;
    }

    // §7: a long-press opens a Paper action card, never a system dialog. Move rather than
    // drag — the same result with a target big enough to hit, and no gesture to discover.
    private void ShowPageActions(int index)
    {
        var held = tray!;
        var actions = new List<CardAction>();
        if (index > 0)
        {
            actions.Add(new CardAction("Move earlier",
                () => { tray = held.Move(index, index - 1); Invalidate(); }));
        }
        if (index < held.Pages.Count - 1)
        {
            actions.Add(new CardAction("Move later",
                () => { tray = held.Move(index, index + 1); Invalidate(); }));
        }
        actions.Add(new CardAction("Remove this page",
            () => { tray = held.RemoveAt(index); Invalidate(); }, Destructive: true));

        ActionCard.Show(Host, Overlay, $"Page {index + 1}",
            $"scanned {TimeFormats.Clock12(held.Pages[index].ScannedAt)}", [.. actions]);
    }

    private void BuildCode(LinearLayout column, int gap)
    {
        codePreview = new PreviewView(Host);
        var window = new FrameLayout(Host)
        {
            Background = PaperUi.Outline(Host, PaperTheme.PaperShade, PaperTheme.Ink, 0),
        };
        window.AddView(codePreview, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        column.AddView(window, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        if (reading.Action != QrAction.Nothing)
        {
            var label = PaperUi.Label(Host, "The code says");
            label.SetPadding(0, gap, 0, 0);
            column.AddView(label);
            // Cascadia, because the value is a literal: a serial number or a URL read back
            // in a proportional face is a value the owner cannot check character by character.
            column.AddView(PaperUi.Mono(Host, reading.Value));
            column.AddView(PaperUi.Secondary(Host,
                "A web address opens in the phone's browser. Anything else stays here."));
        }

        shell.Camera.BindPreview(codePreview);
    }

    public override View? BuildBottomBar() => face switch
    {
        Face.Tray => PaperUi.BottomBar(Host,
            tray!.HasPages
                ? PaperUi.PrimaryButton(Host, PagesLabel(tray.Count), SaveScan)
                : PaperUi.PrimaryButton(Host, "Scan pages", StartDocumentScan),
            PaperUi.SecondaryButton(Host, "Add a page", StartDocumentScan),
            PaperUi.QuietButton(Host, "Discard", Discard)),
        Face.Code => PaperUi.BottomBar(Host,
            reading.Action == QrAction.OpenInBrowser
                ? PaperUi.PrimaryButton(Host, "Open it", OpenReading)
                : PaperUi.PrimaryButton(Host, "Read the code", TakeCodeFrame),
            PaperUi.SecondaryButton(Host, "Scan another", TakeCodeFrame),
            PaperUi.QuietButton(Host, "Done", Discard)),
        _ => null,
    };

    /// <summary>Back leaves the tray rather than the app, which is a level to come up from.</summary>
    public override bool GoBack()
    {
        if (face == Face.Doors)
        {
            return false;
        }
        Discard();
        return true;
    }

    private View Door(string title, string detail, ScanDestination? destination)
    {
        var card = PaperUi.HairlineCard(Host);
        card.AddView(PaperUi.Body(Host, title));
        card.AddView(PaperUi.Secondary(Host, detail));
        card.Clickable = true;
        card.Click += (_, _) =>
        {
            if (destination is { } door)
            {
                tray = ScanTray.For(door);
                face = Face.Tray;
                Invalidate();
                StartDocumentScan();
            }
            else
            {
                reading = default;
                face = Face.Code;
                Invalidate();
            }
        };
        var gap = PaperTheme.Dp(Host, DesignTokens.Space.X2);
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, gap, 0, gap);
        card.LayoutParameters = layout;
        return card;
    }

    private static string DestinationSentence(ScanDestination destination) => destination switch
    {
        ScanDestination.Drive => "This one is going to Drive.",
        ScanDestination.Post => "This one is going to Post.",
        _ => "This one is going to the reMarkable.",
    };

    private static string PagesLabel(int pages) => pages == 1 ? "Send 1 page" : $"Send {pages} pages";

    private static string PagesSoFar(int pages) =>
        pages == 1 ? "1 page so far" : $"{pages} pages so far";

    private void Discard()
    {
        tray = null;
        reading = default;
        face = Face.Doors;
        shell.Camera.Release();
        HideStatus();
        Invalidate();
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
        scanner.GetStartScanIntent(Host)
            .AddOnSuccessListener(new IntentSenderListener(this))
            .AddOnFailureListener(new FailureListener(this, "The scanner is not available"));
    }

    public override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != DocumentScanRequest || resultCode != Result.Ok)
        {
            return;
        }

        var result = GmsDocumentScanningResult.FromActivityResultIntent(data);
        tray = (tray ?? ScanTray.For(ScanDestination.Drive)).Add(PageUris(result));
        ShowStatus(tray.Count == 1 ? "1 page held." : $"{tray.Count} pages held.");
        Invalidate();
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
        if (tray is not { HasPages: true } held)
        {
            StartDocumentScan();
            return;
        }

        // §8: loading is a status sentence, not a spinner. Composing a long document takes
        // long enough to notice, and the sentence is what says so.
        ShowStatus($"Composing {held.Count} pages…");

        var pageImages = new List<byte[]>(held.Count);
        foreach (var page in held.Pages)
        {
            using var input = Host.ContentResolver!.OpenInputStream(Android.Net.Uri.Parse(page)!)!;
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
        var target = Host.ContentResolver!.Insert(
            Android.Provider.MediaStore.Files.GetContentUri("external")!, values);
        if (target is null)
        {
            ShowStatus("Could not file that scan.");
            return;
        }

        using (var output = Host.ContentResolver.OpenOutputStream(target)!)
        {
            output.Write(pdf, 0, pdf.Length);
        }

        Hand(held.Destination, target, entry.DisplayName, pdf);
    }

    // The delivery is hybrid, and which half applies is decided in Codex.Domain
    // (ScanDelivery) rather than here.
    private void Hand(ScanDestination destination, Android.Net.Uri document, string title, byte[] pdf)
    {
        if (ScanDelivery.RouteFor(destination) != DeliveryRoute.ShareIntent)
        {
            SendToRemarkable(title, pdf);
            return;
        }

        var share = new Intent(Intent.ActionSend);
        share.SetType("application/pdf");
        share.PutExtra(Intent.ExtraStream, document);
        share.PutExtra(Intent.ExtraSubject, title);
        share.AddFlags(ActivityFlags.GrantReadUriPermission);
        // Aimed at one app rather than a chooser: a chooser on a locked phone offers
        // whatever survived the lockdown instead of the door the owner picked.
        share.SetPackage(ScanDelivery.TargetPackageFor(destination));

        try
        {
            Host.StartActivity(share);
            ShowStatus($"Handed over · {TimeFormats.Clock12(DateTime.Now)}");
            tray = null;
            face = Face.Doors;
            Invalidate();
        }
        catch (ActivityNotFoundException)
        {
            ShowStatus($"Saved to Documents · {TimeFormats.Clock12(DateTime.Now)}. "
                + "That app is not installed.");
        }
    }

    // The one destination that is not another app on the phone. It is also the only one
    // that can say whether it arrived: a share intent is fire-and-forget, so Drive and Post
    // get "handed over" while this gets a real answer either way.
    private async void SendToRemarkable(string title, byte[] pdf)
    {
        if (!PlateRemarkable.IsPaired)
        {
            ShowStatus($"Saved to Documents · {TimeFormats.Clock12(DateTime.Now)}. "
                + "Pair the reMarkable in Settings to send it there.");
            return;
        }

        // §8: loading is a status sentence, not a spinner.
        ShowStatus("Sending to the reMarkable…");
        try
        {
            await shell.Remarkable.DeliverToRoot(title, pdf);
            ShowStatus($"On the reMarkable · {TimeFormats.Clock12(DateTime.Now)}");
            tray = null;
            face = Face.Doors;
            Invalidate();
        }
        catch (Exception e)
        {
            // The PDF is already filed under Documents, so nothing is lost — which is what
            // makes it safe to say plainly what went wrong instead of retrying blind.
            ShowStatus($"Saved to Documents, but the reMarkable refused it: {e.Message}");
        }
    }

    // These bindings expose the ML Kit barcode *processing* API (not the turnkey GMS
    // code-scanner UI), so QR scanning captures a still frame and decodes it.
    private void TakeCodeFrame()
    {
        if (shell.Camera.Capture is not { } capture)
        {
            ShowStatus("The camera is not ready yet.");
            return;
        }

        var file = new Java.IO.File(Host.CacheDir, "qr-scan.jpg");
        var options = new ImageCapture.OutputFileOptions.Builder(file).Build();
        capture.TakePicture(options, ContextCompat.GetMainExecutor(Host),
            new QrCaptureCallback(this, file));
    }

    // The owner opens it, rather than the code opening itself. A QR sticker anyone can
    // print should not be able to navigate the phone the instant it enters the frame.
    private void OpenReading()
    {
        if (reading.Action != QrAction.OpenInBrowser)
        {
            return;
        }

        // A plain VIEW intent; the device-owner persistent preferred activity routes it into
        // the launcher's RestrictedBrowserActivity. Only http and https ever reach here —
        // see QrScan.
        Host.StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(reading.Value)));
    }

    public override void OnShown(bool showing)
    {
        // Only the code face holds the camera, and only while it is the one on screen.
        if (!showing)
        {
            shell.Camera.Release();
        }
        else if (face == Face.Code && codePreview is not null)
        {
            shell.Camera.BindPreview(codePreview);
        }
    }

    public override void Pause() => shell.Camera.Release();

    private void ShowStatus(string message)
    {
        if (statusLine is null)
        {
            return;
        }
        statusLine.Text = message;
        statusLine.Visibility = ViewStates.Visible;
    }

    private void HideStatus()
    {
        if (statusLine is not null)
        {
            statusLine.Visibility = ViewStates.Gone;
        }
    }

    private sealed class IntentSenderListener(ScanArea area)
        : Java.Lang.Object, Android.Gms.Tasks.IOnSuccessListener
    {
        public void OnSuccess(Java.Lang.Object? result)
        {
            if (result is Android.Content.IntentSender sender)
            {
                area.Host.StartIntentSenderForResult(sender, DocumentScanRequest, null, 0, 0, 0);
            }
        }
    }

    // Captures a still frame to the cache, then decodes any QR code in it.
    private sealed class QrCaptureCallback(ScanArea area, Java.IO.File file)
        : Java.Lang.Object, ImageCapture.IOnImageSavedCallback
    {
        public void OnImageSaved(ImageCapture.OutputFileResults? results)
        {
            var image = InputImage.FromFilePath(area.Host, Android.Net.Uri.FromFile(file)!);
            var options = new BarcodeScannerOptions.Builder()
                .SetBarcodeFormats(Barcode.FormatQrCode)
                .Build();
            BarcodeScanning.GetClient(options).Process(image)
                .AddOnSuccessListener(new QrResultListener(area))
                .AddOnFailureListener(new FailureListener(area, "Could not read that frame"));
        }

        public void OnError(ImageCaptureException? exception) =>
            area.Host.RunOnUiThread(() => area.ShowStatus(
                $"Could not take that frame: {exception?.Message}"));
    }

    private sealed class QrResultListener(ScanArea area)
        : Java.Lang.Object, Android.Gms.Tasks.IOnSuccessListener
    {
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
            // was not a link, which §7 bans: the value goes on the page instead, with the
            // action it deserves in the pinned bar.
            area.reading = QrScan.Read(value);
            if (area.reading.Action == QrAction.Nothing)
            {
                area.ShowStatus("No code in that frame.");
                return;
            }
            area.Invalidate();
        }
    }

    // §7 again: a failure is a sentence on the page, not a toast the owner cannot re-read.
    private sealed class FailureListener(ScanArea area, string message)
        : Java.Lang.Object, Android.Gms.Tasks.IOnFailureListener
    {
        public void OnFailure(Java.Lang.Exception e) =>
            area.Host.RunOnUiThread(() => area.ShowStatus($"{message}: {e.Message}"));
    }
}
