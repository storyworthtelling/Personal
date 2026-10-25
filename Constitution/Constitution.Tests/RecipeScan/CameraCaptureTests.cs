using Constitution.Domain.Diagnostics;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// The partner's camera capture (C-6.5.2, D105), as far as it is pure: where the one photo lives, when it must go, what the
/// permission step says and what the trail may record. The camera, the recognition and the permission dialog are the phone's and
/// nothing here has met them.
/// </summary>
public class CameraCaptureTests
{
    private const string Cache = "/data/user/0/app/cache";

    private static readonly string PageFile = Cache + "/recipe-capture/page.jpg";

    private static IReadOnlyList<string> Lines(params string[] lines) => lines;

    [Fact]
    public void TheFolderIsUnderTheAppCache()
    {
        Assert.Equal(PageFile, CameraCaptureCache.PathIn(Cache));
        Assert.Equal("recipe-capture", CameraCaptureCache.Folder);
    }

    [Fact]
    public void OnlyAFileInTheFolderIsACapture()
    {
        Assert.True(CameraCaptureCache.IsCaptureFile(Cache, PageFile));
        Assert.False(CameraCaptureCache.IsCaptureFile(Cache, "/storage/emulated/0/DCIM/Camera/page.jpg"));
        Assert.False(CameraCaptureCache.IsCaptureFile(Cache, Cache + "/recipe-capture/../shared/page.jpg"));
        Assert.False(CameraCaptureCache.IsCaptureFile(Cache, Cache + "/recipe-capture-other/page.jpg"));
        Assert.False(CameraCaptureCache.IsCaptureFile(Cache, Cache + "/recipe-capture/deeper/page.jpg"));
        Assert.False(CameraCaptureCache.IsCaptureFile(Cache, Cache + "/page.jpg"));
    }

    [Fact]
    public async Task ThePhotoGoesAfterAGoodRead()
    {
        var deleted = new List<string>();

        var lines = await CameraCaptureCache.ReadAndForgetAsync(
            Cache, PageFile, _ => Task.FromResult(Lines("Apple crisp")), path => { deleted.Add(path); return true; });

        Assert.Equal(["Apple crisp"], lines);
        Assert.Equal([PageFile], deleted);
    }

    [Fact]
    public async Task ThePhotoGoesWhenTheReadThrows()
    {
        var deleted = new List<string>();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CameraCaptureCache.ReadAndForgetAsync(
            Cache, PageFile, _ => throw new InvalidOperationException("recognition stopped"), path => { deleted.Add(path); return true; }));

        Assert.Equal("recognition stopped", failure.Message);
        Assert.Equal([PageFile], deleted);
    }

    [Fact]
    public async Task ThePhotoGoesWhenTheReadFailsLate()
    {
        var deleted = new List<string>();

        await Assert.ThrowsAsync<IOException>(() => CameraCaptureCache.ReadAndForgetAsync(
            Cache,
            PageFile,
            async _ =>
            {
                await Task.Yield();
                throw new IOException("gone");
            },
            path => { deleted.Add(path); return true; }));

        Assert.Equal([PageFile], deleted);
    }

    [Fact]
    public async Task AFileOutsideIsRefusedNotDeleted()
    {
        var deleted = new List<string>();
        var read = false;

        await Assert.ThrowsAsync<ArgumentException>(() => CameraCaptureCache.ReadAndForgetAsync(
            Cache,
            "/storage/emulated/0/DCIM/Camera/mine.jpg",
            _ => { read = true; return Task.FromResult(Lines("x")); },
            path => { deleted.Add(path); return true; }));

        Assert.False(read);
        Assert.Empty(deleted);
    }

    [Fact]
    public async Task AFailedDeleteIsReportedNotHidden()
    {
        bool? reported = null;

        await CameraCaptureCache.ReadAndForgetAsync(
            Cache, PageFile, _ => Task.FromResult(Lines("x")), _ => false, forgotten => reported = forgotten);

        Assert.False(reported);
    }

    [Fact]
    public async Task ADeleteThatWorkedIsReported()
    {
        bool? reported = null;

        await CameraCaptureCache.ReadAndForgetAsync(
            Cache, PageFile, _ => Task.FromResult(Lines("x")), _ => true, forgotten => reported = forgotten);

        Assert.True(reported);
    }

    [Fact]
    public void NoCameraIsToldWhateverIsGranted()
    {
        Assert.Equal(CameraStep.NoCamera, CameraAccess.Decide(hasCamera: false, granted: true));
        Assert.Equal(CameraStep.NoCamera, CameraAccess.Decide(hasCamera: false, granted: false));
    }

    [Fact]
    public void AnUnansweredPermissionIsAskedFirst()
    {
        Assert.Equal(CameraStep.AskFirst, CameraAccess.Decide(hasCamera: true, granted: false));
    }

    [Fact]
    public void AGrantedPermissionGoesToTheCamera()
    {
        Assert.Equal(CameraStep.Capture, CameraAccess.Decide(hasCamera: true, granted: true));
    }

    [Fact]
    public void AskingSaysWhyAndWhereThePhotoGoes()
    {
        var sentence = CameraAccess.AskingSentence;

        Assert.Contains("camera", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("on this phone", sentence, StringComparison.Ordinal);
        Assert.Contains("deleted", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ADenialIsSpecificAndLeavesTypingOpen()
    {
        var sentence = CameraAccess.DeniedSentence;

        Assert.Contains("camera was not allowed", sentence, StringComparison.Ordinal);
        Assert.Contains(RecipePickerText.TypeInTitle, sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCameraLeavesTypingOpen()
    {
        Assert.Contains("no camera", CameraAccess.NoCameraSentence, StringComparison.Ordinal);
        Assert.Contains(RecipePickerText.TypeInTitle, CameraAccess.NoCameraSentence, StringComparison.Ordinal);
    }

    [Fact]
    public void SentencesAreQuietAndComplete()
    {
        string[] all =
        [
            CameraAccess.AskingSentence, CameraAccess.DeniedSentence, CameraAccess.NoCameraSentence,
            CameraAccess.CaptureFailedSentence, CameraAccess.ReadFailedSentence,
        ];

        foreach (var sentence in all)
        {
            Assert.DoesNotContain("!", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("sorry", sentence, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".", sentence, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(CameraEvent.PageRead, "recipe camera: page read")]
    [InlineData(CameraEvent.NothingRead, "recipe camera: nothing read")]
    [InlineData(CameraEvent.PhotoDeleted, "recipe camera: photo deleted")]
    [InlineData(CameraEvent.PhotoKept, "recipe camera: photo not deleted")]
    [InlineData(CameraEvent.PermissionDenied, "recipe camera: permission denied")]
    public void TrailPhrasesNameTheStepOnly(CameraEvent step, string expected)
    {
        Assert.Equal(expected, RecipeCameraTrail.LogPhrase(step, null));
    }

    [Fact]
    public void AFailedReadNamesTheTypeNeverTheMessage()
    {
        var phrase = RecipeCameraTrail.LogPhrase(CameraEvent.ReadFailed, new IOException("secret lemon curd words"));

        Assert.Equal("recipe camera: read failed IOException", phrase);
        Assert.DoesNotContain("lemon", phrase, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTrailPhrasePassesTheFilter()
    {
        foreach (var step in Enum.GetValues<CameraEvent>())
        {
            var phrase = RecipeCameraTrail.LogPhrase(step, step == CameraEvent.ReadFailed ? new IOException("x") : null);
            var line = "[Oct 25, 3:04 pm] INFO Vault — " + phrase;

            Assert.Equal(line, TrailLines.Filter(line));
        }
    }

    [Fact]
    public void ThePageFileNameCarriesNoRecipeWords()
    {
        Assert.Matches("^page\\.jpg$", CameraCaptureCache.PageFileName);
    }
}
