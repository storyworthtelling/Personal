using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Meals;
using Constitution.Domain.Notices;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;
using Constitution.Infrastructure.Meals;
using Constitution.Services;
using Constitution.Settings;

namespace Constitution.MealsTab;

/// <summary>
/// "I ate something else" on the phone (C-6.6.1, C-6.6.2, book p.15), shared by Today and Meals so both cards say the same thing.
/// The pinned bar's action opens the one camera (Plate's on the owner's phone, the system camera on the partner's), the answer
/// comes back to the meal's card, which says "Estimating your meal…" and then the figure, with no screen between and nothing to approve.
/// The words and the order are <see cref="AteSomethingElseModel"/>'s; this owns the camera, the permission and the disk.
///
/// <para><b>The photograph</b> is written by the camera into the same cache file the recipe scan uses
/// (<see cref="CameraCaptureLauncher"/>, served by the same provider) and is read once, shrunk and re-encoded
/// (<see cref="AtePhotoScaler"/>); <see cref="AtePhoto"/> deletes it on success, on failure and on an exception. Nothing here keeps
/// the picture, a path to it or an address for it.</para>
///
/// <para><b>What is kept</b> is the estimate (a day, a meal, a description and ranges), in one file under the app's no-backup folder
/// (<see cref="FileAteEstimates"/>): not in the household's records, not in Drive, not in the backup. The dog-ear's fold, turned
/// within its window, takes the estimate back (undo-first); nothing is written to Health Connect.</para>
/// </summary>
public sealed class AteSomethingElseFlow
{
    /// <summary>The camera's answer comes back under this code. Health uses 7311, Plate's scan 7412, the recipe camera 7413 and 7414.</summary>
    public const int CaptureRequestCode = 7415;

    /// <summary>The camera permission's answer comes back under this code.</summary>
    public const int PermissionRequestCode = 7416;

    private readonly Activity host;
    private readonly Func<InstallRole> role;
    private readonly Func<bool> cameraAllowed;
    private readonly Action<int> askForCamera;
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly AteSomethingElseModel model = new();
    private readonly AteUndo undo = new();
    private readonly IAteEstimates store;
    private readonly ConstitutionVaultLog log;
    private CameraCaptureLauncher? capture;
    private bool loaded;

    /// <param name="host">The shell's activity.</param>
    /// <param name="role">Whose phone this is, which decides the camera (Plate's on the owner's).</param>
    /// <param name="cameraAllowed">Whether the camera is allowed now; asked of the one place that checks it (Meals).</param>
    /// <param name="askForCamera">Asks for the camera under the given code; the one place that requests it (Meals).</param>
    public AteSomethingElseFlow(Activity host, Func<InstallRole> role, Func<bool> cameraAllowed, Action<int> askForCamera)
    {
        this.host = host;
        this.role = role;
        this.cameraAllowed = cameraAllowed;
        this.askForCamera = askForCamera;
        var appContext = host.ApplicationContext ?? host;
        store = new FileAteEstimates(Path.Combine(appContext.NoBackupFilesDir!.AbsolutePath, "intake", "intake-estimates.json"));
        log = new ConstitutionVaultLog(appContext);
    }

    /// <summary>Raised on the UI thread whenever a card's line may have changed.</summary>
    public event Action? Changed;

    /// <summary>False while a photo is being taken or estimated: the bar's action waits.</summary>
    public bool CanStart => model.CanStart;

    /// <summary>What a meal's card says on a day, or null when it says nothing of this.</summary>
    public AteLine? LineFor(AteMeal meal, DateOnly day) => model.LineFor(meal, day);

    /// <summary>The meals that have a line today, in the day's order.</summary>
    public IReadOnlyList<AteMeal> MealsWithALine(DateOnly day) => model.MealsWithALine(day);

    /// <summary>
    /// Reads what was kept, once, off the UI thread; an unreadable file shows nothing and is said on the trail. Once is enough: this
    /// install is the file's only writer, so what is in memory is never older than what is on disk.
    /// </summary>
    public void LoadOnce()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        _ = Task.Run(() =>
        {
            AteStoreRead read;
            try
            {
                read = store.Read();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Outcome($"meal photo: read failed {e.GetType().Name}");
                return;
            }

            if (read.Unreadable)
            {
                log.Outcome("meal photo: unreadable");
            }

            host.RunOnUiThread(() =>
            {
                model.Load(read.Items);
                Changed?.Invoke();
            });
        });
    }

    /// <summary>The bar's action was tapped. The permission is asked for here, after a sentence that says why, never at launch.</summary>
    public void Start()
    {
        if (!model.CanStart)
        {
            return;
        }

        var hasCamera = host.PackageManager?.HasSystemFeature(PackageManager.FeatureCameraAny) == true;
        switch (CameraAccess.Decide(hasCamera, cameraAllowed()))
        {
            case CameraStep.NoCamera:
                Say(AteText.NoCameraSentence);
                break;
            case CameraStep.AskFirst:
                Say(AteText.AskingSentence);
                askForCamera(PermissionRequestCode);
                break;
            default:
                OpenCamera();
                break;
        }
    }

    /// <summary>The camera permission's answer, handed over by the shell.</summary>
    public void OnCameraAnswer(int requestCode, Permission[] grantResults)
    {
        if (requestCode != PermissionRequestCode)
        {
            return;
        }

        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
        {
            OpenCamera();
            return;
        }

        log.Outcome("meal photo: permission denied");
        Say(AteText.DeniedSentence);
    }

    /// <summary>The camera's answer, handed over by the shell. A back-out is quiet; a photo goes straight to be estimated.</summary>
    public void OnActivityResult(int requestCode, Result resultCode)
    {
        if (requestCode != CaptureRequestCode)
        {
            return;
        }

        if (resultCode != Result.Ok || capture is null)
        {
            capture?.Discard();
            model.CaptureBackedOut();
            Changed?.Invoke();
            return;
        }

        var photo = capture.PhotoPath;
        var cacheRoot = host.CacheDir!.AbsolutePath;
        model.PhotoTaken();
        Changed?.Invoke();
        _ = Task.Run(() => Estimate(cacheRoot, photo));
    }

    /// <summary>The fold was turned and the board gave back this key: take the estimate back if it is ours.</summary>
    public void UndoTurned(string actKey)
    {
        if (undo.Reverse(actKey) is not { } step)
        {
            return;
        }

        model.Withdraw(step.Recorded, step.Previous);
        _ = Task.Run(() =>
        {
            try
            {
                store.Withdraw(step.Recorded, step.Previous);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Outcome($"meal photo: read failed {e.GetType().Name}");
            }
        });
        Changed?.Invoke();
    }

    private void OpenCamera()
    {
        // The owner's phone has Plate, so the photograph is taken there; the partner's uses the system camera (D86).
        capture = new CameraCaptureLauncher(host, CaptureRequestCode, role() == InstallRole.Partner ? null : RecipeReadContract.PlatePackage);
        if (!model.StartCapture(DateTimeOffset.Now))
        {
            return;
        }

        if (capture.Start() is { } sentence)
        {
            log.Outcome("meal photo: camera failed");
            model.CaptureFailed(AteText.CaptureFailedSentence);
            Changed?.Invoke();
            return;
        }

        Changed?.Invoke();
    }

    private void Say(string sentence)
    {
        model.CaptureFailed(sentence);
        Changed?.Invoke();
    }

    // The estimate runs off the UI thread: the photograph is decoded, sent and deleted in here, whatever happens to it.
    private async Task Estimate(string cacheRoot, string photo)
    {
        AteOutcome outcome;
        try
        {
            outcome = await model.EstimateAsync(AteEstimatorServices.EstimatorOf(host), cacheRoot, photo, CameraCaptureLauncher.Delete, OnForgotten).ConfigureAwait(false);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or Java.Lang.Throwable)
        {
            // The type only goes on the trail: a message could quote the service. The photograph has already been deleted.
            log.Outcome($"meal photo: read failed {e.GetType().Name}");
            outcome = new AteOutcome.Unreadable();
        }

        log.Outcome(outcome switch
        {
            AteOutcome.Read => "meal photo: read",
            AteOutcome.NoMeal => "meal photo: nothing read",
            AteOutcome.Unreadable => "meal photo: unreadable",
            _ => "meal photo: failed",
        });
        host.RunOnUiThread(() => Finished(outcome));
    }

    private void Finished(AteOutcome outcome)
    {
        var recorded = model.Finish(outcome);
        Changed?.Invoke();
        if (recorded is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            AteEstimate? previous;
            try
            {
                previous = store.Record(recorded);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Outcome($"meal photo: read failed {e.GetType().Name}");
                return;
            }

            host.RunOnUiThread(() =>
            {
                ConnectionNoticeBoard.With(board =>
                {
                    undo.Begin(board, recorded, previous, DateTimeOffset.UtcNow);
                    return true;
                });
                handler.PostDelayed(() => undo.Expire(DateTimeOffset.UtcNow), (long)NoticeBoard.UndoWindow.TotalMilliseconds + 1);
            });
        });
    }

    private void OnForgotten(bool gone) => log.Outcome(gone ? "meal photo: deleted" : "meal photo: not deleted");
}
