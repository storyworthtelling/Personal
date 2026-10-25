using System.Text.RegularExpressions;
using System.Xml.Linq;
using Constitution.Domain;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// The partner's camera (C-6.5.2, D105) is compile-checked, not run, so what can be said about it is said by reading the source:
/// the one permission is CAMERA and sits in its own file, the capture goes to a FileProvider file under the app cache and nowhere
/// public, the photograph is deleted on every path, nothing recognised or located reaches the trail, the permission is asked for at
/// the tap after a sentence, and the picker row is live only through the reader. Nothing here has met a camera or ML Kit.
/// </summary>
public class CameraSourceTests
{
    private const string PermissionFile = "Constitution.Android/RecipeCameraPermission.cs";
    private const string LauncherFile = "Constitution.Android/MealsTab/CameraCaptureLauncher.cs";
    private const string ReaderFile = "Constitution.Android/MealsTab/AndroidRecipeTextReader.cs";
    private const string ProviderFile = "Constitution.Android/MealsTab/RecipeCaptureProvider.cs";
    private const string PathsFile = "Constitution.Android/Resources/xml/capture_paths.xml";
    private const string AreaFile = "Constitution.Android/MealsArea.cs";

    private static readonly Regex StorageOutsideTheCache = new(
        @"ExternalFilesDir|GetExternalStorage|ExternalStorage|ExternalCacheDir|\bFilesDir\b|Environment\.Directory|MediaStore\.(?!ActionImageCapture\b|ExtraOutput\b)|DCIM|Pictures",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static IEnumerable<string> HeadFiles() =>
        ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/", StringComparison.Ordinal));

    private static IEnumerable<string> CameraFiles() => [Read(LauncherFile), Read(ReaderFile), Read(ProviderFile)];

    [Fact]
    public void TheGuardReadsTheCameraFilesAndIsNotVacuous()
    {
        foreach (var file in new[] { PermissionFile, LauncherFile, ReaderFile, ProviderFile, PathsFile })
        {
            Assert.True(File.Exists(Path.Combine(ConstitutionSource.Root(), file.Replace('/', Path.DirectorySeparatorChar))), file + " is missing.");
        }
    }

    // ---- the permission ----

    [Fact]
    public void TheCameraPermissionIsDeclaredOnceInItsOwnFile()
    {
        var holders = HeadFiles()
            .Where(file => File.ReadAllText(file).Contains("Permission.Camera", StringComparison.Ordinal))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.Equal(new[] { PermissionFile, AreaFile }.Order(StringComparer.Ordinal).ToList(), holders.Order(StringComparer.Ordinal).ToList());
        Assert.Contains("[assembly: UsesPermission(Name = Android.Manifest.Permission.Camera)]", Read(PermissionFile), StringComparison.Ordinal);
    }

    [Fact]
    public void ADeviceWithoutACameraStillInstalls()
    {
        var source = Read(PermissionFile);

        Assert.Matches(@"UsesFeature\(""android\.hardware\.camera"", Required = false\)", source);
        Assert.Matches(@"UsesFeature\(""android\.hardware\.camera\.autofocus"", Required = false\)", source);
        Assert.DoesNotMatch(@"Required = true", source);
    }

    [Fact]
    public void NothingElseIsAskedForAtRunTimeButCameraAndHealth()
    {
        var asks = HeadFiles()
            .Select(File.ReadAllLines)
            .Select(lines => string.Join("\n", lines.Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))))
            .SelectMany(text => Regex.Matches(text, @"RequestPermissions\(([^;]*)\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).Select(match => match.Groups[1].Value))
            .ToList();

        Assert.Equal(2, asks.Count);
        Assert.Contains(asks, ask => ask.Contains("Android.Manifest.Permission.Camera", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePermissionIsAskedAtTheTapAfterASentence()
    {
        var area = Read(AreaFile);

        var sentence = area.IndexOf("CameraAccess.AskingSentence", StringComparison.Ordinal);
        var ask = area.IndexOf("Host.RequestPermissions(", StringComparison.Ordinal);

        Assert.True(sentence >= 0 && ask > sentence, "The asking sentence must be set before the permission is requested.");
        Assert.Contains("CameraAccess.Decide(", area, StringComparison.Ordinal);
        Assert.Contains("CameraAccess.DeniedSentence", area, StringComparison.Ordinal);
        Assert.Contains("CameraStep.AskFirst", area, StringComparison.Ordinal);
        Assert.Contains("CameraStep.NoCamera", area, StringComparison.Ordinal);
    }

    [Fact]
    public void ADenialLeavesTypingItInAndDiscardsTheLoop()
    {
        var area = Read(AreaFile);
        var denial = area[area.IndexOf("CameraAccess.DeniedSentence", StringComparison.Ordinal)..];

        Assert.Contains("cameraLoop = null", denial[..Math.Min(denial.Length, 400)], StringComparison.Ordinal);
        Assert.Contains("RecordCamera(CameraEvent.PermissionDenied", area, StringComparison.Ordinal);
    }

    // ---- where the photograph goes ----

    [Fact]
    public void TheCaptureIsAFileUnderTheAppCacheServedByTheProvider()
    {
        var launcher = Read(LauncherFile);

        Assert.Contains("MediaStore.ActionImageCapture", launcher, StringComparison.Ordinal);
        Assert.Contains("MediaStore.ExtraOutput", launcher, StringComparison.Ordinal);
        Assert.Contains("host.CacheDir", launcher, StringComparison.Ordinal);
        Assert.Contains("CameraCaptureCache.PathIn(", launcher, StringComparison.Ordinal);
        Assert.Contains("FileProvider.GetUriForFile", launcher, StringComparison.Ordinal);
        Assert.Contains("GrantWriteUriPermission", launcher, StringComparison.Ordinal);
        Assert.Contains("host.PackageName + RecipeCaptureProvider.AuthoritySuffix", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCameraFileTouchesPublicStorageOrTheMediaStore()
    {
        foreach (var source in CameraFiles().Append(Read(AreaFile)).Append(Read(PermissionFile)))
        {
            Assert.DoesNotMatch(StorageOutsideTheCache, source);
        }

        Assert.Matches(StorageOutsideTheCache, "var file = Environment.GetExternalStoragePublicDirectory(Environment.DirectoryPictures);");
        Assert.Matches(StorageOutsideTheCache, "intent.PutExtra(\"output\", MediaStore.Images.Media.ExternalContentUri);");
    }

    [Fact]
    public void TheCaptureIntentExistsInTheLauncherAlone()
    {
        var holders = HeadFiles()
            .Where(file => File.ReadAllText(file).Contains("ActionImageCapture", StringComparison.Ordinal))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.Equal([LauncherFile], holders);
    }

    [Fact]
    public void TheProviderIsPrivateAndServesTheCaptureFolderOnly()
    {
        var provider = Read(ProviderFile);
        var paths = XDocument.Parse(Read(PathsFile));

        Assert.Contains("Exported = false", provider, StringComparison.Ordinal);
        Assert.Contains("GrantUriPermissions = true", provider, StringComparison.Ordinal);
        Assert.Contains("\"${applicationId}\" + RecipeCaptureProvider.AuthoritySuffix", provider, StringComparison.Ordinal);
        Assert.Contains("AuthoritySuffix = \".capture\"", provider, StringComparison.Ordinal);
        Assert.DoesNotContain(ConstitutionIdentity.ApplicationId, provider, StringComparison.Ordinal);
        Assert.Contains("@xml/capture_paths", provider, StringComparison.Ordinal);

        var entry = Assert.Single(paths.Root!.Elements());
        Assert.Equal("cache-path", entry.Name.LocalName);
        Assert.Equal(CameraCaptureCache.Folder + "/", (string?)entry.Attribute("path"));
    }

    [Fact]
    public void TheFolderIsInTheCacheWhichBackupLeavesOut()
    {
        // Auto Backup never carries the cache directory, and NoCloudBackupTests holds AllowBackup=false besides; the capture must not
        // be moved to the files directory, which is the other half of that guarantee.
        foreach (var source in CameraFiles())
        {
            Assert.DoesNotContain("FilesDir", source, StringComparison.Ordinal);
        }
    }

    // ---- the photograph goes on every path ----

    [Fact]
    public void TheReaderDeletesThroughTheFinallyOfTheSharedHelper()
    {
        var reader = Read(ReaderFile);
        var domain = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Domain", "Recipes", "Screens", "CameraCapture.cs"));

        Assert.Contains("CameraCaptureCache.ReadAndForgetAsync(", reader, StringComparison.Ordinal);
        Assert.Matches(@"finally\s*\{[^}]*delete\(", domain);
        Assert.DoesNotMatch(@"catch\s*(\([^)]*\))?\s*\{\s*\}", reader);
        Assert.Matches(@"finally\s*\{[^}]*delete\(", "try { x(); } finally { delete(path); }");
    }

    [Fact]
    public void AStrayPhotoIsSweptBeforeEveryCapture()
    {
        var launcher = Read(LauncherFile);
        var start = launcher[launcher.IndexOf("Start(", StringComparison.Ordinal)..];

        Assert.Contains("Sweep(", start[..Math.Min(start.Length, 900)], StringComparison.Ordinal);
        Assert.Contains("public void Discard()", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void ABackedOutCaptureIsDiscardedAndAReadOneIsHandedToTheReader()
    {
        var area = Read(AreaFile);
        var handler = area[area.IndexOf("private void OnCameraResult(", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("// Recognition is slow", StringComparison.Ordinal)];

        Assert.Contains("OnCameraResult(resultCode)", area, StringComparison.Ordinal);
        Assert.Contains("CameraCaptureLauncher.RequestCode", area, StringComparison.Ordinal);
        Assert.Contains("capture.Discard()", handler, StringComparison.Ordinal);
        Assert.Contains("ReadPhoto(capture.PhotoPath)", handler, StringComparison.Ordinal);
        Assert.Contains("var reader = textReader;", area, StringComparison.Ordinal);
        Assert.Contains("reader.ReadPhotoAsync(photo)", area, StringComparison.Ordinal);
    }

    // ---- what the trail and the page may hold ----

    [Fact]
    public void OnlyThePhrasesOfTheTrailVocabularyAreWritten()
    {
        foreach (var source in CameraFiles().Append(Read(AreaFile)))
        {
            foreach (Match call in Regex.Matches(source, @"\.Outcome\(([^;]*)\);", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            {
                Assert.StartsWith("RecipeCameraTrail.LogPhrase(", call.Groups[1].Value.Trim(), StringComparison.Ordinal);
            }

            Assert.DoesNotContain("Android.Util.Log", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Console.Write", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RecognisedTextIsNeverAMessageOrAPhrase()
    {
        var reader = Read(ReaderFile);

        Assert.DoesNotContain("$\"", reader, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"Message\b", reader);
        Assert.DoesNotContain("AbsolutePath", Read(AreaFile), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingNeedsNoNetwork()
    {
        foreach (var source in CameraFiles())
        {
            Assert.DoesNotMatch(@"Http|WebRequest|Socket|Claude|Anthropic|UploadAsync", source);
        }
    }

    [Fact]
    public void NoPassphraseTokenOrKeyIsHandled()
    {
        var pattern = new Regex("(?<![A-Za-z])(passphrase|token|apikey)(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

        foreach (var source in CameraFiles())
        {
            Assert.DoesNotMatch(pattern, source);
        }
    }

    [Fact]
    public void NoHexOrTypeSizeIsWrittenInTheCameraFiles()
    {
        var literal = new Regex(
            @"""#[0-9A-Fa-f]{6,8}""|Color\.ParseColor\(\s*""|SetTextSize\([^;]*,\s*[0-9]|TextSize\s*=\s*[0-9]",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

        foreach (var source in CameraFiles())
        {
            Assert.DoesNotMatch(literal, source);
        }
    }

    [Fact]
    public void NoEmptyCatchAndNoToastAnywhereInTheCameraFiles()
    {
        foreach (var source in CameraFiles())
        {
            Assert.DoesNotMatch(@"catch\s*(\([^)]*\))?\s*\{\s*\}", source);
            Assert.DoesNotMatch(@"Toast|Snackbar", source);
        }
    }

    // ---- the picker and the loop ----

    [Fact]
    public void ThePickerRowIsLiveOnlyThroughTheReader()
    {
        var area = Read(AreaFile);

        Assert.Contains("cameraCanRead: textReader.CanRead", area, StringComparison.Ordinal);
        Assert.DoesNotContain("cameraCanRead: false", area, StringComparison.Ordinal);
        Assert.Contains("HasSystemFeature(PackageManager.FeatureCameraAny)", Read(ReaderFile), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLoopGivesOnePhotoPerPageAndForgetsOnDone()
    {
        var area = Read(AreaFile);

        Assert.Contains("new CameraLoop()", area, StringComparison.Ordinal);
        Assert.Contains("cameraLoop.AddPage(", area, StringComparison.Ordinal);
        Assert.Contains("cameraLoop.Finish()", area, StringComparison.Ordinal);
        Assert.Contains("cameraLoop.AddPageAction", area, StringComparison.Ordinal);
        Assert.Contains("cameraLoop.DoneAction", area, StringComparison.Ordinal);
        Assert.Contains("cameraLoop.CanFinish", area, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Add a page\"", area, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Done\"", area, StringComparison.Ordinal);
    }

    [Fact]
    public void ADoneCardTakesTheSameConfirmStep()
    {
        var area = Read(AreaFile);
        var finish = area[area.IndexOf("cameraLoop.Finish()", StringComparison.Ordinal)..];

        Assert.Contains("BeginDraft(", finish[..Math.Min(finish.Length, 700)], StringComparison.Ordinal);
    }

    [Fact]
    public void TheShellForwardsThePermissionAnswerToMeals()
    {
        var shell = Read("Constitution.Android/ConstitutionActivity.cs");

        Assert.Contains("mealsArea?.OnRequestPermissionsResult(", shell, StringComparison.Ordinal);
    }
}
