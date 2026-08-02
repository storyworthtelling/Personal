using Android.App;
using Android.Content;
using Android.Media.Audiofx;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;
using FocusPhone.Domain.Music;
using FocusPhone.Ui;

namespace FocusPhone.Audio;

/// <summary>
/// The equalizer band editor: a slider per hardware band plus an "extra loudness" boost. Reads the device's
/// band count and range from a throwaway Equalizer bound to the global output mix, edits the stored
/// <see cref="EqualizerLibrary"/>, and on save persists it and tells the playback service to re-apply. If the
/// device refuses audio effects, the screen says so rather than crashing.
/// </summary>
[Activity(Label = "Equalizer", Theme = "@style/Theme.FocusPhone.Paper")]
public class EqualizerActivity : Activity
{
    private const int MaxLoudnessMillibels = 1500;

    private EqualizerStore store = null!;
    private EqualizerLibrary library = EqualizerLibrary.Flat(0);
    private short bandMin;
    private short bandMax;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        store = new EqualizerStore(this);
        SetContentView(BuildLayout());
    }

    private View BuildLayout()
    {
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var header = PaperUi.PageHeader(this, "Equalizer");
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        root.AddView(header);

        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(edge, 0, edge, 0);

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
            root.AddView(content, Fill());
            root.AddView(PaperUi.BottomBar(this, PaperUi.SecondaryButton(this, "Back", Finish)));
            return root;
        }

        var stored = store.LoadLibrary();
        library = stored is not null && stored.Active.BandGains.Count == bandCount
            ? stored
            : EqualizerLibrary.Flat(bandCount);

        var enabledButton = PaperUi.SecondaryButton(this, EnabledLabel(), () => { });
        enabledButton.Click += (_, _) =>
        {
            library = library with { Active = library.Active with { Enabled = !library.Active.Enabled } };
            enabledButton.Text = EnabledLabel();
        };
        content.AddView(enabledButton);

        for (short band = 0; band < bandCount; band++)
        {
            content.AddView(BuildBandRow(band, centerFrequencies[band]));
        }

        content.AddView(PaperUi.Secondary(this, "Extra loudness"));
        content.AddView(BuildLoudnessRow());

        var scroll = new ScrollView(this);
        scroll.AddView(content);
        root.AddView(scroll, Fill());

        root.AddView(PaperUi.BottomBar(this,
            PaperUi.SecondaryButton(this, "Save", Save),
            PaperUi.QuietButton(this, "Back", Finish)));
        return root;
    }

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
                library = library with { Active = library.Active.WithBand(band, (short)(e.Progress + bandMin)) };
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

    private static string FrequencyLabel(int milliHz)
    {
        var hz = milliHz / 1000;
        return hz >= 1000 ? $"{hz / 1000} kHz" : $"{hz} Hz";
    }

    private static LinearLayout.LayoutParams Fill() =>
        new(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 };
}
