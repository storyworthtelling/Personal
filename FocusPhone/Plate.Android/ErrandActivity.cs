using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Camera.Core;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using Codex.Domain.Design;
using Codex.Domain.Plate;
using Codex.Domain.Post;
using Codex.Ui;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;

namespace Plate;

/// <summary>
/// An errand: another app asked, Plate looks, Plate returns, Plate gets out of the way.
///
/// <para><b>An errand has no navigation.</b> There is no binder gutter on this screen, in
/// any of its faces. Somebody is sitting in <c>onActivityResult</c> while the owner is here,
/// so there is nowhere else to go and the frame says so rather than a rule politely asking
/// them not to wander. Backing out returns a cancel immediately rather than leaving the
/// caller hanging.</para>
///
/// <para>Plate declares the full camera component set so nothing on this phone opens a
/// different camera — and Android silently omits an app missing one of them, with no
/// warning, which is why which action means what lives in <see cref="Errands"/> where a
/// test can read it.</para>
/// </summary>
[Activity(
    Label = "Plate",
    Exported = true,
    NoHistory = true,
    Theme = "@style/Theme.FocusPhone.Paper.AppCompat")]
[IntentFilter([Errands.ImageCapture, Errands.ImageCaptureSecure],
    Categories = [Intent.CategoryDefault])]
[IntentFilter([Errands.VideoCapture], Categories = [Intent.CategoryDefault])]
// DEFAULT only, deliberately: adding CategoryLauncher here would put a second Plate icon on
// the home screen for the same app.
[IntentFilter([Errands.StillImageCamera, Errands.StillImageCameraSecure],
    Categories = [Intent.CategoryDefault])]
[IntentFilter([Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataMimeType = "image/*")]
[IntentFilter([ReadRequest.ReadCard, ReadRequest.ReadStatement],
    Categories = [Intent.CategoryDefault])]
public class ErrandActivity : AppCompatActivity
{
    private Errand errand;
    private PlateCamera camera = null!;
    private PreviewView? previewView;
    private TextView? statusLine;
    private LinearLayout? results;
    private FrameLayout? barSlot;
    private CardReading card = new(string.Empty, string.Empty, string.Empty);
    private MoneyFacts statement = new(null, null, null, null);
    private bool read;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        errand = Errands.For(Intent?.Action, Intent?.Type);
        if (errand == Errand.None)
        {
            // Not an errand at all — hand the owner the app they actually asked for rather
            // than an empty screen with a Cancel button.
            StartActivity(new Intent(this, typeof(PlateActivity)));
            Finish();
            return;
        }

        if (errand == Errand.OpenCamera)
        {
            // The power-button double-press. Nobody is waiting on a result, so this is just
            // Plate opening as itself.
            StartActivity(new Intent(this, typeof(PlateActivity)));
            Finish();
            return;
        }

        camera = new PlateCamera(this);
        SetContentView(BuildLayout());

        if (CheckSelfPermission(Android.Manifest.Permission.Camera)
            != Android.Content.PM.Permission.Granted)
        {
            RequestPermissions([Android.Manifest.Permission.Camera], 0);
        }
        else if (previewView is not null)
        {
            camera.BindPreview(previewView);
        }
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        var gap = PaperTheme.Dp(this, DesignTokens.Space.X3);

        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };
        page.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        page.AddView(PaperUi.ScreenHeader(this, Heading(), PaperTheme.PlateMark));
        page.AddView(PaperUi.Secondary(this, Asked()));

        if (errand == Errand.ShowImage)
        {
            page.AddView(BuildViewer(), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        }
        else
        {
            previewView = new PreviewView(this);
            var window = new RatioFrame(this)
            {
                Background = PaperUi.Outline(this, PaperTheme.PaperShade, PaperTheme.Ink, 0),
            };
            window.AddView(previewView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            page.AddView(window);

            results = new LinearLayout(this) { Orientation = Orientation.Vertical };
            page.AddView(results, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        }

        statusLine = PaperUi.Secondary(this, string.Empty);
        statusLine.Visibility = ViewStates.Gone;
        statusLine.SetPadding(0, gap, 0, gap);
        page.AddView(statusLine);

        root.AddView(page, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        root.AddView(BuildBar());
        return root;
    }

    private View BuildViewer()
    {
        var image = new ImageView(this);
        image.SetScaleType(ImageView.ScaleType.FitCenter);
        if (Intent?.Data is { } source)
        {
            image.SetImageURI(source);
        }
        return image;
    }

    private string Heading() => errand switch
    {
        Errand.Photo => "A picture",
        Errand.Video => "A video",
        Errand.ShowImage => "A picture",
        Errand.ReadCard => "Read a card",
        _ => "Read a statement",
    };

    // Which app asked is worth saying: the owner is standing in an app they did not open,
    // and the reason they are here is the only thing that makes the screen make sense.
    private string Asked() => errand switch
    {
        Errand.Photo => "Something asked for a picture.",
        Errand.Video => "Something asked for a video.",
        Errand.ShowImage => "Handed over to look at.",
        Errand.ReadCard => "Point at the card, then send back what it says.",
        _ => "Point at the statement, then send back the balance.",
    };

    private View BuildBar()
    {
        barSlot = new FrameLayout(this);
        barSlot.AddView(BarFor());
        return barSlot;
    }

    private View BarFor() => errand switch
    {
        Errand.ShowImage => PaperUi.BottomBar(this, PaperUi.PrimaryButton(this, "Done", Cancel)),
        Errand.Photo => PaperUi.BottomBar(this,
            PaperUi.PrimaryButton(this, "Take it", TakePhoto),
            PaperUi.QuietButton(this, "Cancel", Cancel)),
        Errand.Video => PaperUi.BottomBar(this,
            PaperUi.PrimaryButton(this, "Record", StartVideo),
            PaperUi.QuietButton(this, "Cancel", Cancel)),
        _ => PaperUi.BottomBar(this,
            read
                ? PaperUi.PrimaryButton(this, "Send it back", SendReading)
                : PaperUi.PrimaryButton(this, "Read it", ReadFrame),
            read ? PaperUi.SecondaryButton(this, "Read again", ReadFrame) : Blank(),
            PaperUi.QuietButton(this, "Cancel", Cancel)),
    };

    private View Blank() => new Space(this);

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
        Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (previewView is not null)
        {
            camera.BindPreview(previewView);
        }
    }

    protected override void OnPause()
    {
        base.OnPause();
        camera?.Release();
    }

    /// <summary>
    /// Backing out returns a cancel straight away rather than leaving the caller hanging in
    /// <c>onActivityResult</c> with nothing.
    /// </summary>
    private void Cancel()
    {
        SetResult(Result.Canceled);
        Finish();
    }

    public override void OnBackPressed() => Cancel();

    private void TakePhoto()
    {
        if (camera.Capture is not { } capture)
        {
            ShowStatus("The camera is not ready yet.");
            return;
        }

        // Always to a cache file first. The caller may have named a destination of its own
        // (EXTRA_OUTPUT), which is an arbitrary content URI CameraX cannot write into
        // directly — so the bytes are captured once and then put wherever they belong.
        var file = new Java.IO.File(CacheDir, "errand-capture.jpg");
        var options = new ImageCapture.OutputFileOptions.Builder(file).Build();
        capture.TakePicture(options, ContextCompat.GetMainExecutor(this),
            new ErrandCaptureCallback(this, file));
    }

    private void Delivered(Java.IO.File file)
    {
        try
        {
            // Honour the caller's destination when it gave one: that is the contract, and a
            // caller that supplied a URI is reading it back rather than looking at our data.
            if (Intent?.GetParcelableExtra(MediaStore.ExtraOutput) as Android.Net.Uri is { } asked)
            {
                Copy(file, asked);
                SetResult(Result.Ok);
                Finish();
                return;
            }

            // No destination given: file it in the camera roll like any other capture and
            // hand back the row we made. It is a picture the owner took either way, so it
            // belongs in the one folder Google Photos actually backs up.
            var entry = CaptureFiling.Photo(DateTime.Now);
            var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.DisplayName, entry.DisplayName);
            values.Put(MediaStore.IMediaColumns.MimeType, entry.MimeType);
            values.Put(MediaStore.IMediaColumns.RelativePath, entry.RelativePath);
            var target = ContentResolver!.Insert(MediaStore.Images.Media.ExternalContentUri!, values);
            if (target is null)
            {
                ShowStatus("Could not make a place to save that.");
                return;
            }

            Copy(file, target);
            var result = new Intent();
            result.SetData(target);
            result.AddFlags(ActivityFlags.GrantReadUriPermission);
            SetResult(Result.Ok, result);
            Finish();
        }
        catch (Exception e)
        {
            // The caller is blocked, so it hears either a picture or a plain cancel — never
            // a half-written file it would then try to read.
            ShowStatus($"That one did not save: {e.Message}");
        }
    }

    private void Copy(Java.IO.File from, Android.Net.Uri to)
    {
        using var input = System.IO.File.OpenRead(from.AbsolutePath!);
        using var output = ContentResolver!.OpenOutputStream(to)!;
        input.CopyTo(output);
    }

    private void StartVideo() =>
        // Deliberately not built. A requested video needs the same shoot-and-return path as
        // a photo through the Darkroom, which is the next release; declaring the component
        // without honouring it would be worse than saying so.
        ShowStatus("Plate cannot record for another app yet.");

    private void ReadFrame()
    {
        if (camera.Capture is not { } capture)
        {
            ShowStatus("The camera is not ready yet.");
            return;
        }

        ShowStatus("Reading…");
        var file = new Java.IO.File(CacheDir, "errand-read.jpg");
        var options = new ImageCapture.OutputFileOptions.Builder(file).Build();
        capture.TakePicture(options, ContextCompat.GetMainExecutor(this),
            new ReadCallback(this, file));
    }

    private void Recognised(Text? text)
    {
        var lines = new List<string>();
        foreach (var block in text?.TextBlocks ?? [])
        {
            foreach (var line in block?.Lines ?? [])
            {
                if (!string.IsNullOrWhiteSpace(line?.Text))
                {
                    lines.Add(line.Text!);
                }
            }
        }

        if (lines.Count == 0)
        {
            ShowStatus("No words in that frame.");
            return;
        }

        if (errand == Errand.ReadCard)
        {
            card = BusinessCard.Read(lines);
        }
        else
        {
            statement = MoneyMail.Read(null, string.Join('\n', lines));
        }

        read = true;
        ShowResults();
    }

    // The owner corrects it before it goes back. This is why the reader errs toward leaving
    // a field empty: a wrong value that looks right is one nobody re-reads.
    private void ShowResults()
    {
        if (results is null)
        {
            return;
        }

        results.RemoveAllViews();
        if (errand == Errand.ReadCard)
        {
            results.AddView(Field("Name", card.Name, value => card = card with { Name = value }));
            results.AddView(Field("Phone", card.Phone, value => card = card with { Phone = value }));
            results.AddView(Field("Email", card.Email, value => card = card with { Email = value }));
        }
        else
        {
            var payload = ReadRequest.StatementExtras(statement);
            results.AddView(PaperUi.Body(this, payload is null
                ? "No balance in that frame."
                : $"Balance {payload[IndexHandoff.BalanceExtra]}"));
            if (payload?.TryGetValue(IndexHandoff.AsOfExtra, out var asOf) == true)
            {
                results.AddView(PaperUi.Secondary(this, $"as of {asOf}"));
            }
        }

        HideStatus();
        Rebuild();
    }

    private View Field(string label, string value, Action<string> onChanged)
    {
        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.AddView(PaperUi.Label(this, label));
        var field = PaperUi.Field(this, string.Empty);
        field.Text = value;
        field.TextChanged += (_, _) => onChanged(field.Text ?? string.Empty);
        column.AddView(field);
        return column;
    }

    private void SendReading()
    {
        var result = new Intent();
        if (errand == Errand.ReadCard)
        {
            foreach (var (key, value) in ReadRequest.CardExtras(card))
            {
                result.PutExtra(key, value);
            }
        }
        else
        {
            // The existing Index contract, unchanged. Plate is a second sender of the
            // payload Post already sends, not a second dialect of it — Index must not have
            // to know which app spoke.
            var payload = ReadRequest.StatementExtras(statement);
            if (payload is null)
            {
                ShowStatus("There is no balance to send back.");
                return;
            }
            foreach (var (key, value) in payload)
            {
                result.PutExtra(key, value);
            }
        }

        SetResult(Result.Ok, result);
        Finish();
    }

    // Only the bar changes state, so only the bar is rebuilt — replacing the page would
    // throw away what the owner has typed into the fields.
    private void Rebuild()
    {
        if (barSlot is null)
        {
            return;
        }
        barSlot.RemoveAllViews();
        barSlot.AddView(BarFor());
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

    private void HideStatus()
    {
        if (statusLine is not null)
        {
            statusLine.Visibility = ViewStates.Gone;
        }
    }

    private sealed class ErrandCaptureCallback(ErrandActivity host, Java.IO.File file)
        : Java.Lang.Object, ImageCapture.IOnImageSavedCallback
    {
        public void OnImageSaved(ImageCapture.OutputFileResults? results) =>
            host.RunOnUiThread(() => host.Delivered(file));

        public void OnError(ImageCaptureException? exception) =>
            host.RunOnUiThread(() => host.ShowStatus(
                $"That one did not save: {exception?.Message}"));
    }

    private sealed class ReadCallback(ErrandActivity host, Java.IO.File file)
        : Java.Lang.Object, ImageCapture.IOnImageSavedCallback
    {
        public void OnImageSaved(ImageCapture.OutputFileResults? results)
        {
            var image = InputImage.FromFilePath(host, Android.Net.Uri.FromFile(file)!);
            TextRecognition.GetClient(
                    Xamarin.Google.MLKit.Vision.Text.Latin.TextRecognizerOptions.DefaultOptions!)
                .Process(image)
                .AddOnSuccessListener(new ReadListener(host))
                .AddOnFailureListener(new ReadFailure(host));
        }

        public void OnError(ImageCaptureException? exception) =>
            host.RunOnUiThread(() => host.ShowStatus(
                $"Could not take that frame: {exception?.Message}"));
    }

    private sealed class ReadListener(ErrandActivity host)
        : Java.Lang.Object, Android.Gms.Tasks.IOnSuccessListener
    {
        public void OnSuccess(Java.Lang.Object? result) =>
            host.Recognised(result as Text);
    }

    private sealed class ReadFailure(ErrandActivity host)
        : Java.Lang.Object, Android.Gms.Tasks.IOnFailureListener
    {
        public void OnFailure(Java.Lang.Exception e) =>
            host.RunOnUiThread(() => host.ShowStatus($"Could not read that: {e.Message}"));
    }
}
