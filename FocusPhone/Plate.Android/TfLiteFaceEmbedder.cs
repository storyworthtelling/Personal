using Android.Content;
using Android.Graphics;
using Java.Nio;
using Xamarin.TensorFlow.Lite;

namespace Plate;

/// <summary>
/// Turns a face crop into numbers, using a TensorFlow Lite model shipped in the APK's
/// assets.
///
/// <para><b>The model is not in this repository.</b> Which one to ship is a decision with a
/// licence and a training-data provenance attached — the faces being matched are the owner's
/// friends — so it is theirs to make, and this class simply reports
/// <see cref="IsReady"/> = false until a file appears at <see cref="AssetName"/>. Everything
/// above it keeps working: faces are still found, and the name can still be written by
/// hand.</para>
///
/// <para>The shapes below are the MobileFaceNet family's conventions, which is what the
/// candidates all follow: a 112x112 RGB crop, each channel shifted to roughly -1..1, and an
/// embedding out. The output length is read from the model rather than assumed, so swapping
/// a 128-wide model for a 192-wide one needs no code change — and <c>FaceBook</c> already
/// refuses to compare vectors of different lengths, so a swap fails loudly instead of
/// producing confident nonsense.</para>
/// </summary>
public sealed class TfLiteFaceEmbedder : IFaceEmbedder, IDisposable
{
    /// <summary>Where the model goes: <c>Assets/face-embedder.tflite</c> in this head.</summary>
    public const string AssetName = "face-embedder.tflite";

    /// <summary>The MobileFaceNet family's input size.</summary>
    public const int InputSize = 112;

    private readonly Interpreter? interpreter;
    private readonly int outputLength;

    public TfLiteFaceEmbedder(Context context)
    {
        try
        {
            using var asset = context.Assets!.OpenFd(AssetName);
            using var stream = asset.CreateInputStream()!;
            var model = ByteBuffer.AllocateDirect((int)asset.Length)!;
            model.Order(ByteOrder.NativeOrder());

            var bytes = new byte[asset.Length];
            var read = 0;
            while (read < bytes.Length)
            {
                var got = stream.Read(bytes, read, bytes.Length - read);
                if (got <= 0)
                {
                    break;
                }
                read += got;
            }
            model.Put(bytes);
            model.Rewind();

            interpreter = new Interpreter(model);
            interpreter.AllocateTensors();
            // Read the width from the model rather than assuming it.
            var shape = interpreter.GetOutputTensor(0)!.Shape()!;
            outputLength = shape[^1];
        }
        catch (Exception e)
        {
            // No model in assets is the ordinary case today, not a fault: the head ships
            // without one until the owner has chosen which to ship.
            Android.Util.Log.Info("Plate", $"No face embedder: {e.Message}");
            interpreter = null;
        }
    }

    public bool IsReady => interpreter is not null && outputLength > 0;

    public float[] Embed(Bitmap face)
    {
        if (interpreter is null || outputLength <= 0)
        {
            return [];
        }

        try
        {
            using var scaled = Bitmap.CreateScaledBitmap(face, InputSize, InputSize, filter: true)!;
            var input = ByteBuffer.AllocateDirect(InputSize * InputSize * 3 * sizeof(float))!;
            input.Order(ByteOrder.NativeOrder());

            var pixels = new int[InputSize * InputSize];
            scaled.GetPixels(pixels, 0, InputSize, 0, 0, InputSize, InputSize);
            foreach (var pixel in pixels)
            {
                // (v - 127.5) / 128, the normalisation this model family was trained with.
                input.PutFloat((((pixel >> 16) & 0xFF) - 127.5f) / 128f);
                input.PutFloat((((pixel >> 8) & 0xFF) - 127.5f) / 128f);
                input.PutFloat(((pixel & 0xFF) - 127.5f) / 128f);
            }
            input.Rewind();

            var output = ByteBuffer.AllocateDirect(outputLength * sizeof(float))!;
            output.Order(ByteOrder.NativeOrder());
            interpreter.Run(input, output);
            output.Rewind();

            var embedding = new float[outputLength];
            for (var i = 0; i < outputLength; i++)
            {
                embedding[i] = output.GetFloat();
            }

            // L2-normalised, which is what cosine distance assumes and what makes one
            // threshold mean the same thing on every picture.
            var length = Math.Sqrt(embedding.Sum(value => (double)value * value));
            if (length <= 0)
            {
                return [];
            }
            return [.. embedding.Select(value => (float)(value / length))];
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("Plate", $"Could not embed a face: {e.Message}");
            return [];
        }
    }

    public void Dispose()
    {
        interpreter?.Close();
        interpreter?.Dispose();
    }
}
