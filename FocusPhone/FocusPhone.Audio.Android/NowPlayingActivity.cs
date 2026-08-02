using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using MediaController = AndroidX.Media3.Session.MediaController;
using FocusPhone.Domain.Design;
using FocusPhone.Domain.Music;
using FocusPhone.Ui;

namespace FocusPhone.Audio;

/// <summary>
/// The full-screen, iPod-faithful now-playing screen: large artwork, title / artist, a draggable scrubber
/// with elapsed and remaining clocks, transport, and shuffle / repeat. A thin adapter over a MediaController
/// — all the scrubber math and the repeat cycle live in the unit-tested <see cref="PlaybackTimeline"/> and
/// <see cref="PlaybackModes"/>.
/// </summary>
[Activity(Label = "Now Playing", Theme = "@style/Theme.FocusPhone.Paper")]
public class NowPlayingActivity : Activity
{
    private MediaController? controller;
    private Handler? uiHandler;
    private Java.Lang.Runnable? ticker;
    private bool dragging;

    private ImageView artwork = null!;
    private TextView titleView = null!;
    private TextView subtitleView = null!;
    private TextView elapsedView = null!;
    private TextView remainingView = null!;
    private SeekBar scrubber = null!;
    private Button playPause = null!;
    private Button shuffle = null!;
    private Button repeat = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        SetContentView(BuildLayout());
        ConnectController();
    }

    private View BuildLayout()
    {
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var header = PaperUi.PageHeader(this, "Now Playing");
        header.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        root.AddView(header);

        artwork = new ImageView(this);
        artwork.SetScaleType(ImageView.ScaleType.FitCenter);
        artwork.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X4), edge, PaperTheme.Dp(this, DesignTokens.Space.X4));
        root.AddView(artwork, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        titleView = PaperUi.Title(this, string.Empty);
        titleView.SetPadding(edge, 0, edge, 0);
        root.AddView(titleView);
        subtitleView = PaperUi.Secondary(this, string.Empty);
        subtitleView.SetPadding(edge, 0, edge, PaperTheme.Dp(this, DesignTokens.Space.X3));
        root.AddView(subtitleView);

        var scrubRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        scrubRow.SetGravity(GravityFlags.CenterVertical);
        scrubRow.SetPadding(edge, 0, edge, 0);
        elapsedView = PaperUi.Secondary(this, "0:00");
        remainingView = PaperUi.Secondary(this, "-0:00");
        scrubber = new SeekBar(this) { Max = 1000 };
        scrubber.ProgressChanged += (_, e) =>
        {
            if (e.FromUser)
            {
                UpdateClockLabels(e.Progress / 1000.0);
            }
        };
        scrubber.StartTrackingTouch += (_, _) => dragging = true;
        scrubber.StopTrackingTouch += (_, _) =>
        {
            dragging = false;
            SeekToFraction(scrubber.Progress / 1000.0);
        };
        scrubRow.AddView(elapsedView);
        scrubRow.AddView(scrubber, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        scrubRow.AddView(remainingView);
        root.AddView(scrubRow);

        var transport = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        transport.SetGravity(GravityFlags.Center);
        transport.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X3), edge, 0);
        transport.AddView(PaperUi.SecondaryButton(this, "⏮", () => controller?.SeekToPrevious()));
        playPause = PaperUi.PrimaryButton(this, "Play", TogglePlayPause);
        transport.AddView(playPause);
        transport.AddView(PaperUi.SecondaryButton(this, "⏭", () => controller?.SeekToNext()));
        root.AddView(transport);

        var modes = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        modes.SetGravity(GravityFlags.Center);
        shuffle = PaperUi.QuietButton(this, "Shuffle: off", ToggleShuffle);
        repeat = PaperUi.QuietButton(this, "Repeat: off", CycleRepeat);
        modes.AddView(shuffle);
        modes.AddView(repeat);
        root.AddView(modes);

        root.AddView(PaperUi.BottomBar(this, PaperUi.SecondaryButton(this, "Back", Finish)));
        return root;
    }

    private void ConnectController()
    {
        var token = new SessionToken(this, new ComponentName(this, Java.Lang.Class.FromType(typeof(PlaybackService))));
        var future = new MediaController.Builder(this, token).BuildAsync();
        future.AddListener(new Java.Lang.Runnable(() =>
        {
            controller = (MediaController)future.Get()!;
            StartTicking();
        }), AndroidX.Core.Content.ContextCompat.GetMainExecutor(this));
    }

    protected override void OnDestroy()
    {
        StopTicking();
        controller?.Release();
        base.OnDestroy();
    }

    private void StartTicking()
    {
        uiHandler ??= new Handler(Looper.MainLooper!);
        ticker ??= new Java.Lang.Runnable(Tick);
        uiHandler.PostDelayed(ticker, 500);
    }

    private void StopTicking()
    {
        if (uiHandler is not null && ticker is not null)
        {
            uiHandler.RemoveCallbacks(ticker);
        }
    }

    private void Tick()
    {
        Render();
        uiHandler?.PostDelayed(ticker!, 500);
    }

    private void Render()
    {
        if (controller is null)
        {
            return;
        }

        var metadata = controller.MediaMetadata;
        titleView.Text = metadata?.Title?.ToString() ?? string.Empty;
        subtitleView.Text = Join(metadata?.Artist?.ToString(), metadata?.AlbumTitle?.ToString());
        RenderArtwork(metadata);

        playPause.Text = controller.IsPlaying ? "Pause" : "Play";
        shuffle.Text = controller.ShuffleModeEnabled ? "Shuffle: on" : "Shuffle: off";
        repeat.Text = RepeatLabel(FromPlayerRepeat(controller.RepeatMode));

        var position = TimeSpan.FromMilliseconds(Math.Max(0, controller.CurrentPosition));
        var duration = controller.Duration > 0 ? TimeSpan.FromMilliseconds(controller.Duration) : TimeSpan.Zero;
        if (!dragging)
        {
            scrubber.Progress = (int)Math.Round(PlaybackTimeline.Fraction(position, duration) * 1000);
            elapsedView.Text = PlaybackTimeline.Elapsed(position);
            remainingView.Text = PlaybackTimeline.Remaining(position, duration);
        }
    }

    private void RenderArtwork(MediaMetadata? metadata)
    {
        var data = metadata?.ArtworkData;
        if (data is { Count: > 0 })
        {
            var bytes = data.ToArray();
            artwork.SetImageBitmap(BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length));
        }
        else
        {
            artwork.SetImageDrawable(null);
        }
    }

    private void UpdateClockLabels(double fraction)
    {
        if (controller is null)
        {
            return;
        }

        var duration = controller.Duration > 0 ? TimeSpan.FromMilliseconds(controller.Duration) : TimeSpan.Zero;
        var position = PlaybackTimeline.SeekTarget(fraction, duration);
        elapsedView.Text = PlaybackTimeline.Elapsed(position);
        remainingView.Text = PlaybackTimeline.Remaining(position, duration);
    }

    private void SeekToFraction(double fraction)
    {
        if (controller is null || controller.Duration <= 0)
        {
            return;
        }

        var target = PlaybackTimeline.SeekTarget(fraction, TimeSpan.FromMilliseconds(controller.Duration));
        controller.SeekTo((long)target.TotalMilliseconds);
    }

    private void TogglePlayPause()
    {
        if (controller is null)
        {
            return;
        }

        if (controller.IsPlaying)
        {
            controller.Pause();
        }
        else
        {
            controller.Play();
        }
    }

    private void ToggleShuffle()
    {
        if (controller is not null)
        {
            controller.ShuffleModeEnabled = !controller.ShuffleModeEnabled;
            Render();
        }
    }

    private void CycleRepeat()
    {
        if (controller is not null)
        {
            var modes = new PlaybackModes(FromPlayerRepeat(controller.RepeatMode), controller.ShuffleModeEnabled);
            controller.RepeatMode = ToPlayerRepeat(modes.CycleRepeat().Repeat);
            Render();
        }
    }

    private static string Join(string? artist, string? album)
    {
        var parts = new[] { artist, album }.Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(" · ", parts);
    }

    private static string RepeatLabel(RepeatMode mode) => mode switch
    {
        RepeatMode.All => "Repeat: all",
        RepeatMode.One => "Repeat: one",
        _ => "Repeat: off",
    };

    // Media3 Player repeat-mode contract values (REPEAT_MODE_OFF/ONE/ALL). The Xamarin binding doesn't
    // surface them as named constants on IPlayer, and these are stable, documented API values.
    private const int RepeatOff = 0;
    private const int RepeatOne = 1;
    private const int RepeatAll = 2;

    private static int ToPlayerRepeat(RepeatMode mode) => mode switch
    {
        RepeatMode.All => RepeatAll,
        RepeatMode.One => RepeatOne,
        _ => RepeatOff,
    };

    private static RepeatMode FromPlayerRepeat(int repeat) => repeat switch
    {
        RepeatAll => RepeatMode.All,
        RepeatOne => RepeatMode.One,
        _ => RepeatMode.Off,
    };
}
