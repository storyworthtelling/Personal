using Stopwatch = System.Diagnostics.Stopwatch;
using Android.App;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Post;
using Storyworthtelling.Time;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Email;
using Codex.Ui;
using Storyworthtelling.Voice;

namespace Post;

/// <summary>
/// Filed: correspondence that has been dealt with — deleted at the source, kept here as a
/// ghost. The other half of the to-do-list frame the Mailbox is the front of.
///
/// <para><b>Searched, not listed (the owner's ruling).</b> Listing every ghost read the whole
/// archive, every body, on every visit. Now the page opens on one box: the owner says what they
/// are looking for in their own words, and Claude picks up to five from a short catalogue of
/// what is filed (<see cref="FiledCatalogue"/> — who, when, the subject and the first words,
/// pulled out by SQLite without loading a body). <i>Show more</i> asks again for others; the
/// catalogue is a cached prompt, so a second ask is cheap. With no key, or when Claude cannot be
/// reached, the query's own words are matched instead and the status line says so.</para>
///
/// <para><b>Judging whether it works.</b> Every ask logs a round line and every search ends with a
/// verdict (<see cref="FiledSearchTrail"/>): HIT and where the result sat, REPHRASED, EMPTY or
/// LEFT, with Claude's picks set against the word match. Nothing searched for is logged.</para>
///
/// A ghost filed a moment ago is not offered while its undo window is open — the settling rule
/// from when this was a list, now applied by <see cref="Codex.Infrastructure.Storage.FilingStore.LoadEntries"/>.
/// </summary>
public sealed class FiledArea(Activity host) : PostArea(host)
{
    private enum Stage
    {
        Asking,
        Searching,
        Results,
    }

    private Stage stage = Stage.Asking;
    private string draft = string.Empty;
    private string? status;

    private FiledSearchSession? session;
    private readonly List<Ghost> results = [];
    private IReadOnlyList<FiledEntry>? entries;
    private FiledCatalogue? catalogue;

    // What the owner did with a result, for the search's verdict. Put back outranks opened.
    private string? used;
    private FiledSearchEnd usedHow = FiledSearchEnd.Opened;
    private bool verdictLogged;

    private Microsoft.Extensions.Options.IOptions<VoiceOptions>? voiceOptions;

    public override BinderArea Tab =>
        new("Filed", Resource.Drawable.area_filed, PaperTheme.CautionMark, PaperTheme.CautionFill);

    public override void Pause() => EndSearch(FiledSearchEnd.Left);

    public override bool GoBack()
    {
        if (stage != Stage.Results)
        {
            return false;
        }
        NewSearch();
        return true;
    }

    public override View BuildView()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        var field = PaperUi.Field(Host, "Describe what you're looking for");
        field.Text = draft;
        field.SetSelection(field.Text?.Length ?? 0);
        field.SetSingleLine(true);
        field.ImeOptions = ImeAction.Search;
        field.TextChanged += (_, _) => draft = field.Text ?? string.Empty;
        field.EditorAction += (_, e) =>
        {
            e.Handled = true;
            Find();
        };
        field.Enabled = stage != Stage.Searching;
        column.AddView(field, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        if (status is not null)
        {
            var line = PaperUi.BodyMuted(Host, status);
            var pad = PaperTheme.Dp(Host, DesignTokens.Space.X3);
            line.SetPadding(0, pad, 0, pad);
            column.AddView(line);
        }

        if (results.Count > 0)
        {
            var list = new ListView(Host);
            PaperUi.StylePaperList(Host, list);
            list.Adapter = new PaperListAdapter(Host, results.Select(Row).ToList());
            list.ItemLongClick += (_, e) =>
            {
                if (e.Position >= 0 && e.Position < results.Count)
                {
                    ShowGhostActions(results[e.Position]);
                }
            };
            column.AddView(list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        }
        return column;
    }

    public override View? BuildBottomBar() => stage switch
    {
        Stage.Asking => PaperUi.BottomBar(Host, PaperUi.PrimaryButton(Host, "Find", Find)),
        Stage.Results when session is { Exhausted: false } => PaperUi.BottomBar(Host,
            PaperUi.PrimaryButton(Host, "Show more", ShowMore),
            PaperUi.SecondaryButton(Host, "New search", NewSearch)),
        Stage.Results => PaperUi.BottomBar(Host, PaperUi.SecondaryButton(Host, "New search", NewSearch)),
        _ => null,
    };

    private void Find()
    {
        var query = draft.Trim();
        if (query.Length == 0 || stage == Stage.Searching)
        {
            return;
        }
        EndSearch(FiledSearchEnd.NewSearch);
        session = FiledSearchSession.Start(query);
        results.Clear();
        entries = null;
        catalogue = null;
        used = null;
        verdictLogged = false;
        Search("Looking through what is filed…");
    }

    private void ShowMore()
    {
        if (session is null || stage == Stage.Searching)
        {
            return;
        }
        Search("Looking for more…");
    }

    private void NewSearch()
    {
        EndSearch(FiledSearchEnd.NewSearch);
        session = null;
        results.Clear();
        entries = null;
        catalogue = null;
        draft = string.Empty;
        status = null;
        stage = Stage.Asking;
        Invalidate();
    }

    // Logs the search's verdict once. A later use of the same results is not a second verdict.
    private void EndSearch(FiledSearchEnd how)
    {
        if (session is null || verdictLogged)
        {
            return;
        }
        var end = used is null ? how : usedHow;
        if (FiledSearchTrail.Ended(session, used, end) is { } line)
        {
            PostDiagnostics.LogInfo("FiledSearch", line);
        }
        verdictLogged = true;
    }

    private void Search(string sentence)
    {
        var current = session!;
        stage = Stage.Searching;
        status = sentence;
        Invalidate();

        var action = TapWatch.Action(PostKeyActions.SearchFiled, TimeSpan.FromSeconds(90));
        Task.Run(async () =>
        {
            try
            {
                var outcome = await AskAsync(current);
                action.Succeeded();
                Host.RunOnUiThread(() => Show(current, outcome));
            }
            catch (Exception e)
            {
                PostDiagnostics.LogWarning("FiledSearch", "Filed could not be searched", e);
                action.Failed($"{e.GetType().Name}: {e.Message}");
                Host.RunOnUiThread(() =>
                {
                    if (!ReferenceEquals(session, current))
                    {
                        return;
                    }
                    stage = results.Count > 0 ? Stage.Results : Stage.Asking;
                    status = "Filed could not be searched. The diagnostics log has the details.";
                    Invalidate();
                });
            }
        });
    }

    private sealed record Outcome(FiledSearchSession Session, IReadOnlyList<Ghost> Found, string? WhyWords);

    private async Task<Outcome> AskAsync(FiledSearchSession current)
    {
        var clock = Stopwatch.StartNew();
        if (entries is null || catalogue is null)
        {
            entries = LoadEntries();
            catalogue = FiledCatalogue.Build(entries, current.Query);
        }
        var everything = entries;
        var sent = catalogue;

        var client = await ClientAsync();
        string? whyWords = null;
        FiledAnswer? answer = null;
        FiledSearchReply? reply = null;
        if (client is null || !client.HasKey)
        {
            whyWords = "no Claude key";
        }
        else
        {
            try
            {
                var shownLines = current.Shown.Select(sent.LineOf).OfType<int>().ToList();
                reply = await client.AskAsync(sent, current.Query, shownLines, DateOnly.FromDateTime(DateTime.Now));
                answer = FiledAnswer.Parse(reply.Text, sent, current.Shown);
                if (answer.Garbled)
                {
                    whyWords = $"reply unreadable, stop {reply.StopReason ?? "unknown"}";
                    PostDiagnostics.LogWarning("FiledSearch",
                        $"Claude's Filed search reply could not be read (stop {reply.StopReason}, request {reply.RequestId})");
                }
            }
            catch (Exception e)
            {
                PostDiagnostics.LogWarning("FiledSearch", "Claude could not be asked; matching words instead", e);
                whyWords = $"Claude failed: {e.GetType().Name}";
            }
        }

        IReadOnlyList<string> found;
        FiledRound round;
        var queryWords = current.Query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (whyWords is null)
        {
            found = answer!.Picks.Select(pick => pick.SourceId).ToList();
            var wordsTop = FiledWords.Find(everything, current.Query, current.Shown, take: 20);
            var skipped = FiledWords.Complete(everything, current.Query)
                .Count(id => !found.Contains(id) && !current.Shown.Contains(id));
            round = new FiledRound(
                current.Round + 1, ByClaude: true, WhyWords: null, queryWords, sent.Sent, sent.Total,
                found.Count, answer.Picks.Count(pick => pick.Strong), answer.Rejected,
                found.Count(wordsTop.Contains), skipped, clock.ElapsedMilliseconds,
                reply!.Usage, reply.StopReason, reply.RequestId);
        }
        else
        {
            found = FiledWords.Find(everything, current.Query, current.Shown, take: FiledAnswer.MaxPicks);
            round = new FiledRound(
                current.Round + 1, ByClaude: false, whyWords, queryWords, sent.Sent, sent.Total,
                found.Count, 0, 0, 0, 0, clock.ElapsedMilliseconds, null, null, null);
        }
        PostDiagnostics.LogInfo("FiledSearch", FiledSearchTrail.Round(round));

        using var database = TimelineDatabase.Open(Host);
        return new Outcome(current.Record(found), database.Filing.LoadGhosts(found), whyWords);
    }

    private void Show(FiledSearchSession asked, Outcome outcome)
    {
        if (!ReferenceEquals(session, asked))
        {
            return; // a newer search started while this one was out
        }
        session = outcome.Session;
        results.AddRange(outcome.Found.Where(ghost => results.All(shown => shown.Item.SourceId != ghost.Item.SourceId)));
        stage = Stage.Results;
        status = outcome.Found.Count == 0
            ? (results.Count == 0 ? "Nothing filed fits that." : "Nothing else fits.")
            : outcome.WhyWords switch
            {
                null => null,
                "no Claude key" => "Matched by words only: Post has no Claude key.",
                _ => "Matched by words only: Claude could not be reached.",
            };
        Invalidate();
    }

    // Read on the search's own thread: a few short fields per ghost, never a body in full.
    private IReadOnlyList<FiledEntry> LoadEntries()
    {
        var window = TimeSpan.FromMilliseconds(DesignTokens.Motion.UndoWindowMs);
        using var database = TimelineDatabase.Open(Host);
        var raw = database.Filing.LoadEntries(settledBy: DateTimeOffset.UtcNow - window);
        var names = ContactNames();
        return raw.Select(entry => names.TryGetValue(entry.ContactKey, out var name) ? entry with { Who = name } : entry)
            .ToList();
    }

    // One pass over the address book rather than a lookup per ghost. Keys are normalized the way
    // the timeline normalizes them, so a ghost's contact key finds its person.
    private Dictionary<string, string> ContactNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var person in ContactPicker.People(Host))
            {
                foreach (var phone in person.Phones)
                {
                    names.TryAdd(ContactKeyNormalizer.NormalizePhone(phone), person.Name);
                }
                foreach (var email in person.Emails)
                {
                    names.TryAdd(ContactKeyNormalizer.NormalizeEmail(email), person.Name);
                }
            }
        }
        catch (Exception e)
        {
            // Without contacts the search still runs, on addresses and numbers.
            PostDiagnostics.LogWarning("FiledSearch", "Could not read contact names for the search", e);
        }
        return names;
    }

    // The key lives in the Drive vault, so it is read once and kept; a search that found none
    // reads again next time, in case the vault has been unlocked since.
    private async Task<FiledSearchClient?> ClientAsync()
    {
        try
        {
            if (voiceOptions is null || string.IsNullOrWhiteSpace(voiceOptions.Value.ApiKey))
            {
                var options = Codex.Ui.Voice.VoiceOptionsAccess.ForHead(MailAccess.Account().GetAccessToken, "Post");
                await options.RefreshAsync(CancellationToken.None);
                voiceOptions = options;
            }
            return new FiledSearchClient(MailAccess.Shared, voiceOptions);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning("FiledSearch", "Could not read the Claude key for the search", e);
            return null;
        }
    }

    private PaperListRow Row(Ghost ghost)
    {
        var title = ContactNameResolver.DisplayName(Host, ghost.Item.ContactKey);
        var sent = RelativeTime.Format(ghost.Item.TimestampUtc.LocalDateTime, DateTime.Now);
        return new PaperListRow(title, $"{sent} · {TimelineItemFormatter.Describe(ghost.Item)}");
    }

    private void NoteUse(Ghost ghost, FiledSearchEnd how)
    {
        if (used is null || how == FiledSearchEnd.PutBack)
        {
            used = ghost.Item.SourceId;
            usedHow = how;
        }
    }

    // Un-filing is allowed at any time (the owner asked for it explicitly). What comes back
    // is the ghost, because the source original is already gone — there is nothing else it
    // could be, and saying so plainly is better than implying a round trip that cannot
    // happen.
    private void ShowGhostActions(Ghost ghost)
    {
        NoteUse(ghost, FiledSearchEnd.Opened);
        var who = ContactNameResolver.DisplayName(Host, ghost.Item.ContactKey);
        ActionCard.Show(Host, Overlay, who, TimelineItemFormatter.Describe(ghost.Item),
            new CardAction("Put back in the Mailbox", () => Unfile(ghost), Lead: true));
    }

    private void Unfile(Ghost ghost)
    {
        if (Filing.Unfile(Host, ghost.Item.SourceId) is null)
        {
            return;
        }
        NoteUse(ghost, FiledSearchEnd.PutBack);

        var place = results.IndexOf(ghost);
        results.Remove(ghost);
        Invalidate();
        DogEar.Show(Host, Overlay, $"Put back {ContactNameResolver.DisplayName(Host, ghost.Item.ContactKey)}.",
            onUndo: () =>
            {
                Filing.File(Host, ghost.Item, ghost.FiledAtUtc);
                usedHow = FiledSearchEnd.Opened; // an undone put back was only a look
                results.Insert(Math.Clamp(place, 0, results.Count), ghost);
                Invalidate();
            });
    }
}
