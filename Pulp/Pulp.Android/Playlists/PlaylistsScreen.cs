using Android.App;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Actions;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using Pulp.Domain.Voice;
using Pulp.Library;
using Pulp.Title;

namespace Pulp.Playlists;

/// <summary>
/// Everything behind the Playlists tab: the list of the viewer's playlists (their own and the shared
/// ones, D97), a playlist's page (mockup 04, D225) and the made-playlist flow (D104, D272). The area
/// owns the frame and Back; this owns the state, the reads and the writes, and builds a page from
/// that state on every render.
///
/// <para>A playlist's page is drawn as approved: its name as the Serif header, the status line, "Hide
/// watched" (D101), then the rows in hand order — struck when watched and kept, dimmed when a caught-up
/// show waits on a season (D224) or a film is not out (D32). No bottom bar (D223). The grip is a Move
/// target rather than a drag (D103, by ruling); removal is undo-first on the dog-ear (§13). The
/// voiced line per playlist (D38) stays hidden: no composer is wired in the head yet, and that seat
/// hides when it has no voice.</para>
///
/// <para>Every read and write is off the UI thread; loading is a status sentence (§6); with nobody on
/// the roster the page is its empty state, never invented rows.</para>
/// </summary>
internal sealed class PlaylistsScreen
{
    private enum Showing
    {
        List,
        Playlist,
        Make,
    }

    private const string Loading = "Reading your playlists…";
    private const string GripGlyph = "⋮⋮";

    private readonly Activity host;
    private readonly Func<FrameLayout> overlay;
    private readonly Action invalidate;
    private readonly PulpServices services;
    private readonly LibraryWrites writes;
    private readonly PlaylistMaker maker;
    private readonly Dictionary<TitleId, CatalogueTitle> titles = [];

    private Showing showing = Showing.List;
    private PlaylistId? open;
    private Data? data;
    private string? status = Loading;
    private bool hideWatched;
    private bool naming;
    private string nameDraft = string.Empty;
    private string promptDraft = string.Empty;
    private bool shareNew;
    private bool making;
    private string? makeStatus;
    private int loadGeneration;
    private CancellationTokenSource paint = new();

    public PlaylistsScreen(Activity host, Func<FrameLayout> overlay, Action invalidate)
    {
        this.host = host;
        this.overlay = overlay;
        this.invalidate = invalidate;
        services = PulpServices.For(host);
        writes = new LibraryWrites(services, () => host.RunOnUiThread(Reload));
        maker = new PlaylistMaker(services);
    }

    private sealed record Data(ThisPhoneAnswer Me, IReadOnlyList<HouseholdPlaylist> Playlists, HouseholdView View);

    /// <summary>The header: a playlist's own name on its page (mockup 04), else the area's or the flow's.</summary>
    public string? Title => showing switch
    {
        Showing.Playlist => Current?.List.Name,
        Showing.Make => "Make a playlist",
        _ => null,
    };

    private HouseholdPlaylist? Current =>
        open is { } id ? data?.Playlists.FirstOrDefault(playlist => playlist.Id == id) : null;

    public bool GoBack()
    {
        if (showing == Showing.List)
        {
            return false;
        }

        showing = Showing.List;
        open = null;
        makeStatus = null;
        invalidate();
        return true;
    }

    public void Reload()
    {
        var generation = ++loadGeneration;
        _ = Task.Run(async () =>
        {
            try
            {
                var view = await services.HouseholdAsync(CancellationToken.None);
                var me = services.ThisPhoneOf(view);
                var playlists = me.Viewer is { } viewer ? view.PlaylistsFor(viewer) : [];
                host.RunOnUiThread(() =>
                {
                    if (generation != loadGeneration)
                    {
                        return;
                    }

                    data = new Data(me, playlists, view);
                    status = null;
                    invalidate();
                    LookUpOpenTitles();
                });
            }
            catch (Exception exception)
            {
                services.Diagnostics.Warn("playlists", "The playlists could not be read.", exception);
                host.RunOnUiThread(() =>
                {
                    status = "Your playlists couldn't be read just now.";
                    invalidate();
                });
            }
        });
    }

    /// <summary>The open playlist's titles: the cached copy paints at once, the rest arrive and repaint.</summary>
    private void LookUpOpenTitles()
    {
        if (Current is not { } playlist)
        {
            return;
        }

        var ids = playlist.List.Entries
            .Where(entry => entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show)
            .Select(entry => new TitleId(entry.Key))
            .Where(id => !titles.ContainsKey(id))
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var found = new Dictionary<TitleId, CatalogueTitle>();
            foreach (var id in ids)
            {
                try
                {
                    var title = services.CachedCopy(id) ?? await services.TitleAsync(id, tracked: true, opened: false, CancellationToken.None);
                    if (title is not null)
                    {
                        found[id] = title;
                    }
                }
                catch (Exception exception)
                {
                    services.Diagnostics.Warn("playlists", $"Title {id} could not be looked up for its playlist row.", exception);
                }
            }

            host.RunOnUiThread(() =>
            {
                foreach (var (id, title) in found)
                {
                    titles[id] = title;
                }

                invalidate();
            });
        });
    }

    public View Build()
    {
        // A new paint: posters still arriving for the old one are no longer wanted.
        paint.Cancel();
        paint = new CancellationTokenSource();

        var column = new LinearLayout(host) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(host) { FillViewport = true };
        scroll.AddView(column, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        if (status is not null || data is null)
        {
            column.AddView(PaperUi.Secondary(host, status ?? Loading));
            return scroll;
        }

        switch (data.Me.Status)
        {
            case ThisPhoneStatus.NoRoster:
                column.AddView(Empty(VoiceSeat.EmptyPlaylists, primary: null));
                return scroll;
            case ThisPhoneStatus.Unmatched:
                column.AddView(PaperUi.Secondary(host, ThisPhone.UnmatchedSentence));
                return scroll;
            case ThisPhoneStatus.Known:
                break;
            default:
                throw new InvalidOperationException($"A viewer status the Playlists page does not know: {data.Me.Status}.");
        }

        var viewer = data.Me.Viewer!.Value;
        switch (showing)
        {
            case Showing.Playlist when Current is { } playlist:
                BuildPlaylist(column, playlist, viewer);
                break;
            case Showing.Make:
                BuildMake(column, viewer);
                break;
            default:
                showing = Showing.List;
                BuildList(column, viewer);
                break;
        }

        return scroll;
    }

    // ---- the list of playlists ----

    private void BuildList(LinearLayout column, ViewerId viewer)
    {
        var playlists = data!.Playlists;
        if (playlists.Count == 0 && !naming)
        {
            column.AddView(Empty(VoiceSeat.EmptyPlaylists, primary: OpenMake));
            column.AddView(Centred(PaperUi.SecondaryButton(host, "New playlist…", StartNaming)));
            return;
        }

        foreach (var playlist in playlists)
        {
            var lines = LibraryRows.Lines(host);
            lines.AddView(LibraryRows.TitleLine(host, playlist.List.Name));
            lines.AddView(PaperUi.Secondary(host, PlaylistPageSummary.Status(playlist, viewer)));
            var pad = PaperTheme.Dp(host, DesignTokens.Space.X3);
            lines.SetPadding(0, pad, 0, pad);
            lines.SetMinimumHeight(PaperTheme.Dp(host, DesignTokens.Shape.ListRowMinHeightDp));
            lines.Clickable = true;
            var id = playlist.Id;
            lines.Click += (_, _) => Open(id);
            column.AddView(lines);
            column.AddView(LibraryRows.Hairline(host));
        }

        if (naming)
        {
            BuildNaming(column, viewer);
            return;
        }

        var actions = new LinearLayout(host) { Orientation = Orientation.Horizontal };
        actions.SetPadding(0, PaperTheme.Dp(host, DesignTokens.Space.X4), 0, 0);
        actions.AddView(PaperUi.SecondaryButton(host, "New playlist…", StartNaming));
        var make = PaperUi.PrimaryButton(host, EmptyState.PrimaryLabel(VoiceSeat.EmptyPlaylists)!, OpenMake);
        actions.AddView(make, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            MarginStart = PaperTheme.Dp(host, DesignTokens.Space.X2),
        });
        column.AddView(actions);
    }

    private void StartNaming()
    {
        naming = true;
        invalidate();
    }

    /// <summary>"New playlist…" typed in place (as the picker does it): a name, whose it is, and Make it.</summary>
    private void BuildNaming(LinearLayout column, ViewerId viewer)
    {
        var form = new LinearLayout(host) { Orientation = Orientation.Vertical };
        form.SetPadding(0, PaperTheme.Dp(host, DesignTokens.Space.X4), 0, 0);

        var field = PaperUi.Field(host, "Playlist name");
        field.Text = nameDraft;
        field.TextChanged += (_, _) => nameDraft = field.Text ?? string.Empty;
        form.AddView(field);
        form.AddView(Whose(), Spaced());

        var buttons = new LinearLayout(host) { Orientation = Orientation.Horizontal };
        buttons.AddView(PaperUi.PrimaryButton(host, "Make it", () => CreateNamed(viewer)));
        buttons.AddView(PaperUi.QuietButton(host, "Cancel", () =>
        {
            naming = false;
            nameDraft = string.Empty;
            invalidate();
        }));
        form.AddView(buttons, Spaced());
        column.AddView(form);
        field.RequestFocus();
    }

    private LinearLayout Whose() =>
        PaperUi.Segmented(host, ["Just me", "Shared"], index => shareNew = index == 1, shareNew ? 1 : 0);

    private void CreateNamed(ViewerId viewer)
    {
        var name = nameDraft.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var playlist = new HouseholdPlaylist(HouseholdEdits.NewPlaylistId(), viewer, shareNew, Playlist.Named(name));
        naming = false;
        nameDraft = string.Empty;
        open = playlist.Id;
        showing = Showing.Playlist;
        writes.Enqueue($"make the playlist {name}", token => services.CreatePlaylistAsync(playlist, token));
    }

    private void Open(PlaylistId id)
    {
        open = id;
        showing = Showing.Playlist;
        invalidate();
        LookUpOpenTitles();
    }

    private void OpenMake()
    {
        showing = Showing.Make;
        makeStatus = null;
        invalidate();
    }

    // ---- a playlist (mockup 04) ----

    private void BuildPlaylist(LinearLayout column, HouseholdPlaylist playlist, ViewerId viewer)
    {
        column.AddView(PaperUi.Secondary(host, PlaylistPageSummary.Status(playlist, viewer)));

        if (playlist.List.Entries.Count == 0)
        {
            column.AddView(Empty(VoiceSeat.EmptyPlaylist, primary: null));
            return;
        }

        column.AddView(HideWatchedChip(), Spaced());

        var view = data!.View;
        var sittings = view.SittingsOf(viewer);
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var rows = Pulp.Domain.Library.Playlists.Rows(
            playlist.List,
            entry => PlaylistPageStanding.For(entry, sittings.Where(s => StringComparer.OrdinalIgnoreCase.Equals(s.Title.Value, entry.Key)).ToList(), TitleOf(entry), now, TimeZoneInfo.Local),
            today,
            hideWatched);

        foreach (var row in rows)
        {
            column.AddView(BuildRow(playlist, row, view, today));
            column.AddView(LibraryRows.Hairline(host));
        }
    }

    private CatalogueTitle? TitleOf(PlaylistEntry entry) =>
        entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show ? titles.GetValueOrDefault(new TitleId(entry.Key)) : null;

    /// <summary>"Hide watched" (D101): a chip on its own line, washed in the identity while it is on.</summary>
    private View HideWatchedChip()
    {
        var chip = PaperUi.Label(host, "Hide watched");
        chip.SetTextColor(hideWatched ? PaperTheme.Ink : PaperTheme.InkSoft);
        chip.Background = PaperUi.Outline(
            host,
            hideWatched ? PaperTheme.Wash(Android.Graphics.Color.ParseColor(DesignTokens.Identity.Pulp.Mark)) : PaperTheme.PaperBright,
            PaperTheme.Hairline,
            DesignTokens.Shape.RadiusInput);
        var padX = PaperTheme.Dp(host, DesignTokens.Space.X3);
        var padY = PaperTheme.Dp(host, DesignTokens.Space.X1);
        chip.SetPadding(padX, padY, padX, padY);
        chip.Clickable = true;
        chip.Click += (_, _) =>
        {
            hideWatched = !hideWatched;
            invalidate();
        };

        var line = new LinearLayout(host) { Orientation = Orientation.Horizontal };
        line.SetMinimumHeight(PaperTheme.Dp(host, DesignTokens.Shape.TouchTargetDp));
        line.SetGravity(GravityFlags.CenterVertical);
        line.AddView(chip);
        return line;
    }

    private View BuildRow(HouseholdPlaylist playlist, PlaylistRow row, HouseholdView view, DateOnly today)
    {
        var title = TitleOf(row.Entry);
        var text = PlaylistPageRowText.For(row, title, view.Services, view.HomeOverrides, today);
        var name = text.Name ?? (title is null && row.Entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show
            ? "Looking it up…"
            : row.Entry.Key);

        var lines = LibraryRows.Lines(host);
        var titleLine = LibraryRows.TitleLine(host, name);
        if (row.Struck)
        {
            LibraryRows.Strike(titleLine);
        }

        lines.AddView(titleLine);
        lines.AddView(LibraryRows.KindLine(host, text.KindLine));
        if (text.WhereLine is { } where)
        {
            lines.AddView(LibraryRows.WhereLine(host, where, text.WhereEmphasis));
        }

        var grip = PaperUi.Label(host, GripGlyph);
        grip.SetTextColor(PaperTheme.Hairline);
        grip.Gravity = GravityFlags.Center;
        grip.SetMinWidth(PaperTheme.Dp(host, DesignTokens.Shape.TouchTargetDp / 2));
        grip.SetPadding(0, 0, PaperTheme.Dp(host, DesignTokens.Space.X2), 0);
        grip.Clickable = true;
        grip.Click += (_, _) => ShowMoves(playlist, row.Entry, name);

        var poster = LibraryRows.Poster(host, title?.PosterUrl, LibraryRows.PlaylistPosterWidthDp, paint.Token);
        if (row.Struck)
        {
            poster.Alpha = LibraryRows.StruckPosterAlpha;
        }

        var rowView = LibraryRows.Row(host, grip, poster, LibraryRows.PlaylistPosterWidthDp, lines, DesignTokens.Space.X3);
        if (row.Dim != RowDim.None)
        {
            rowView.Alpha = DesignTokens.Alpha.DisabledOpacity;
        }

        if (row.Entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show)
        {
            rowView.Clickable = true;
            rowView.Click += (_, _) => host.StartActivity(TitleActivity.For(host, new TitleId(row.Entry.Key)));
        }

        rowView.LongClick += (_, args) =>
        {
            args.Handled = true;
            ShowCard(playlist, row.Entry, name, text.KindLine);
        };
        return rowView;
    }

    /// <summary>The grip's Move card (D103 by Move): only the moves that change something.</summary>
    private void ShowMoves(HouseholdPlaylist playlist, PlaylistEntry entry, string name)
    {
        var moves = PlaylistPageMoves.Offered(playlist.List, entry);
        if (moves.Count == 0)
        {
            return;
        }

        var actions = moves
            .Select(move => new CardAction(PlaylistPageMoves.Label(move), () =>
            {
                // Applied to the list as it stands when each lands — on screen and in the household — so
                // a move never writes back a copy that predates an edit made since.
                ShowEdit(playlist.Id, list => PlaylistEdits.Move(list, entry, move));
                writes.Enqueue($"move {name} on {playlist.List.Name}", token => services.EditPlaylistAsync(playlist.Id, list => PlaylistEdits.Move(list, entry, move), token));
            }))
            .ToArray();
        ActionCard.Show(host, overlay(), name, playlist.List.Name, actions);
    }

    /// <summary>The playlist row's card (mockup 10): Play · Mark watched · Move to another playlist · Remove from this playlist.</summary>
    private void ShowCard(HouseholdPlaylist playlist, PlaylistEntry entry, string name, string subtitle)
    {
        var viewer = data!.Me.Viewer!.Value;
        var marks = PlaylistPageMarks.Watch(entry, TitleOf(entry), DateTimeOffset.UtcNow)
            .Select(mark => new WatchedSitting(viewer, mark))
            .ToList();
        // The removal and its undo, once for the page and once for the household: each is applied to
        // its own list as it stands, so the undo puts the entry back where it stood and keeps a move
        // made while the dog-ear was up, rather than writing back the list from before.
        var shown = new EntryRemoval(entry);
        var removal = new EntryRemoval(entry);
        var isTitle = entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show;

        var handlers = new Dictionary<CardActionKind, CardHandler>
        {
            // Play lives on the title page (D151); a row plays from there (D223).
            [CardActionKind.Play] = new(() =>
            {
                if (isTitle)
                {
                    host.StartActivity(TitleActivity.For(host, new TitleId(entry.Key)));
                }
            }),
            [CardActionKind.MarkWatched] = new(
                () => writes.Enqueue($"mark {name} watched", async token =>
                {
                    foreach (var mark in marks)
                    {
                        await services.PutAsync(HouseholdMaps.Watched, mark, token);
                    }
                }),
                () => writes.Enqueue($"undo marking {name} watched", async token =>
                {
                    foreach (var mark in marks)
                    {
                        await services.RemoveAsync(HouseholdMaps.Watched, mark, token);
                    }
                })),
            [CardActionKind.MoveToPlaylist] = new(() => ShowMoveTo(playlist, entry, name)),
            [CardActionKind.RemoveFromPlaylist] = new(
                () =>
                {
                    ShowEdit(playlist.Id, shown.Apply);
                    writes.Enqueue($"remove {name} from {playlist.List.Name}", token => services.EditPlaylistAsync(playlist.Id, removal.Apply, token));
                },
                () =>
                {
                    ShowEdit(playlist.Id, shown.Undo);
                    writes.Enqueue($"put {name} back on {playlist.List.Name}", token => services.EditPlaylistAsync(playlist.Id, removal.Undo, token));
                }),
        };

        PulpActionCard.Show(host, overlay(), new CardItem(name, CardPlace.PlaylistRow) { Subtitle = subtitle }, handlers);
    }

    /// <summary>"Move to another playlist": the other playlists the viewer sees; the move is undone from the corner.</summary>
    private void ShowMoveTo(HouseholdPlaylist source, PlaylistEntry entry, string name)
    {
        var others = data!.Playlists.Where(playlist => playlist.Id != source.Id).ToList();
        if (others.Count == 0)
        {
            ActionCard.Show(host, overlay(), "Move to another playlist", "There's no other playlist yet.");
            return;
        }

        var actions = others.Select(target => new CardAction(target.List.Name, () =>
        {
            // Inverse edits, not snapshots: the undo takes the entry off the target only if this move
            // put it there, and puts it back on the source where it stood, keeping anything since.
            var shownLeft = new EntryRemoval(entry);
            var shownArrived = new EntryAddition(entry);
            var left = new EntryRemoval(entry);
            var arrived = new EntryAddition(entry);
            ShowEdit(target.Id, shownArrived.Apply);
            ShowEdit(source.Id, shownLeft.Apply);
            writes.Enqueue($"move {name} to {target.List.Name}", async token =>
            {
                await services.EditPlaylistAsync(target.Id, arrived.Apply, token);
                await services.EditPlaylistAsync(source.Id, left.Apply, token);
            });
            DogEar.Show(host, overlay(), $"Undo moving {name} to {target.List.Name}.", () =>
            {
                ShowEdit(target.Id, shownArrived.Undo);
                ShowEdit(source.Id, shownLeft.Undo);
                writes.Enqueue($"undo moving {name} to {target.List.Name}", async token =>
                {
                    await services.EditPlaylistAsync(target.Id, arrived.Undo, token);
                    await services.EditPlaylistAsync(source.Id, left.Undo, token);
                });
            });
        })).ToArray();
        ActionCard.Show(host, overlay(), "Move to another playlist", name, actions);
    }

    /// <summary>
    /// Shows an edit at once — the change itself is the success (§5) — applied to the playlist as the
    /// page shows it now, while the write follows and the reload after it shows what was kept.
    /// </summary>
    private void ShowEdit(PlaylistId id, Func<Playlist, Playlist> change)
    {
        if (data is null)
        {
            return;
        }

        data = data with { Playlists = data.Playlists.Select(playlist => playlist.Id == id ? playlist with { List = change(playlist.List) } : playlist).ToList() };
        invalidate();
    }

    // ---- making one (D104, D272: no mockup; built from the Paper anatomy) ----

    private void BuildMake(LinearLayout column, ViewerId viewer)
    {
        column.AddView(PaperUi.BodyMuted(host, "A few words for the evening — a rainy Sunday, a long drive — or start from one of your playlists."));

        var field = PaperUi.Field(host, "What's it for?");
        field.Text = promptDraft;
        field.Enabled = !making;
        field.TextChanged += (_, _) => promptDraft = field.Text ?? string.Empty;
        column.AddView(field, Spaced());
        column.AddView(Whose(), Spaced());

        if (making)
        {
            column.AddView(PaperUi.Secondary(host, PlaylistMaker.Asking), Spaced());
            return;
        }

        column.AddView(Centred(PaperUi.PrimaryButton(host, "Make it", () =>
        {
            var words = promptDraft.Trim();
            if (words.Length > 0)
            {
                Make(PlaylistSeed.FromPrompt(words), viewer);
            }
        })), Spaced());

        if (makeStatus is not null)
        {
            column.AddView(PaperUi.Secondary(host, makeStatus), Spaced());
        }

        var seeds = data!.Playlists.Where(playlist => playlist.List.Entries.Any(entry => entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show)).ToList();
        if (seeds.Count == 0)
        {
            return;
        }

        var heading = PaperUi.Heading(host, "More like a playlist");
        heading.SetPadding(0, PaperTheme.Dp(host, DesignTokens.Space.X6), 0, PaperTheme.Dp(host, DesignTokens.Space.X1));
        column.AddView(heading);
        foreach (var seed in seeds)
        {
            var row = LibraryRows.TitleLine(host, seed.List.Name);
            var pad = PaperTheme.Dp(host, DesignTokens.Space.X3);
            row.SetPadding(0, pad, 0, pad);
            row.SetMinimumHeight(PaperTheme.Dp(host, DesignTokens.Shape.TouchTargetDp));
            row.Clickable = true;
            row.Click += (_, _) => MakeLike(seed, viewer);
            column.AddView(row);
            column.AddView(LibraryRows.Hairline(host));
        }
    }

    private void MakeLike(HouseholdPlaylist seed, ViewerId viewer)
    {
        making = true;
        invalidate();
        _ = Task.Run(() =>
        {
            try
            {
                var made = PlaylistSeed.FromPlaylist(seed.List, entry => entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show ? services.CachedCopy(new TitleId(entry.Key)) : null);
                host.RunOnUiThread(() => Make(made, viewer));
            }
            catch (ArgumentException exception)
            {
                services.Diagnostics.Warn("playlists", $"'{seed.List.Name}' could not seed a playlist.", exception);
                host.RunOnUiThread(() =>
                {
                    making = false;
                    makeStatus = "Open that playlist once so its titles are known, then try again.";
                    invalidate();
                });
            }
        });
    }

    private void Make(PlaylistSeed seed, ViewerId viewer)
    {
        making = true;
        makeStatus = null;
        invalidate();
        var shared = shareNew;
        _ = Task.Run(async () =>
        {
            MadeOutcome outcome;
            try
            {
                outcome = await maker.MakeAsync(seed, viewer, shared, CancellationToken.None);
            }
            catch (Exception exception)
            {
                services.Diagnostics.Warn("playlists", "Making a playlist failed.", exception);
                outcome = new MadeOutcome(null, PlaylistMaker.NoIdea);
            }

            host.RunOnUiThread(() =>
            {
                making = false;
                if (outcome.Saved is { } saved)
                {
                    promptDraft = string.Empty;
                    open = saved.Id;
                    showing = Showing.Playlist;
                    Reload();
                }
                else
                {
                    makeStatus = outcome.Status;
                }

                invalidate();
            });
        });
    }

    // ---- small parts ----

    private EmptyState Empty(VoiceSeat seat, Action? primary)
    {
        var empty = new EmptyState(host, seat, primary);

        // No composer is wired in the head yet, so the seat shows its neutral line (D264).
        empty.Present(VoiceLineAction.ShowNeutral, null);
        empty.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        return empty;
    }

    private View Centred(View view)
    {
        var line = new LinearLayout(host) { Orientation = Orientation.Horizontal };
        line.SetGravity(GravityFlags.CenterHorizontal);
        line.AddView(view);
        return line;
    }

    private LinearLayout.LayoutParams Spaced() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = PaperTheme.Dp(host, DesignTokens.Space.X3) };
}
