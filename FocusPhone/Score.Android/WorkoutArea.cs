using Android.Views;
using Codex.Domain.Design;
using Codex.Domain.Music;
using Codex.Domain.Voice;
using Codex.Ui;

namespace Score;

/// <summary>
/// Workout: play at a tempo.
///
/// A <b>mode</b>, not a library. Nothing here is saved and nothing here is learned
/// from — the run is ephemeral and never reaches the taste profile, because music
/// chosen for its speed is evidence of a pace rather than of taste. Pause and End are
/// the whole model; there is nothing to come back to.
///
/// Bands say what you are doing, the dial corrects it once you are moving and can
/// feel that 168 is not 172. Skip advances <b>within</b> the band, holding the tempo:
/// on this screen skip means "not this song", never "not this speed".
/// </summary>
public sealed class WorkoutArea : ScoreArea
{
    private readonly MusicLibrary library;
    private readonly ScorePlayback playback;
    private readonly Codex.Ui.Voice.VoiceGreeting voice;

    private TempoBand band = TempoBand.Steady;
    private int tempo = TempoBands.Default(TempoBand.Steady);
    private bool running;

    // The voiced note, and what it belongs to. One line per track, so it changes when
    // the track does rather than on a clock.
    private string? note;
    private string? noteFor;
    private string? lastCategory;

    // Held so a turn of the dial can update the line under it without rebuilding the
    // pane — the dial is the thing under the finger, and replacing it mid-drag takes
    // the gesture with it.
    private Dial? dial;
    private FrameLayout? poolLine;

    // The tempo lookup, and what it is saying while it runs.
    private string? lookupStatus;
    private bool lookingUp;

    public WorkoutArea(Activity host, MusicLibrary library, ScorePlayback playback) : base(host)
    {
        this.library = library;
        this.playback = playback;
        voice = new Codex.Ui.Voice.VoiceGreeting(
            new Score.Google.DeviceAccountTokenProvider().GetAccessToken, "Score");
    }

    public override BinderArea Tab => new("Workout", Resource.Drawable.area_workout, PaperTheme.ScoreMark);

    // The screen already shows what is playing; a bar underneath repeating it is the
    // same information twice.
    public override bool ShowsNowPlayingBar => !running;

    public override bool GoBack()
    {
        // Leaving the tab does not end a run — the music keeps playing, which is the
        // point of a mode you start and then put the phone in a pocket.
        return false;
    }

    public override View BuildView()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.SetGravity(GravityFlags.CenterHorizontal);

        column.AddView(PaperUi.Segmented(
            Host,
            [.. TempoBands.Names],
            index => ChooseBand((TempoBand)index),
            (int)band,
            PaperTheme.ScoreMark));

        dial = new Dial(Host, new DialGeometry(TempoBands.Slowest, TempoBands.Fastest), tempo, PaperTheme.ScoreMark);

        // Turning keeps the count under the dial honest as the finger moves; settling
        // is what actually changes the music, so a drag across thirty BPM is one
        // change of tempo rather than thirty fades.
        dial.Turned += Retune;
        dial.Settled += _ => Settle();
        var size = PaperTheme.Dp(Host, DialSizeDp);
        column.AddView(dial, new LinearLayout.LayoutParams(size, size));

        poolLine = new FrameLayout(Host);
        FillBelowDial();
        column.AddView(poolLine);

        var scroll = new ScrollView(Host);
        scroll.AddView(column);
        return scroll;
    }

    private const int DialSizeDp = 254;

    public override View? BuildBottomBar() => running
        ? PaperUi.BottomBar(Host,
            PaperUi.QuietButton(Host, "Skip", () => playback.Controller?.SeekToNextMediaItem()),
            PaperUi.SecondaryButton(Host, playback.IsPlaying ? "Pause" : "Play", TogglePlayback),
            PaperUi.PrimaryButton(Host, "End", End))
        : PaperUi.BottomBar(Host, PaperUi.PrimaryButton(Host, "Start", Start));

    // A new track means a new line. Driven off the tick rather than a player callback
    // because the shell already has one, and a note arriving a second late is a note
    // arriving on time.
    public override void OnTick()
    {
        if (!running)
        {
            return;
        }

        var playing = playback.CurrentMediaId;
        if (playing is not null && playing != noteFor)
        {
            noteFor = playing;
            note = null;
            FillBelowDial();
            _ = ComposeNoteAsync(playing);
        }
    }

    // Everything under the dial: while a run is on, what is playing; otherwise, what
    // there is to play. Refilled in place rather than through a rebuild.
    private void FillBelowDial()
    {
        poolLine!.RemoveAllViews();
        poolLine.AddView(running ? BuildNowPlaying() : BuildPoolLine());
    }

    private View BuildPoolLine()
    {
        var pool = Pool();
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        column.AddView(PaperUi.Body(Host, pool.Tracks.Count switch
        {
            0 => $"Nothing at {tempo} BPM yet.",
            1 => $"One track near {tempo} BPM.",
            _ => $"{pool.Tracks.Count} tracks near {tempo} BPM.",
        }));

        // Said plainly rather than hidden: a thin pool is going to repeat itself, and
        // a run that loops without warning reads as the app being broken.
        if (pool.IsThin)
        {
            column.AddView(PaperUi.Secondary(Host,
                "That will loop. Friday's crate will be asked for more at this tempo."));

            // Offered only when the pool is thin, and only while there is anything left
            // to look up — a button that is usually there and usually does nothing
            // teaches you to press it for reassurance rather than for a reason.
            var unmeasured = library.Catalog.Songs().Count(track => track.Bpm <= 0);
            if (unmeasured > 0 && !lookingUp)
            {
                column.AddView(PaperUi.Secondary(Host,
                    $"{unmeasured} tracks have no tempo yet — most files never carry one."));
                column.AddView(PaperUi.SecondaryButton(Host, "Look up tempos", () => _ = LookUpTemposAsync()));
            }
        }

        if (!string.IsNullOrEmpty(lookupStatus))
        {
            column.AddView(PaperUi.Secondary(Host, lookupStatus));
        }

        return column;
    }

    private View BuildNowPlaying()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.AddView(PaperUi.Title(Host, playback.CurrentTitle ?? "…"));

        // Pinyon, per track. Hidden until there is a line — no key, no profile, or a
        // call that failed leaves the screen as it was.
        if (!string.IsNullOrWhiteSpace(note))
        {
            column.AddView(PaperUi.Voice(Host, note));
        }

        return column;
    }

    /// <summary>
    /// Asks a public catalogue for the tempos the files do not carry. Loading is a
    /// status sentence, never a spinner, and it counts up over a real library rather
    /// than saying "working…" for several minutes.
    /// </summary>
    private async Task LookUpTemposAsync()
    {
        lookingUp = true;
        lookupStatus = "Looking up tempos…";
        FillBelowDial();

        try
        {
            var found = await new TempoBackfill(Host).RunAsync(
                library.Catalog.Songs(),
                (done, total) =>
                {
                    lookupStatus = $"Looking up tempos… {done} of {total}.";
                    Host.RunOnUiThread(FillBelowDial);
                },
                CancellationToken.None);

            // The long tail nobody has measured anywhere is the normal outcome, and
            // saying so is better than implying the lookup failed.
            lookupStatus = found == 0
                ? "No tempos found for those."
                : $"Found {found}. Rescan isn't needed — they're in the library now.";
            library.LoadCatalog();
        }
        catch (Exception e)
        {
            lookupStatus = "Couldn't reach the tempo catalogue: " + e.Message;
        }
        finally
        {
            lookingUp = false;
            FillBelowDial();
        }
    }

    private TempoPool Pool() => TempoPool.Build(library.Catalog.Songs(), tempo);

    // Starting and ending change the bottom bar and the now-playing bar, which belong
    // to the shell — those are the only moments that need the whole pane rebuilt.
    private void Changed() => Invalidate();

    private void ChooseBand(TempoBand chosen)
    {
        band = chosen;
        tempo = TempoBands.Default(chosen);

        // The band moves the dial; the dial is not under a finger here, so it is told
        // rather than rebuilt.
        if (dial is not null)
        {
            dial.Value = tempo;
        }

        FillBelowDial();
        Settle();
    }

    private void Retune(int bpm)
    {
        tempo = bpm;
        band = TempoBands.Of(bpm);
        FillBelowDial();
    }

    // The tempo has stopped moving. Mid-run that means a new pool, faded into.
    private void Settle()
    {
        if (running)
        {
            Restart(fade: true);
        }
    }

    private void Start()
    {
        if (Pool().Tracks.Count == 0)
        {
            return;
        }

        running = true;
        Restart(fade: false);
    }

    private void Restart(bool fade)
    {
        var pool = Pool();
        if (pool.Tracks.Count == 0)
        {
            End();
            return;
        }

        // Shuffled, because closest-first would play the same handful in the same
        // order every time; the pool is already only tracks that keep step.
        library.StartWorkout(new ShufflePlanner().Shuffle(pool.Tracks), fade);
        RememberGap(pool);
        FillBelowDial();
        Changed();
    }

    private void TogglePlayback()
    {
        playback.TogglePlayPause();
        Changed();
    }

    private void End()
    {
        running = false;
        note = null;
        noteFor = null;
        playback.Controller?.Stop();
        Changed();
    }

    // A measurement, not a preference: "there is not enough music at this speed".
    // Friday reads it once and it is cleared, so a tempo the owner tried twice does
    // not stack up into a demand.
    private void RememberGap(TempoPool pool) => MusicSettingsStore.TempoGap = pool.Gap;

    private async Task ComposeNoteAsync(string trackId)
    {
        var category = VoicePicker.Pick(
            WorkoutVoice.Categories, WorkoutVoice.Weights, lastCategory, Random.Shared.NextDouble());
        if (category is null)
        {
            return;
        }

        lastCategory = category;
        var line = await voice.ComposeAsync(
            WorkoutVoice.Purpose(category),
            $"{playback.CurrentTitle} — running at {tempo} BPM",
            CancellationToken.None,
            highVisibility: false);

        // The track may have moved on while the line was being written; a note for a
        // song that finished is worse than no note.
        if (!string.IsNullOrWhiteSpace(line) && noteFor == trackId)
        {
            note = line;
            FillBelowDial();
        }
    }
}
