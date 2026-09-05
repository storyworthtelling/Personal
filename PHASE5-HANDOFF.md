# FocusPhone Audio — Context Handoff

## What this is
Redesigning the **FocusPhone Audio app** (distraction-free Pixel launcher project) as a UX designer.
Two sides: **Podcasts** (north star: Podcast Addict) and **Music** (north star: iPod Classic, streaming
the owner's own files from Google Drive). Repo: `/home/user/Personal`, project under `FocusPhone/`.
User is `storyworthtelling`.

## Status: Phases 0–4b are MERGED (PR #110)
Podcasts complete (subscribe, OPML import, resume, per-show speed, skip-silence, queue w/ 3 auto-arrange
strategies, manage-subscriptions). Music works end-to-end: Google sign-in (AccountManager, no Play-Services
dep) → Drive folder scan (TagLibSharp tags) → SQLite catalog cache → iPod browse → streams/plays via
`drive://` ResolvingDataSource. `PlaybackService` is a Media3 `MediaLibraryService` (Android-Auto-capable
structurally; **browse tree not yet populated** — deferred to Phase 9).

## The plan
Full plan file: **`/root/.claude/plans/take-up-the-persona-virtual-zebra.md`** — read "PART 2 — Next Phases"
for the current work. A designer feedback round on-device found the app structurally sound but **full of
dead-ends**. Two deep design-question batches were completed; decisions locked below.

**Next phases:** 5 (P0 dead-end fixes) → 6 (Listening Genius domain/infra, TDD) → 7 (Discovery tab + Claude
pass) → 8 (music playback polish) → 9 (Android Auto tree + Cast, device-gated).

## Listening Genius decisions (locked, for Phases 6–7)
New pillar the owner requested — a Claude-powered **Taste Profile** modeled on Storyboarder's
`DesignBibleMerger` (map-reduce evidence → durable propagatable profile). Decisions:
- **Drive-only source** (no on-device library, no in-app upload — just a "how to add music" guide).
- **Claude-powered engine** (owner's API key, needs kill switch + honest data note).
- **Passive-only learning** (no ratings/thumbs — completions/skips/replays/album-vs-shuffle-per-genre/time-of-day).
- **Explicit guardrail** = embedded advisory tag + Claude inference.
- **Discovery is its own tab but playlists generate only when prompted.**
- **Friday picks** = quiet in-app "New for you" shelf, no push/badge.

## CURRENT TASK: Phase 5 (just started)
**Branch already reset:** `git checkout -B claude/focusphone-audio-ux-research-r3pi2z origin/main` done
(main is at `dea06cd`; PR #110 was merged so we restart fresh — do NOT stack on merged history). Nothing
committed yet this phase. Commit identity set: `user.email=noreply@anthropic.com`, `user.name=Claude`.

Phase 5 = **P0 dead-end fixes**, three parts:

1. **Podcast OPML → listen dead-end.** New pure TDD helper `FocusPhone.Domain/Audio/OpmlImportSelection.cs`:
   given parsed `OpmlSubscription`s + current `SubscriptionList`, produce a checklist model
   (`{Title, FeedUrl, AlreadySubscribed}`, new ones checked) + `Apply(selectedFeedUrls)` → updated list
   (dedupe). Tests in `FocusPhone.Tests/Audio/OpmlImportSelectionTests.cs`. Then `ManageSubscriptionsActivity`:
   OPML pick → checkbox dialog → persist → **finish() back to a visibly populated Home** (auto-return).
   `AudioActivity` rebuilds Podcasts pane on resume + shows "Fetching episodes…" *sentence* (not blank).
   Fix first-run/empty copy. Same auto-return for single-URL add. Manage stays management-only.

2. **Music cold-start dead-end.** Replace empty state with a "How to add music" guide (rip CDs to tagged
   FLAC/MP3, iTunes files, buy real downloads; **file type + embedded tags matter, Drive folder structure
   doesn't**; supported exts in `AudioFileTypes.cs`: mp3/m4a/m4b/aac/flac/ogg/oga/opus/wav/alac/wma) +
   Scan button inline.

3. **No-dead-ends sweep** across all Audio screens (forward action + way home).

## Key technical facts / answered questions
- **Cannot rip from YouTube Music in-app** (no API + ToS). Legit sources: rip CDs, iTunes/purchased files,
  Google Takeout, real download stores (Bandcamp/Qobuz/Amazon MP3).
- Catalog organization comes from **embedded tags** (TagLibSharp: title/artist/**album artist**/album/
  track#/disc#/genre/art), NOT folders/filenames.

## Files already examined (relevant to Phase 5)
- `FocusPhone.Infrastructure/Podcasts/OpmlImportParser.cs` — `Parse(opml) → IReadOnlyList<OpmlSubscription>`
  (record: `Title`, `FeedUrl`), dedupes by URL. Pure.

## Files still to read (immediate next step)
- `FocusPhone.Domain/Audio/SubscriptionList.cs` + `PodcastSubscription.cs` (Add/Remove/Move/Export)
- `FocusPhone.Audio.Android/ManageSubscriptionsActivity.cs`
- `FocusPhone.Audio.Android/SubscriptionStore.cs`
- `FocusPhone.Audio.Android/AudioActivity.cs` (large — Podcasts pane + Music empty state sections)

## Working constraints (CLAUDE.md + session)
- **No dotnet/device in sandbox** — validation is CI only (`build-focusphone.yml`: test job + signed APK
  build). Push and watch via GitHub MCP Actions tools. Target `FocusPhone.Tests/FocusPhone.Tests.csproj`
  for tests.
- **TDD strictly** — tests before implementation. Pure logic in Domain/Infra; Android heads are thin
  untested adapters.
- **Design system:** no hex/type-size literals outside `DesignTokens.cs` (leak-guard test). Reuse `PaperUi`
  factories + existing dialog patterns → no new tokens for Phase 5. Success = inline state change (no toasts);
  destructive = undo-first (`UndoBar`); loading = status *sentence* not spinner; 12-hour/relative time.
- **C# style:** MS conventions, NO underscores anywhere (private fields are `camelCase`). Object-oriented;
  avoid unnecessary async.
- **Git:** develop + push to `claude/focusphone-audio-ux-research-r3pi2z`, `git push -u origin` with
  exponential-backoff retry. Never push to main. Don't open a PR unless asked. Commit co-author trailer:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` + `Claude-Session:` line. Don't put the model
  ID in any pushed artifact.
- Batch device testing to the END (owner's standing instruction — build through phases first).

## Note on the stop-hook "Unverified commits" warning
The git stop-hook flagged 72 commits as unverified. These are **pre-existing merged `main` history** (merge
commits, prior Claude commits, an unrelated author), NOT commits from this session — an artifact of comparing
the freshly-reset branch against the stale merged branch tip. **Do NOT rebase/amend them.** The warning
clears once the first Phase 5 commit lands and the remote branch advances past the merged tip.

## Immediate next step
Read the four files listed above. Then write `OpmlImportSelectionTests.cs` first (TDD), implement
`OpmlImportSelection.cs`, wire the checklist dialog + auto-return + populated-Home, add the music "how to add"
guide, do the no-dead-ends sweep. Commit per logical group, push, watch CI green.
