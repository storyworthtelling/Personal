using Codex.Ui;
using Android.Graphics;
using Codex.Domain.Plate;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Face;

namespace Plate;

/// <summary>Turns one cropped face into numbers. The step that needs a model.</summary>
public interface IFaceEmbedder
{
    /// <summary>Whether this phone actually has a model to embed with.</summary>
    bool IsReady { get; }

    /// <summary>The face as a vector, or empty when there is no model or it failed.</summary>
    float[] Embed(Bitmap face);
}

/// <summary>
/// Finding faces in a picture and, where possible, saying whose they are.
///
/// <para><b>Detection is ML Kit and always works.</b> Recognition needs a second thing — an
/// embedding model — and ML Kit does not provide one. The model is a separate binary
/// decision (size, licence, provenance) that belongs to the owner rather than to this code,
/// so it sits behind <see cref="IFaceEmbedder"/> and its absence degrades honestly: Plate
/// still finds the faces and simply says it does not know whose they are, instead of
/// pretending the feature is missing altogether.</para>
///
/// <para>None of this leaves the phone. Claude will not name a person in an image, and a
/// Darkroom picture has not been uploaded anywhere to be asked about in the first place.</para>
/// </summary>
public sealed class FaceRecogniser(IFaceEmbedder embedder)
{
    /// <summary>
    /// A face smaller than this is a bystander in the background, not the subject. Embedding
    /// one produces noise that then teaches the book something wrong.
    /// </summary>
    public const int SmallestUsefulFace = 64;

    public bool CanRecognise => embedder.IsReady;

    // Nested rather than file-scoped: a file-scoped type gets a mangled name with angle
    // brackets in it, and anything deriving from Java.Lang.Object needs a generated Java
    // peer class — whose name would then be invalid Java. The build says so in twelve
    // javac errors that never mention the word "file".
    private sealed class Found(TaskCompletionSource<Java.Lang.Object?> waiting)
        : Java.Lang.Object, Android.Gms.Tasks.IOnSuccessListener
    {
        public void OnSuccess(Java.Lang.Object? result) => waiting.TrySetResult(result);
    }

    private sealed class NotFound(TaskCompletionSource<Java.Lang.Object?> waiting)
        : Java.Lang.Object, Android.Gms.Tasks.IOnFailureListener
    {
        // A failure is "no faces", not an exception: the caller is a screen the owner is
        // already looking at.
        public void OnFailure(Java.Lang.Exception e) => waiting.TrySetResult(null);
    }

    /// <summary>
    /// Every face in the picture, largest first, each with the crop it came from. Empty when
    /// there are none, when detection fails, or when the phone cannot run it — all of which
    /// are the same thing to the caller: nothing to offer the owner.
    /// </summary>
    public async Task<IReadOnlyList<FoundFace>> FindAsync(Bitmap picture)
    {
        try
        {
            var options = new FaceDetectorOptions.Builder()
                .SetPerformanceMode(FaceDetectorOptions.PerformanceModeAccurate)
                // Landmarks ON, for one reason: a face embedder is markedly more accurate
                // on an eye-aligned crop, and the eyes are how we level it.
                .SetLandmarkMode(FaceDetectorOptions.LandmarkModeAll)
                .SetContourMode(FaceDetectorOptions.ContourModeNone)
                .SetMinFaceSize(0.1f)
                .Build();

            var detector = FaceDetection.GetClient(options);
            // The success/failure listener pair, which is how every other ML Kit call in
            // this head is written — the Tasks-to-Task extension is not used anywhere in
            // this repo, and one call reaching for it would be the odd one out.
            var waiting = new TaskCompletionSource<Java.Lang.Object?>();
            detector.Process(InputImage.FromBitmap(picture, 0))
                .AddOnSuccessListener(new Found(waiting))
                .AddOnFailureListener(new NotFound(waiting));
            var faces = await waiting.Task;
            detector.Close();

            var found = new List<FoundFace>();
            if (faces is not System.Collections.IEnumerable list)
            {
                return found;
            }

            foreach (var entry in list)
            {
                if (entry is not Face face || face.BoundingBox is not { } box)
                {
                    continue;
                }

                // Clamp: ML Kit's box can run past the edge on a face at the frame's border,
                // and Bitmap.CreateBitmap throws rather than clipping.
                var left = Math.Max(0, box.Left);
                var top = Math.Max(0, box.Top);
                var width = Math.Min(box.Width(), picture.Width - left);
                var height = Math.Min(box.Height(), picture.Height - top);
                if (width < SmallestUsefulFace || height < SmallestUsefulFace)
                {
                    continue;
                }

                var crop = Bitmap.CreateBitmap(picture, left, top, width, height)!;
                var levelled = Level(crop, face);
                found.Add(new FoundFace(levelled, embedder.Embed(levelled)));
                if (!ReferenceEquals(levelled, crop))
                {
                    crop.Recycle();
                }
            }

            return [.. found.OrderByDescending(entry => entry.Crop.Width * entry.Crop.Height)];
        }
        catch (Exception e)
        {
            // No faces. Never a crash on a picture the owner was only looking at.
            HeadLog.Warn("Plate", $"Face detection did not run: {e.Message}");
            return [];
        }
    }

    /// <summary>
    /// Rotates the crop so the eyes are level. Face embedders are trained on aligned faces
    /// and lose real accuracy without it — a head tilted thirty degrees reads as a different
    /// person often enough to matter. Returns the crop untouched when the landmarks are not
    /// there or the tilt is too small to be worth resampling for.
    /// </summary>
    private static Bitmap Level(Bitmap crop, Face face)
    {
        var left = face.GetLandmark(FaceLandmark.LeftEye)?.Position;
        var right = face.GetLandmark(FaceLandmark.RightEye)?.Position;
        if (left is null || right is null)
        {
            return crop;
        }

        var degrees = Math.Atan2(right.Y - left.Y, right.X - left.X) * 180 / Math.PI;
        if (Math.Abs(degrees) < 2)
        {
            return crop;
        }

        var matrix = new Matrix();
        matrix.PostRotate(-(float)degrees, crop.Width / 2f, crop.Height / 2f);
        return Bitmap.CreateBitmap(crop, 0, 0, crop.Width, crop.Height, matrix, filter: true)!;
    }
}

/// <summary>One face in a picture: the crop, and its numbers if a model could produce them.</summary>
public sealed record FoundFace(Bitmap Crop, float[] Descriptor)
{
    public bool CanBeMatched => Descriptor.Length > 0;
}

/// <summary>
/// The embedder when the phone has no model. Everything above it keeps working: faces are
/// still found and still shown, and the only thing missing is the name — which the owner can
/// write themselves.
/// </summary>
public sealed class NoFaceEmbedder : IFaceEmbedder
{
    public bool IsReady => false;

    public float[] Embed(Bitmap face) => [];
}
