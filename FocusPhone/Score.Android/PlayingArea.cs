using Android.Graphics;
using Android.Views;
using AndroidX.Media3.Common;
using Codex.Domain.Audio;
using Codex.Domain.Design;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Ui;

namespace Score;

/// <summary>
/// Now playing, as an area rather than a screen you leave to.
///
/// It wears the ordinary frame — header, stroke, tabs — because it is a place in the
/// app, not a modal over it. The consequence is that it gives up horizontal swipe for
/// changing tracks (that gesture is the system's edge-swipe back), which is the trade
/// that keeps "no back button anywhere" true. Prev and next are real targets instead.
///
/// A three-way switch under the header changes <b>what the page is showing</b> —
/// the same position and the same meaning it has on every other screen. Cover, the
/// queue, and the words: lyrics for music, show notes for a podcast. Title, scrubber
/// and transport never move as it changes; only the panel above them does.
/// </summary>
public sealed class PlayingArea : ScoreArea
{
    private const int PickArtCode = 7301;

    /// <summary>The art, big enough to be worth looking at (DESIGN.md §7 Artwork slot).</summary>
    private const int ArtworkSizeDp = 254;

    private readonly ScorePlayback playback;
    private readonly ArtworkOverrideStore artworkStore;
    private readonly PlaybackSettingsStore settingsStore;

    private ArtworkWindow artwork = null!;
    private TextView titleView = null!;
    private TextView subtitleView = null!;
    private TextView elapsedView = null!;
    private TextView remainingView = null!;
    private TextView notesView = null!;
    private FrameLayout panel = null!;
    private SeekBar scrubber = null!;
    private Button playPause = null!;
    private Button shuffle = null!;
    private Button repeat = null!;

    private Panel showing = Panel.Cover;
    private bool dragging;
    private string? pendingAlbumKey;
    private string? lastMediaId;

    // The words, and which track they belong to. Fetched once per track and held for
    // as long as it plays — they do not change, and the panel is opened and closed
    // more often than the track changes.
    private readonly ILyricsSource lyricsSource = new LrcLibClient(
        new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler()));
    private Lyrics lyrics = Lyrics.None;
    private string? lyricsFor;
    private readonly List<TextView> lyricViews = [];
    private ScrollView? lyricsScroll;
    private int litLine = -1;

    public PlayingArea(Android.App.Activity host, ScorePlayback playback) : base(host)
    {
        this.playback = playback;
        artworkStore = new ArtworkOverrideStore(host);
        settingsStore = new PlaybackSettingsStore(host);
    }

    public override BinderArea Tab => new("Playing", Resource.Drawable.area_playing, PaperTheme.ProgressMark, PaperTheme.ProgressFill);

    /// <summary>Named for the area, not for the track — the track has the page below.</summary>
    public override string Title => "Now playing";

    /// <summary>This screen <i>is</i> the current track, so the bar would only repeat it.</summary>
    public override bool ShowsNowPlayingBar => false;

    private enum Panel
    {
        Cover,
        Queue,
        Words,
    }

    public override View BuildView()
    {
        var edge = PaperTheme.Dp(Host, DesignTokens.Shape.ScreenEdgeDp);
        var root = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        root.AddView(BuildSwitch(), new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        panel = new FrameLayout(Host);
        panel.SetPadding(0, PaperTheme.Dp(Host, DesignTokens.Space.X4), 0, PaperTheme.Dp(Host, DesignTokens.Space.X4));
        root.AddView(panel, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        titleView = PaperUi.Title(Host, string.Empty);
        root.AddView(titleView);
        subtitleView = PaperUi.Secondary(Host, string.Empty);
        subtitleView.SetPadding(0, 0, 0, PaperTheme.Dp(Host, DesignTokens.Space.X3));
        root.AddView(subtitleView);

        root.AddView(BuildScrubber());
        root.AddView(BuildTransport());
        root.AddView(BuildModes());

        // Per-show speed belongs to the thing that is playing, so it lives here
        // rather than on the podcast shelf — and it is §7's picker expanding in
        // place, not the dialog it used to be.
        if (playback.Kind == MediaKind.Podcast)
        {
            root.AddView(BuildSpeed());
        }

        RenderPanel();
        Render();
        return root;
    }

    private View BuildSwitch()
    {
        // Words is live only when there is something to show — an episode with notes,
        // or a track somebody has typed the words up for. When there is nothing the
        // segment is dimmed rather than dropped, per §8, so the switch keeps its shape.
        var isPodcast = playback.Kind == MediaKind.Podcast;
        var hasWords = isPodcast ? !string.IsNullOrWhiteSpace(Notes()) : !lyrics.IsEmpty;

        return PaperUi.Segmented(
            Host,
            ["Cover", "Queue", isPodcast ? "Notes" : "Lyrics"],
            index =>
            {
                showing = (Panel)index;
                RenderPanel();
            },
            (int)showing,
            PaperTheme.ScoreMark,
            [true, playback.Queued().Count > 0, hasWords]);
    }

    private View BuildScrubber()
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        elapsedView = PaperUi.Secondary(Host, "0:00");
        remainingView = PaperUi.Secondary(Host, "-0:00");
        scrubber = new SeekBar(Host) { Max = 1000 };
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

        row.AddView(elapsedView);
        row.AddView(scrubber, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(remainingView);
        return row;
    }

    private View BuildTransport()
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.Center);
        row.SetPadding(0, PaperTheme.Dp(Host, DesignTokens.Space.X3), 0, 0);
        row.AddView(PaperUi.SecondaryButton(Host, "⏮", () => playback.Controller?.SeekToPrevious()));
        playPause = PaperUi.PrimaryButton(Host, "Play", playback.TogglePlayPause);
        row.AddView(playPause);
        row.AddView(PaperUi.SecondaryButton(Host, "⏭", () => playback.Controller?.SeekToNext()));
        return row;
    }

    private View BuildModes()
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.Center);
        shuffle = PaperUi.QuietButton(Host, "Shuffle: off", ToggleShuffle);
        repeat = PaperUi.QuietButton(Host, "Repeat: off", CycleRepeat);
        row.AddView(shuffle);
        row.AddView(repeat);
        row.AddView(PaperUi.QuietButton(Host, "EQ",
            () => Host.StartActivity(new Android.Content.Intent(Host, typeof(EqualizerActivity)))));
        row.AddView(PaperUi.QuietButton(Host, "Art", PickArt));
        return row;
    }

    private View BuildSpeed()
    {
        var key = playback.SettingsKey;
        var settings = settingsStore.Load();
        var current = key is null ? settings.Default : settings.For(key);

        var row = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        var presets = PlaybackSettings.Presets.ToList();
        var chosen = presets.IndexOf(current.Speed);
        var picker = new AccordionPicker(
            Host,
            "Speed:",
            presets.Select(preset => new PickerOption($"{preset:0.0}×")),
            chosen < 0 ? presets.IndexOf(1.0f) : chosen);
        picker.Chosen += index => Apply(current.WithSpeed(presets[index]));
        row.AddView(picker);

        Button? trim = null;
        trim = PaperUi.QuietButton(Host, TrimLabel(current.SkipSilence), () =>
        {
            Apply(current with { SkipSilence = !current.SkipSilence });
            trim!.Text = TrimLabel(current.SkipSilence);
        });
        row.AddView(trim);
        return row;

        void Apply(PlaybackSettings updated)
        {
            current = updated;
            settingsStore.Save(key is null
                ? settings with { Default = updated }
                : settings.With(key, updated));
            playback.ApplySettings(updated.Speed, updated.SkipSilence);
        }
    }

    private static string TrimLabel(bool on) => on ? "Trim silence: on" : "Trim silence: off";

    private void RenderPanel()
    {
        panel.RemoveAllViews();
        switch (showing)
        {
            case Panel.Words:
                panel.AddView(playback.Kind == MediaKind.Podcast ? BuildNotes() : BuildLyrics());
                break;

            case Panel.Queue:
                var queued = playback.Queued();
                panel.AddView(queued.Count == 0
                    ? PaperUi.EmptyState(Host, "Nothing queued.")
                    : PaperUi.List(
                        Host,
                        [.. queued.Select(item => new PaperListRow(
                            item.IsCurrent ? $"▸  {item.Title}" : item.Title, item.Subtitle))],
                        playback.PlayQueuedAt));
                break;

            default:
                artwork = new ArtworkWindow(Host);
                var size = PaperTheme.Dp(Host, ArtworkSizeDp);
                panel.AddView(artwork, new FrameLayout.LayoutParams(size, size, GravityFlags.Center));
                RenderArtwork();
                break;
        }
    }

    private View BuildNotes()
    {
        notesView = PaperUi.Body(Host, Notes() ?? string.Empty);
        var scroll = new ScrollView(Host);
        scroll.AddView(notesView);
        return scroll;
    }

    /// <summary>
    /// The words, following along when somebody has timed them.
    ///
    /// Built once and then only re-coloured: rebuilding the panel every second to move
    /// the highlight would throw away the scroll position, which on a long song means
    /// the panel snaps back to the first verse once a second.
    /// </summary>
    private View BuildLyrics()
    {
        lyricViews.Clear();
        litLine = -1;

        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        foreach (var line in lyrics.Lines)
        {
            // An empty timed line is a rest — an instrumental — and it keeps its place
            // so the highlight has somewhere to go that is not a lyric.
            var view = PaperUi.Body(Host, line.Text);
            view.SetTextColor(PaperTheme.InkMuted);
            lyricViews.Add(view);
            column.AddView(view);
        }

        lyricsScroll = new ScrollView(Host);
        lyricsScroll.AddView(column);
        LightTheLine();
        return lyricsScroll;
    }

    // Untimed words are left plain: lighting the first line for the whole song would
    // be worse than not following along at all.
    private void LightTheLine()
    {
        if (!lyrics.Timed || lyricViews.Count == 0)
        {
            return;
        }

        var at = lyrics.LineAt(playback.Position);
        if (at == litLine)
        {
            return;
        }

        if (litLine >= 0 && litLine < lyricViews.Count)
        {
            lyricViews[litLine].SetTextColor(PaperTheme.InkMuted);
        }

        litLine = at;
        if (at < 0 || at >= lyricViews.Count)
        {
            return;
        }

        lyricViews[at].SetTextColor(PaperTheme.Ink);

        // Kept a third of the way down rather than at the top, so the next lines are
        // already readable when they arrive.
        lyricsScroll?.SmoothScrollTo(0, Math.Max(0, lyricViews[at].Top - (lyricsScroll.Height / 3)));
    }

    // Best-effort and quiet: no words is the ordinary case, not a failure.
    private async Task LoadLyricsAsync(string mediaId)
    {
        var found = await lyricsSource.FetchAsync(
            playback.CurrentArtist ?? string.Empty,
            playback.CurrentTitle ?? string.Empty,
            playback.CurrentAlbum,
            playback.Duration,
            CancellationToken.None);

        // The track may have moved on while the words were being fetched.
        if (lyricsFor != mediaId)
        {
            return;
        }

        lyrics = found;
        if (!found.IsEmpty)
        {
            Invalidate(); // the switch's third segment has just become live
        }
    }

    public override void OnTick()
    {
        Render();
        if (showing == Panel.Words && playback.Kind != MediaKind.Podcast)
        {
            LightTheLine();
        }
    }

    private void Render()
    {
        if (playback.Controller is not { } controller)
        {
            return;
        }

        // Changing track returns the switch to Cover. It carries no memory on
        // purpose: what you wanted to see about the last track says nothing about
        // what you want to see about this one.
        //
        // The whole pane is rebuilt rather than just the panel, because the switch
        // itself changes with the track — Notes for a podcast, Lyrics for music, and
        // dimmed when there is nothing behind it. Safe here precisely because the
        // track changed: nobody is mid-drag on a scrubber that just reset.
        var mediaId = playback.CurrentMediaId;
        if (mediaId != lastMediaId)
        {
            lastMediaId = mediaId;
            showing = Panel.Cover;

            // New track, new words. Cleared before the fetch so the switch does not
            // offer the last song's lyrics for this one.
            lyrics = Lyrics.None;
            lyricsFor = mediaId;
            if (mediaId is not null && playback.Kind == MediaKind.Music)
            {
                _ = LoadLyricsAsync(mediaId);
            }

            Invalidate();
            return;
        }

        var metadata = controller.MediaMetadata;
        titleView.Text = metadata?.Title?.ToString() ?? string.Empty;
        subtitleView.Text = Join(metadata?.Artist?.ToString(), metadata?.AlbumTitle?.ToString());

        if (showing == Panel.Cover)
        {
            RenderArtwork();
        }

        playPause.Text = controller.IsPlaying ? "Pause" : "Play";
        shuffle.Text = controller.ShuffleModeEnabled ? "Shuffle: on" : "Shuffle: off";
        repeat.Text = RepeatLabel(FromPlayerRepeat(controller.RepeatMode));

        if (!dragging)
        {
            var position = playback.Position;
            var duration = playback.Duration;
            scrubber.Progress = (int)Math.Round(PlaybackTimeline.Fraction(position, duration) * 1000);
            elapsedView.Text = PlaybackTimeline.Elapsed(position);
            remainingView.Text = PlaybackTimeline.Remaining(position, duration);
        }
    }

    // Show notes ride with the media item rather than being looked up here, so this
    // area never needs to know what an episode is.
    private string? Notes() => playback.Controller?.MediaMetadata?.Description?.ToString();

    private void RenderArtwork()
    {
        // Precedence: the owner's override for this album, then the embedded art,
        // then the bookplate the window falls back to.
        var overrideUri = artworkStore.Resolve(AlbumKeyOf(playback.Controller?.MediaMetadata));
        if (overrideUri is not null && TrySetOverrideArt(overrideUri))
        {
            return;
        }

        var data = playback.Controller?.MediaMetadata?.ArtworkData;
        if (data is { Count: > 0 })
        {
            var bytes = data.ToArray();
            artwork.SetArtwork(BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length));
        }
        else
        {
            artwork.SetArtwork((Bitmap?)null);
        }
    }

    private bool TrySetOverrideArt(string uri)
    {
        try
        {
            using var stream = Host.ContentResolver!.OpenInputStream(Android.Net.Uri.Parse(uri));
            var bitmap = BitmapFactory.DecodeStream(stream);
            if (bitmap is not null)
            {
                artwork.SetArtwork(bitmap);
                return true;
            }
        }
        catch (Exception e) when (e is Java.IO.FileNotFoundException or Java.Lang.SecurityException or Java.IO.IOException)
        {
            // A revoked or missing override just falls through to the embedded art.
        }

        return false;
    }

    private static string AlbumKeyOf(MediaMetadata? metadata) => ArtworkOverrides.AlbumKey(
        metadata?.AlbumArtist?.ToString() ?? metadata?.Artist?.ToString() ?? string.Empty,
        metadata?.AlbumTitle?.ToString() ?? string.Empty);

    private void PickArt()
    {
        pendingAlbumKey = AlbumKeyOf(playback.Controller?.MediaMetadata);
        var intent = new Android.Content.Intent(Android.Content.Intent.ActionOpenDocument);
        intent.AddCategory(Android.Content.Intent.CategoryOpenable!);
        intent.SetType("image/*");
        Host.StartActivityForResult(intent, PickArtCode);
    }

    public override void OnActivityResult(
        int requestCode, Android.App.Result resultCode, Android.Content.Intent? data)
    {
        if (requestCode != PickArtCode || resultCode != Android.App.Result.Ok
            || data?.Data is not { } uri || pendingAlbumKey is null)
        {
            return;
        }

        try
        {
            Host.ContentResolver!.TakePersistableUriPermission(uri, Android.Content.ActivityFlags.GrantReadUriPermission);
        }
        catch (Java.Lang.SecurityException)
        {
            // Some providers don't grant persistable access; the override still works for this session.
        }

        artworkStore.Set(pendingAlbumKey, uri.ToString());
        Render();
    }

    private void UpdateClockLabels(double fraction)
    {
        var duration = playback.Duration;
        var position = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * fraction);
        elapsedView.Text = PlaybackTimeline.Elapsed(position);
        remainingView.Text = PlaybackTimeline.Remaining(position, duration);
    }

    private void SeekToFraction(double fraction)
    {
        if (playback.Controller is not { } controller || playback.Duration == TimeSpan.Zero)
        {
            return;
        }

        controller.SeekTo((long)(playback.Duration.TotalMilliseconds * fraction));
    }

    private void ToggleShuffle()
    {
        if (playback.Controller is { } controller)
        {
            controller.ShuffleModeEnabled = !controller.ShuffleModeEnabled;
            Render();
        }
    }

    private void CycleRepeat()
    {
        if (playback.Controller is { } controller)
        {
            var next = FromPlayerRepeat(controller.RepeatMode) switch
            {
                RepeatMode.Off => RepeatMode.All,
                RepeatMode.All => RepeatMode.One,
                _ => RepeatMode.Off,
            };
            controller.RepeatMode = ToPlayerRepeat(next);
            Render();
        }
    }

    private static string Join(string? artist, string? album) =>
        string.Join(" · ", new[] { artist, album }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string RepeatLabel(RepeatMode mode) => mode switch
    {
        RepeatMode.One => "Repeat: one",
        RepeatMode.All => "Repeat: all",
        _ => "Repeat: off",
    };

    private const int RepeatOff = 0;
    private const int RepeatOne = 1;
    private const int RepeatAll = 2;

    private static int ToPlayerRepeat(RepeatMode mode) => mode switch
    {
        RepeatMode.One => RepeatOne,
        RepeatMode.All => RepeatAll,
        _ => RepeatOff,
    };

    private static RepeatMode FromPlayerRepeat(int repeat) => repeat switch
    {
        RepeatOne => RepeatMode.One,
        RepeatAll => RepeatMode.All,
        _ => RepeatMode.Off,
    };
}
