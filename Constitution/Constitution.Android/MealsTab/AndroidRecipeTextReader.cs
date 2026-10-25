using Android.App;
using Android.Content.PM;
using Constitution.Domain.Recipes.Screens;
using Constitution.Services;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using MlText = Xamarin.Google.MLKit.Vision.Text.Text;

namespace Constitution.MealsTab;

/// <summary>
/// Reads one photographed recipe card page into lines, on the phone (C-6.5.2, D105): Google's ML Kit text recognition running
/// on-device, the package Plate carries. No image is sent anywhere, and the photograph is deleted the moment the reading is over,
/// whatever it came to (<see cref="CameraCaptureCache.ReadAndForgetAsync"/> deletes in a <c>finally</c>). Thin: the order of read
/// and delete is the domain's; the trail phrases are the domain's and carry no recognised word and no path.
/// </summary>
internal sealed class AndroidRecipeTextReader(Activity host) : IRecipeTextReader
{
    /// <summary>Whether this phone has a camera at all. The permission is asked for at the tap, not here.</summary>
    public bool CanRead => host.PackageManager?.HasSystemFeature(PackageManager.FeatureCameraAny) == true;

    public Task<IReadOnlyList<string>> ReadPhotoAsync(string photoPath) =>
        CameraCaptureCache.ReadAndForgetAsync(host.CacheDir!.AbsolutePath, photoPath, Recognize, CameraCaptureLauncher.Delete, OnForgotten);

    private void OnForgotten(bool gone) =>
        new ConstitutionVaultLog(host.ApplicationContext ?? host).Outcome(
            RecipeCameraTrail.LogPhrase(gone ? CameraEvent.PhotoDeleted : CameraEvent.PhotoKept, null));

    private async Task<IReadOnlyList<string>> Recognize(string path)
    {
        InputImage image;
        try
        {
            image = InputImage.FromFilePath(host, Android.Net.Uri.FromFile(new Java.IO.File(path))!);
        }
        catch (Java.IO.IOException e)
        {
            throw new RecipeTextReadException("The photo could not be opened for reading.", e);
        }

        var recognizer = TextRecognition.GetClient(Xamarin.Google.MLKit.Vision.Text.Latin.TextRecognizerOptions.DefaultOptions!);
        try
        {
            var done = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            recognizer.Process(image)
                .AddOnSuccessListener(new Success(done))
                .AddOnFailureListener(new Failure(done));
            return await done.Task.ConfigureAwait(false);
        }
        finally
        {
            recognizer.Close();
        }
    }

    private sealed class Success(TaskCompletionSource<IReadOnlyList<string>> done) : Java.Lang.Object, Android.Gms.Tasks.IOnSuccessListener
    {
        public void OnSuccess(Java.Lang.Object? result)
        {
            var lines = new List<string>();
            foreach (var block in (result as MlText)?.TextBlocks ?? [])
            {
                foreach (var line in block?.Lines ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(line?.Text))
                    {
                        lines.Add(line.Text!);
                    }
                }
            }

            done.TrySetResult(lines);
        }
    }

    private sealed class Failure(TaskCompletionSource<IReadOnlyList<string>> done) : Java.Lang.Object, Android.Gms.Tasks.IOnFailureListener
    {
        public void OnFailure(Java.Lang.Exception e) =>
            done.TrySetException(new RecipeTextReadException("Text recognition did not finish.", e));
    }
}

/// <summary>
/// A photograph that could not be read. Its own text is fixed and says nothing of the page; the platform's failure rides inside it
/// with its stack, and only this type's name is ever written to the trail.
/// </summary>
internal sealed class RecipeTextReadException(string description, Exception inner) : Exception(description, inner);
