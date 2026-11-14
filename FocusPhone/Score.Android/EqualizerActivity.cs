using Android.App;
using Android.Content;
using Android.Media.Audiofx;
using Android.OS;
using Android.Views;
using Android.Widget;
using Score.Google;
using Codex.Domain.Design;
using Codex.Domain.Music;
using Codex.Ui;

namespace Score;

/// <summary>
/// The equalizer band editor: a slider per hardware band, the owner's saved presets (tap to apply, long-press
/// to delete, undo-first), "Save as preset", sound check and an "extra loudness" boost. Reads the device's
/// band count and range from a throwaway Equalizer bound to the global output mix, edits the stored
/// <see cref="EqualizerLibrary"/>, and on save persists it and tells the playback service to re-apply. If the
/// device refuses audio effects, the screen says so rather than crashing.
/// </summary>
[Activity(Label = "Equalizer", Theme = "@style/Theme.FocusPhone.Paper")]
public class EqualizerActivity : Activity
{
    private const int MaxLoudnessMillibels = 1500;

    private EqualizerStore store = null!;
    private readonly DeviceAccountTokenProvider tokens = new();
    private EqualizerLibrary library = EqualizerLibrary.Flat(0);
    private bool loaded;
    private FrameLayout? overlay;
    private short bandMin;
    private short bandMax;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TapWatch.Attach(this);
        PaperTransitions.Apply(this);
        store = new EqualizerStore(this);
        SetContentView(BuildLayout());
    }

    // Names the tab ScoreActivity should land on once this screen finishes (see
    // ScoreActivity.PendingArea); ReorderToFront brings its existing instance forward rather
    // than creating a second one, or starts it fresh if it isn't running at all. Reachable from
    // Music, Playing and Settings alike, so — like the other multi-entry screens here — it
    // lights one fixed home tab rather than tracking which one actually launched it.
    private void SwitchArea(int index)
    {
        ScoreActivity.PendingArea = index;
        var intent = new Intent(this, typeof(ScoreActivity));
        intent.AddFlags(ActivityFlags.ReorderToFront);
        StartActivity(intent);
        Finish();
    }

    // Mirror equalizer curve/presets/loudness up to Drive on the way out, so a tuning change here is backed
    // up even if the app is killed before the home screen next pauses. Best-effort and off the UI thread.
    protected override void OnPause()
    {
        base.OnPause();
        var sync = new PodcastLibrarySync(
            tokens.GetAccessToken, "Score",
            new PodcastStore(this), new SubscriptionStore(this), new PlaybackProgressStore(this),
            new PlaybackSettingsStore(this), store);
        Task.Run(async () =>
        {
            try
            {
                await sync.BackupAsync(System.Threading.CancellationToken.None);
            }
            catch
            {
                // Best-effort: the home screen's backup catches the cloud copy up next time.
            }
        });
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };

        int[] centerFrequencies;
        short bandCount;
        try
        {
            using var probe = new Equalizer(0, 0);
            bandCount = probe.NumberOfBands;
            var range = probe.GetBandLevelRange();
            bandMin = range is { Length: >= 2 } ? range[0] : (short)-1500;
            bandMax = range is { Length: >= 2 } ? range[1] : (short)1500;
            centerFrequencies = new int[bandCount];
            for (short band = 0; band < bandCount; band++)
            {
                centerFrequencies[band] = probe.GetCenterFreq(band);
            }
        }
        catch (Exception e) when (e is Java.Lang.RuntimeException or Java.Lang.UnsupportedOperationException)
        {
            content.AddView(PaperUi.Body(this, "This device doesn't offer an equalizer."));
            root.AddView(Framed(content), Fill());
            root.AddView(PaperUi.BottomBar(this, PaperUi.SecondaryButton(this, "Back", Finish)));
            return root;
        }

        // Read once; a rebuild after a preset is applied keeps the edits in hand rather than the stored copy.
        if (!loaded)
        {
            var stored = store.LoadLibrary();
            library = stored is not null && stored.Active.BandGains.Count == bandCount
                ? stored
                : EqualizerLibrary.Flat(bandCount);
            loaded = true;
        }

        var enabledButton = PaperUi.SecondaryButton(this, EnabledLabel(), () => { });
        enabledButton.Click += (_, _) =>
        {
            library = library.EditActive(active => active with { Enabled = !active.Enabled });
            enabledButton.Text = EnabledLabel();
        };
        content.AddView(enabledButton);

        for (short band = 0; band < bandCount; band++)
        {
            content.AddView(BuildBandRow(band, centerFrequencies[band]));
        }

        BuildPresets(content);

        content.AddView(PaperUi.Secondary(this,
            "Sound check evens out loudness between tracks that carry a ReplayGain tag. Tracks without one play as they are."));
        var soundCheck = PaperUi.SecondaryButton(this, SoundCheckLabel(), () => { });
        soundCheck.Click += (_, _) =>
        {
            store.SoundCheck = !store.SoundCheck;
            soundCheck.Text = SoundCheckLabel();
        };
        content.AddView(soundCheck);

        content.AddView(PaperUi.Secondary(this, "Extra loudness"));
        content.AddView(BuildLoudnessRow());

        var scroll = new ScrollView(this);
        scroll.AddView(content);
        var (framed, host) = BinderFrame.WrapWithOverlay(
            this, "Equalizer", ScoreShellAreas.All[ScoreShellAreas.Music].Hue, scroll,
            ScoreShellAreas.All, ScoreShellAreas.Music, SwitchArea);
        overlay = host;
        root.AddView(framed, Fill());

        root.AddView(PaperUi.BottomBar(this,
            PaperUi.SecondaryButton(this, "Save", Save),
            PaperUi.QuietButton(this, "Back", Finish)));
        return root;
    }

    private View Framed(View content) => BinderFrame.Wrap(
        this, "Equalizer", ScoreShellAreas.All[ScoreShellAreas.Music].Hue, content,
        ScoreShellAreas.All, ScoreShellAreas.Music, SwitchArea);

    private View BuildBandRow(short band, int centerFrequencyMilliHz)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        row.AddView(PaperUi.Secondary(this, FrequencyLabel(centerFrequencyMilliHz)),
            new LinearLayout.LayoutParams(PaperTheme.Dp(this, DesignTokens.Space.X12), ViewGroup.LayoutParams.WrapContent));

        var slider = new SeekBar(this) { Max = bandMax - bandMin, Progress = library.Active.BandGains[band] - bandMin };
        slider.ProgressChanged += (_, e) =>
        {
            if (e.FromUser)
            {
                library = library.EditActive(active => active.WithBand(band, (short)(e.Progress + bandMin)));
            }
        };
        row.AddView(slider, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        return row;
    }

    private View BuildLoudnessRow()
    {
        var slider = new SeekBar(this) { Max = MaxLoudnessMillibels, Progress = Math.Clamp(store.LoudnessMillibels, 0, MaxLoudnessMillibels) };
        slider.ProgressChanged += (_, e) =>
        {
            if (e.FromUser)
            {
                store.LoudnessMillibels = e.Progress;
            }
        };
        return slider;
    }

    private void Save()
    {
        store.SaveLibrary(library);
        var apply = new Intent(this, typeof(PlaybackService));
        apply.SetAction(AudioPlaybackCommands.ActionApplyAudioEffects);
        StartService(apply);
        Finish();
    }

    private string EnabledLabel() => library.Active.Enabled ? "Equalizer: on" : "Equalizer: off";

    private string SoundCheckLabel() => store.SoundCheck ? "Sound check: on" : "Sound check: off";

    // The saved presets (W7.3). A preset is a curve, so it is the one thing on this screen with a name: tap
    // applies it, long-press offers deleting it, and the current curve can be saved under a new name. The
    // playback service remembers which preset each output device was left on.
    private void BuildPresets(LinearLayout content)
    {
        content.AddView(PaperUi.Secondary(this, library.Presets.Count == 0
            ? "No presets yet. Shape a curve, then save it under a name."
            : library.InForce is { } inForce ? $"Presets — {inForce} is on" : "Presets"));

        foreach (var preset in library.Presets)
        {
            var name = preset.Name;
            var row = PaperUi.SecondaryButton(this, name, () => ApplyPreset(name));
            row.LongClick += (_, _) => OfferDelete(name);
            content.AddView(row);
        }

        var field = PaperUi.Field(this, "Preset name");
        content.AddView(field);
        content.AddView(PaperUi.QuietButton(this, "Save as preset", () =>
        {
            var name = (field.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return;
            }
            library = library.SavePreset(name);
            Rebuild();
        }));
    }

    private void ApplyPreset(string name)
    {
        library = library.ApplyPreset(name);
        Rebuild();
    }

    private void OfferDelete(string name)
    {
        if (overlay is null)
        {
            return;
        }
        ActionCard.Show(this, overlay, name, null, new CardAction("Delete preset", () =>
        {
            var before = library;
            library = library.DeletePreset(name);
            Rebuild();
            if (overlay is { } host)
            {
                DogEar.Show(this, host, $"Deleted {name}.", onUndo: () =>
                {
                    library = before;
                    Rebuild();
                });
            }
        }, Destructive: true));
    }

    private void Rebuild() => SetContentView(BuildLayout());

    private static string FrequencyLabel(int milliHz)
    {
        var hz = milliHz / 1000;
        return hz >= 1000 ? $"{hz / 1000} kHz" : $"{hz} Hz";
    }

    private static LinearLayout.LayoutParams Fill() =>
        new(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 };
}
