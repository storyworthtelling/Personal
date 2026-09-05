using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Codex.Domain.Design;
using Codex.Domain.Plate;
using Codex.Ui;

namespace Plate;

/// <summary>
/// One picture, open.
///
/// <para>Crop, tint and filter — the part of the phase with no analogue anywhere in the
/// suite, so the least borrowed and the most invented. The three are a segmented control
/// under the window with a quiet row beneath for whichever is showing, which keeps §7's one
/// primary action per screen: the primary is Send.</para>
///
/// <para>Nothing is written until the owner sends. Backing out leaves the picture exactly as
/// it was captured, because an editor that saves as it goes has quietly decided for them.</para>
/// </summary>
[Activity(Label = "Plate", Theme = "@style/Theme.FocusPhone.Paper.AppCompat")]
public class EditorActivity : AppCompatActivity
{
    public const string ItemExtra = "item";
    public const string CaptionExtra = "caption";

    /// <summary>The owner sent it; the Darkroom should file it and let it go.</summary>
    public const int SentResult = 21;

    /// <summary>The owner threw it away; the Darkroom should offer the undo.</summary>
    public const int DeletedResult = 22;

    private enum Tool
    {
        Crop,
        Tint,
        Filter,
    }

    private DarkroomStore files = null!;
    private string itemId = string.Empty;
    private Bitmap? original;
    private PhotoEdit edit = PhotoEdit.None;
    private Tool tool = Tool.Crop;

    private ImageView? window;
    private LinearLayout? toolRow;
    private FrameLayout? overlay;
    private FaceRecogniser recogniser = null!;
    private FaceBookStore faces = null!;
    private Rolodex rolodex = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        files = new DarkroomStore(this);
        itemId = Intent?.GetStringExtra(ItemExtra) ?? string.Empty;
        original = BitmapFactory.DecodeFile(
            new Java.IO.File(files.Directory(), itemId).AbsolutePath);
        if (original is null)
        {
            // The file is gone — deleted from under us, or never written. Going back is the
            // only honest answer; an editor with no picture in it is a dead end.
            Finish();
            return;
        }

        // Recognition is local and optional: ML Kit finds the faces, and naming one needs an
        // embedding model this phone may not have. Without it the faces are still found and
        // the owner can still write the name themselves.
        recogniser = new FaceRecogniser(this, new NoFaceEmbedder());
        faces = new FaceBookStore(this);
        rolodex = new Rolodex(this);

        SetContentView(BuildLayout());
        Repaint();
        _ = LookForFaces();
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        var gap = PaperTheme.Dp(this, DesignTokens.Space.X3);

        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };
        page.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);

        window = new ImageView(this);
        window.SetScaleType(ImageView.ScaleType.FitCenter);
        window.SetBackgroundColor(PaperTheme.PaperShade);
        page.AddView(window, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        var tools = PaperUi.Segmented(this, ["Crop", "Tint", "Filter"], index =>
        {
            tool = (Tool)index;
            BuildToolRow();
        }, 0, PaperTheme.ProgressMark);
        var toolsLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        toolsLayout.SetMargins(0, gap, 0, 0);
        page.AddView(tools, toolsLayout);

        toolRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        page.AddView(toolRow);
        BuildToolRow();

        var caption = Intent?.GetStringExtra(CaptionExtra);
        if (!string.IsNullOrWhiteSpace(caption))
        {
            page.AddView(PaperUi.Voice(this, caption));
        }

        // The frame a dog-ear folds into, over the page rather than beside it.
        overlay = new FrameLayout(this);
        overlay.AddView(page, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        root.AddView(overlay, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        root.AddView(PaperUi.BottomBar(this,
            PaperUi.PrimaryButton(this, "Send", Send),
            PaperUi.DestructiveButton(this, "Delete", Delete)));
        return root;
    }

    /// <summary>
    /// Offers a name for a face, once, as a question. It never files anything by itself: a
    /// wrong suggestion costs a tap, and a suggestion accepted silently costs a picture
    /// filed under the wrong person.
    /// </summary>
    private async Task LookForFaces()
    {
        if (original is null)
        {
            return;
        }

        var found = await recogniser.FindAsync(original);
        var face = found.FirstOrDefault(entry => entry.CanBeMatched);
        if (face is null)
        {
            foreach (var entry in found)
            {
                entry.Crop.Recycle();
            }
            return;
        }

        var book = faces.Load();
        var match = book.Nearest(face.Descriptor);
        if (!match.IsSomeone || overlay is null)
        {
            return;
        }

        // §13: something waiting, not something to interrupt with. The fold carries it.
        DogEar.SetNotice(this, overlay, "face", new DogEar.Notice(
            $"Is this {match.Name}?",
            "Saying yes teaches it. Saying nothing changes nothing.",
            PaperTheme.ProgressMark,
            "Yes, that is them",
            () =>
            {
                faces.Save(book.Learn(match.ContactId, match.Name, face.Descriptor));
                DogEar.SetNotice(this, overlay, "face", null);
            }));
    }

    private void BuildToolRow()
    {
        if (toolRow is null || original is null)
        {
            return;
        }

        toolRow.RemoveAllViews();
        switch (tool)
        {
            case Tool.Crop:
                foreach (var shape in Enum.GetValues<CropShape>())
                {
                    var chosen = shape;
                    toolRow.AddView(PaperUi.QuietButton(this, Label(chosen), () =>
                    {
                        edit = edit.CropTo(
                            PhotoEdit.Shaped(chosen, original.Width, original.Height));
                        Repaint();
                    }));
                }
                toolRow.AddView(PaperUi.QuietButton(this, "Turn", () =>
                {
                    edit = edit.Turn();
                    Repaint();
                }));
                break;

            case Tool.Tint:
                toolRow.AddView(PaperUi.QuietButton(this, "Cooler", () =>
                {
                    edit = edit.TintBy(-1);
                    Repaint();
                }));
                toolRow.AddView(PaperUi.Secondary(this, Warmth()));
                toolRow.AddView(PaperUi.QuietButton(this, "Warmer", () =>
                {
                    edit = edit.TintBy(1);
                    Repaint();
                }));
                break;

            default:
                foreach (var filter in Enum.GetValues<PhotoFilter>())
                {
                    var chosen = filter;
                    toolRow.AddView(PaperUi.QuietButton(this, PhotoEdit.NameOf(chosen), () =>
                    {
                        edit = edit.With(chosen);
                        Repaint();
                    }));
                }
                break;
        }
    }

    private static string Label(CropShape shape) => shape switch
    {
        CropShape.Square => "1:1",
        CropShape.Portrait => "9:16",
        _ => "Free",
    };

    private string Warmth() => edit.Tint switch
    {
        0 => "as taken",
        < 0 => $"cool {-edit.Tint}",
        _ => $"warm {edit.Tint}",
    };

    // The window always shows the edit as it stands, which is the only way to judge one.
    private void Repaint()
    {
        if (window is null || original is null)
        {
            return;
        }

        var shown = PhotoRender.Apply(original, edit);
        window.SetImageBitmap(shown ?? original);
        BuildToolRow();
    }

    private void Send()
    {
        try
        {
            Flatten();
        }
        catch (Exception e)
        {
            // The original is untouched, so nothing is lost — which is what makes it safe to
            // say so and stay put rather than leave with a half-written file.
            Android.Util.Log.Warn("Plate", $"Could not write the edit: {e}");
            return;
        }

        SetResult((Result)SentResult, WithId());
        Finish();
    }

    // Only a picture the owner actually changed is written back. Re-encoding a JPEG they
    // did not touch loses quality for nothing, every single time.
    private void Flatten()
    {
        if (original is null || edit.IsUnchanged)
        {
            return;
        }

        using var edited = PhotoRender.Apply(original, edit);
        if (edited is null)
        {
            return;
        }

        var file = new Java.IO.File(files.Directory(), itemId);
        using var output = System.IO.File.Create(file.AbsolutePath!);
        edited.Compress(Bitmap.CompressFormat.Jpeg!, 92, output);
    }

    private void Delete()
    {
        SetResult((Result)DeletedResult, WithId());
        Finish();
    }

    private Intent WithId()
    {
        var result = new Intent();
        result.PutExtra(ItemExtra, itemId);
        return result;
    }

    public override void OnBackPressed()
    {
        // Nothing is written on the way out: an editor that saved as it went would have
        // decided for the owner.
        SetResult(Result.Canceled);
        base.OnBackPressed();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        original?.Recycle();
        original = null;
    }
}
