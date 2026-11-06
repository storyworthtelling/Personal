# 6 · Authorial voice: every place it is used

Read-only inventory. No real names, balances or addresses appear below; the only names in the code's prompts are invented test data, which are left out here.

**The main finding:** of roughly 40 places that look like the owner's voice, only about 15 actually send the owner's learned profile to the model. Another 5 ask the model to write "in their voice" without ever loading the profile. About 8 show lines written into the code by hand, set in the voice's script font. About 12 are declared seats that nothing ever composes. The rules governing them differ from app to app.

---

## 1. The shared stack (`Storyworthtelling.Voice/`)

**Profile.** `VoiceProfile.cs` has these fields:
- `Register` (a few words)
- `Traits`
- `Examples` (each a `VoiceExample(Text, ProjectId)`)
- `Avoid`
- `Cadence`
- `Punctuation`
- `DocumentsAnalyzed`, plus a flag `NeedsRelearn`

`IsEmpty` is true when `DocumentsAnalyzed <= 0` or `Register` is blank. `Discard(projectId)` removes one book's examples and lowers the count by one. If that was the only book, the profile empties.

**Storage.**
- The profile is `storyworthtelling-voice.json`, **plaintext JSON in Drive** (`DriveVoiceProfileStore.cs`).
- A missing, blank or garbled file loads as Empty.
- A file in the old shape (it has `summary` or `signatureMoves`) loads as Empty with `NeedsRelearn = true`.
- **Nothing in any app reads `NeedsRelearn`.** No file outside the Voice library and tests mentions it.

**The key.**
- The key comes from the encrypted vault (`storyworthtelling-secrets.json`, `Claude.ApiKey`) through `VaultVoiceConfigStore`, which needs the app's local passphrase.
- `DriveVoiceOptions.RefreshAsync` loads it into a cached `IOptions<VoiceOptions>`.
- The vault carries only the key, so model choice always falls back to the defaults.
- Folio is the exception: it reads its own local `SettingsStore.ClaudeApiKey` live (`Folio.Android/MauiProgram.cs:85-86`).

**Models** (`VoiceOptions.cs` and `VoiceConfig.cs`):

| Setting | Value |
|---|---|
| `Model` | `claude-sonnet-5-5` |
| `FastModel` | `claude-haiku-5-5` |
| `RefineMaxTokens` | 2,000 |
| `ComposeMaxTokens` | 400 |

`ComposeAsync` uses `Model` when `HighVisibility` is true and `FastModel` otherwise (`VoiceClient.cs:167`). CLAUDE.md still says `claude-sonnet-4-6` / `claude-haiku-4-5-20251001`, which is out of date. No voice call uses prompt caching.

**`MessageRequest(Purpose, Context = "", MaxWords = 40, HighVisibility = false)`.**

**`VoicedMessenger.ComposeAsync(request, fallback)`** (`VoicedMessenger.cs`):
- It loads the profile and returns `fallback` if the profile is empty.
- Otherwise it calls the client and returns `fallback` if the reply is blank.
- Any exception other than cancellation is logged at Debug with the Purpose, then `fallback` is returned. It never throws.

### Compose system prompt (verbatim, `VoiceClient.cs:41`)
> "You are writing a single situational message inside an app, in the voice described by the PROFILE below. The message is UI copy shown for a specific occasion — write it to sound like the person the profile describes, honoring their traits, writing in their cadence and punctuation, drawing on their example phrasings, and avoiding what they'd avoid. PURPOSE names the occasion; CONTEXT gives the specifics to reflect. Keep it within the word limit, plain text, no markup, no quotes, no preamble — just the message itself."

The user turn is:
`PROFILE (JSON):\n{register,traits,examples[],avoid,cadence,punctuation}\n\nPURPOSE: …\nCONTEXT: …\nWORD LIMIT: n`

**Every example ever learned goes into every compose call.**

### Refine system prompt (verbatim, `VoiceClient.cs:21`)
> "You are a voice analyst distilling one author's writing voice so short app messages can later be written to sound like them. You are given the CURRENT PROFILE (possibly empty) and a NEW WRITING SAMPLE. Update the profile to reflect the sample, folding it into what's already there rather than replacing it. Capture how the person actually writes — tone, register, attitude, cadence — not the subject matter. Respond with ONLY a JSON object: {"register":string,"traits":[string],"examples":[string],"avoid":[string],"cadence":[string],"punctuation":[string]}. register = how this person sounds, in a few words rather than a paragraph ("wry, warm, economical"). traits = a few short descriptors ("plain-spoken", "unhurried", "dry"). examples = 2 or 3 characteristic phrasings drawn from THIS sample, quoted closely, to anchor the voice concretely — supply only fresh ones from this sample; do NOT repeat the profile's existing examples, as the app preserves those itself. cadence = the shapes their sentences take ("short declarative openers", "lists of three", "a long sentence then a very short one"). punctuation = their marks and how they use them ("em-dash asides", "semicolons, rarely", "no exclamation marks"). Keep cadence and punctuation apart: a sentence shape and a mark are different instructions to write under. avoid = things that would read as not-them ("corporate boilerplate", "hype"). Keep traits, avoid, cadence and punctuation to at most 6 items each. No prose outside the JSON."

The user turn is `CURRENT PROFILE (JSON) … NEW WRITING SAMPLE: <text>`.

How the reply is merged:
- If the reply is unparseable or has no `register`, the existing profile is kept.
- `Register`, `Traits`, `Avoid`, `Cadence` and `Punctuation` are **replaced wholesale** by the model's output.
- `Examples` are merged in code: every old example is kept, new ones are appended, and duplicates are dropped ignoring case. There is no cap.
- `DocumentsAnalyzed` goes up by one.

### What feeds the profile
**Only Folio.** `ReviewProcessor.RefineOwnerVoice` (`Folio.Infrastructure/Pipeline/ReviewProcessor.cs:206-239`):
- It runs once per draft at ingestion, guarded by `Novel.VoiceAnalyzed`.
- It sends the **first 12,000 characters** of the manuscript (`VoiceSampleChars`).
- A failure is logged as a warning and the flag is left false, so a later run retries.
- Deleting a project's last draft calls `Discard(projectId)` (`ProjectService.cs:378-385, 469-487`). That removes the examples only; the register and traits learned from that book stay.

---

## 2. Inventory by app

### Folio (MAUI): 2 occasions
The closed list is in `Folio.Domain/Voice/VoicedOccasions.cs`.

**F1 — desk line for each book** (`DeskLine` = "a one-line note on the desk about how this book is coming along")
- **Composed in:** `ProjectService.VoicedLineAsync` / `ComposeLine` (`ProjectService.cs:401-460`). It calls `IVoiceClient` directly rather than going through VoicedMessenger.
- **Context:** `"Title: {title}. Draft {n}. {facts}"`. The facts come from `DeskStatus.VoiceFacts` (`DeskStatus.cs:41-60`):
  - Completed: "The revision report is finished and delivered."
  - Failed: "The review stopped with an error and did not finish. Do not say or imply that it is complete."
  - Cancelled: "The owner stopped this review before it finished. Do not say or imply that it is complete."
  - Stuck (`NeedsAttention`): null, so no line is composed.
  - Otherwise: "The review is still in progress and is NOT finished. Right now: {step}. Do not say or imply that the draft or the report is complete."
- **Model and length:** `MaxWords: 18`. `HighVisibility` is not set, so it runs on the **fast model**.
- **Caching:** `VoicedLinePlan.NeedsComposing` recomposes only when the stage changes. The line is stored in `Novel.VoicedLine` / `VoicedLineStage` in `folio.db`. The fallback is never stored.
- **Fallback:** the neutral row summary is left as it is; the voiced line simply isn't added.
- **Displayed in:** `MainPage.xaml.cs:494-502`, as a separate label under the facts. Style is `VoiceLabel` (Script / Pinyon, 26, InkBlue). It is suppressed when `NeedsAttention`.
- **Guards:**
  - `VoicedOccasionTests`: no trouble words in an occasion, occasions are distinct, and every `new MessageRequest(` names a `VoicedOccasions.` constant (checked in source).
  - `VoicedLineServiceTests`, including `AStoppedBookGetsNoVoicedLine_EvenOneRememberedFromBefore`.

**F2 — empty desk** (`EmptyDesk` = "a line for an empty desk, inviting a first manuscript")
- **Composed in:** `MainPage.ShowVoicedLinesAsync` (`:120-152`) through `VoicedMessenger`, with context "no manuscripts yet" and `HighVisibility: true` (**strong model**).
- **Caching:** once per local day (`EmptyDeskLinePlan`), stored in Preferences as `EmptyDeskLine` / `EmptyDeskLineDay`.
- **Fallback:** "Nothing on the desk. Add a manuscript and Folio starts reading."
- **Rendering:** the voiced line uses **`EmptyStateLabel`** (Serif Italic, muted, centred), **not** `VoiceLabel` (`MainPage.xaml.cs:426-433`). So in Folio the owner's voice appears in the empty-state typeface.
- **Guard:** `EmptyDeskLineTests`.

**Stale documentation:** CLAUDE.md's "voiced welcome" in Folio no longer exists. The occasions now are only DeskLine and EmptyDesk.

### Index (MAUI): 3 seats
Sources:
- `Ledger.Domain/Voice/VoiceSeatPrompts.cs`
- `VoiceFactBuilder.cs`
- `Ledger.Infrastructure/Voice/VoiceSeatComposer.cs`
- `Ledger.Android/Views/VoiceSeatLine.cs`

**Occasions (Purpose):**
- Accounts: "a line above the accounts about how the household's debt has been moving"
- Snowball: "a line above the debt-free date about how far the debt has come down"
- Savings: "a line under the switch on the savings page about the pace the savings are growing at"

**Instruction.** This is the only app that pins a **second instruction inside Context**, ahead of the FACTS list:
> Common: "Write one short sentence in the owner's voice, as a line of copy at the top of a screen in a personal finance notebook. Build it ONLY from the FACTS below: use no number the FACTS do not give, and do not invent one. Say numbers in words, and percentages in words as in "about one percent". No advice, no instruction, no judgement, no exclamation marks, no emoji, no quotation marks. At most eighteen words. If the FACTS are not enough for a sentence, reply with nothing at all."
>
> Accounts / Snowball add: "Speak only in percentages and counts. Never state a dollar amount, an account number, a bank name or an account name."
>
> Savings adds: "Mention only the monthly rate the FACTS give, as an amount per month. Never state a balance, a total saved, a goal or target, a date, an account name or a bank name."

The composed Context is `<instruction>\n\nFACTS (the only things you may draw on):\n- …`. `MaxWords: 24`, `HighVisibility: true` (**strong model**).

**Facts** (`VoiceFactBuilder`):
- **Snowball:** "Total owed is down {words} percent from its peak, which was {in/last <Month>…}." plus "N loans are already paid off."
- **Accounts:** "Total owed is down/up {words} percent since {Month}." or "about the same as it was in {Month}". This needs an anchor at least 28 days back. It also adds the paid-off count.
- **Savings:** "Taken together, the savings accounts are growing by about {N} dollars a month." N is **digits**, rounded to 5, 10 or 100.
- No facts means the seat is hidden and no call is made.

**Caching and checks:**
- Once per (seat, facts hash, local day), held **in memory only**, so a process restart recomposes.
- A failure is remembered for 10 minutes and then retried.
- No signed-in Google account, no key, or no facts means hidden with no call.
- The reply passes through `VoiceReplyCheck`: at most 30 words, no newline, and on Accounts and Snowball nothing matching `\$|\d{3,}|\d,\d{3}|dollars?|usd|bucks`.

**Fallback:** hidden. There is no neutral line.

**Displayed:** `VoiceLabel` (Pinyon, 26, InkBlue), hidden by default.
- `MainPage.xaml.cs:664-698` shows Accounts, hidden in first-run.
- `SnowballPage.xaml.cs:209`, line placed at `:374` above "Debt-free".
- `SavingsPage.xaml.cs:351`, at the top of the Accounts side.

**Guards:**
- `VoiceSeatPromptTests` (no digits in the instructions; amounts forbidden)
- `VoiceFactsTests` (`AccountsAndSnowballFactsCarryNoDigitsAtAll`)
- `VoiceSeatComposerTests` (logging carries the seat name only; one call per day)
- `VoiceSeatSourceTests` (`VoiceLabel` style, starts hidden, the page never waits)

### Foreword (MAUI)
**W1 — welcome on the Today screen**
- **Composed in:** `MainPage.xaml.cs:84-104`, `new MessageRequest("welcome", "the owner is checking their morning brief", HighVisibility: true)`, strong model.
- **Caching: none.** It is composed on **every `OnAppearing`**.
- **No closed list:** the bare string "welcome" is not in any occasions list.
- **Fallback:** hidden (fallback is the empty string). It is also hidden if Google isn't configured.
- **Displayed:** `MainPage.xaml:21-23`, `VoiceLabel` (Pinyon).
- **No guard.**

**W2–W4 — printed seats that are drawn but never composed:**
- the Brief's opening line: `ForewordPage.cs:54-60`, Pinyon at `PaperTypeScale.Title` ≈ 21pt, centred
- the Day page's narrator: `DayPdfComposer.DrawVoice`, `DayDocument.Voice`
- the checklist coach: `ChecklistPage.cs:29-36`, `DayDocument.ChecklistVoice`

In production nothing ever composes them:
- `CrosswordDeliveryService.cs:156` passes `voice: string.Empty`.
- `DailyDeliveryService.cs:47-55` never sets `Voice` or `ChecklistVoice`.

Only tests fill these fields. These are dead seats.

**W5 — the prayer page's lead line** (`ClaudePastoralComposer.cs:30`). This is deliberately **not** the voice stack, so it has **no profile**:
> "You write one short line at the top of a private prayer page, in the voice of the person whose page it is — plain, unsentimental, the way somebody talks to themselves. It introduces the list below it. Never more than one sentence. Never a prayer, never advice, never scripture, never encouragement of the greeting-card kind. Do not name anybody on the list. Respond with the line and nothing else."

- **Model:** Foreword's own `AnthropicOptions.Model` (claude-sonnet-5-5), max_tokens 200. This is the **only voice-like call with `cache_control`**.
- **Context:** counts written as digits ("{n} things are being held"), how long the oldest has been held, and **the most recent journal entry, verbatim**.
- **Caching:** once per morning delivery.
- **Fallback:** no line. Only the first line of the reply is kept.
- **Rendering:** Pinyon at Title size, in **Ink, not ink-blue**. The same is true of all the PDF script lines, because `DrawText` defaults to `Ink`.
- **Guard:** `PrayerIsolationTests` checks this file uses neither `IVoiceProfileStore` nor `VoicedMessenger`.

### Flyleaf (native)
**L1 — "hello" on the cover-load screen**
- **Composed in:** `HomeActivity.cs:110-135`, through `Codex.Ui.Voice.VoiceGreeting.ComposeAsync("welcome", "the owner just opened Flyleaf; it is {h:mm tt}")`, `highVisibility` defaulting to true (**strong model**).
- **Timing:** once per process (`coverShownThisProcess`). If it takes longer than **2.5 s** the line is dropped. The call is not cancelled, so it is still billed.
- **Fallback:** hidden.
- **Rendering:** `PaperUi.Secondary` (**Sans 14**, recoloured `PaperTheme.Paper`), **not Pinyon**.
- **No guard and no closed list.**

### Score (native): 3 surfaces, all through `VoiceGreeting`

**S1 — greeting** (`ScoreActivity.cs:149-174`)
- **Purpose:** "welcome".
- **Context:** "the owner is opening their music and podcasts". On the first open of the day, if the nightly rescan found something, it adds "…their automatic overnight library rescan found this: {RescanOutcome.Describe()}". That text uses digit counts, e.g. "3 new tracks".
- **Model:** strong.
- **Caching: composed on every `OnCreate`.** `FirstOpenOfDay` only gates the rescan fact, not the call. Comments call this "the hourly seat", but no hourly logic exists.
- **Displayed:** `PaperUi.Voice` (Pinyon, ink-blue, `TypeSp.Voice`) under the header, only on the Music and Podcasts roots (`ShowsVoice`). Discover suppresses it while its crate note shows.

**S2 — Discover crate note** (`DiscoverArea.cs:420-470`)
- **Purpose (verbatim):** "a one-line note about a record in this week's crate, as the person who picked it out — vary how it opens from one pick to the next (lead with an image, a memory, a detail about the record itself, whatever fits) rather than starting with "Picked this because" or any other fixed opening every time".
- **Context:** "{title} by {artist} — the reason it was picked: {pick.Why}". `pick.Why` is itself Claude-generated by `DiscoveryClient`.
- **Model:** strong.
- **Caching:** one per pick, held in memory for as long as the crate is. If the call fails, an empty entry is kept and there is **no retry**.
- **Rendering:** `PaperUi.Voice`.

**S3 — Workout note for each track** (`WorkoutArea.cs:316-340`, `Codex.Domain/Voice/WorkoutVoice.cs`)
- `VoicePicker` makes a weighted draw (coaching 5, pun 3, congratulation 2), never the same category twice in a row.
- Purposes (verbatim):
  - Pun: "one line about the track now playing, built on its title or its words, delivered flat — a pun the speaker is not going to acknowledge having made"
  - Congratulation: "one line congratulating the listener out of all proportion to what they have done, then quietly withdrawing the claim"
  - Coaching: "one line of encouragement from someone performing the part of a coach and not quite committing to it — it lands on a flat retraction rather than a cheer"
- **Context:** "{title} — running at {tempo} BPM" (digits).
- **Model:** `highVisibility: false`, so **fast**.
- **Frequency:** one call per track change, no cache.
- **Rendering:** `PaperUi.Voice`.
- **Guard:** `WorkoutVoiceTests` covers only the picker and the weights.

**No Score seat has a closed list or an errors-never-voiced guard.**

### Post (native): "voice" seat with no model
**P1 — empty Mailbox** (`Codex.Domain/Post/EmptyMailbox.cs`, `MailboxArea.cs:543-562`, state in `EmptyMailboxStore`, prefs `mailboxVoice`)
- It is a pool of seven **lines written in the code**, plus one "go-away" ladder of 4 rungs. The ladder retires for 30 days after its last rung and restarts when mail arrives.
- **There is no model call.** The class comment says "the voice then rewrites whatever was drawn", but nothing does.
- **Rendering:** `PaperUi.EmptyState` (**Serif Italic, muted**), not Pinyon.
- **Stale comment:** the class comment says "§14 permits voiced error copy", which contradicts current §14 ("Errors are never voiced").

`PostActivity.cs:24` notes that there is deliberately no greeting. FiledArea and PostSync use the vault key for search and mail sorting, not the voice.

### Plate (native)
**D1 — empty Darkroom** (`Codex.Domain/Plate/DarkroomVoice.cs`)
- It shows 6 lines written into the code, chosen at random **on every render** (`PlateActivity.EmptyLine()`). There is no model call.
- **Rendering:** `PaperUi.Voice` (Pinyon, ink-blue) at `DarkroomArea.cs:50`.
- The doc says it is "the one place in the whole suite that hand appears", which is false.
- **Guard:** `DarkroomVoiceTests` checks there is no nagging, at least 5 lines, and at most 60 characters.

**D2 — caption over waiting photos** (`DarkroomCaptionClient.cs:51`, triggered by `PlateActivity.Kept` → `CaptionSoon` at `:306-363`)
- It is a vision call on up to 4 JPEGs. **It does not load the profile.** System prompt:
  > "You write one short line for a photographer looking at their own unsorted pictures, in their voice: dry, warm, never instructional. Say something true about what is in front of them. Do not name or identify any person. Do not tell them what to do with the pictures. Reply with the line only — no quotes, no preamble, at most fifteen words."
- User text: `DarkroomVoice.CaptionPrompt(n)`, e.g. "Look at these {n} pictures waiting to be chosen. Write one short line about what they have in common, in the owner's voice. No more than a sentence, and never an instruction."
- **Model:** `FastModel`, `ComposeMaxTokens`.
- **Frequency:** one call per capture, debounced 3 s, single-flight.
- **Persistence:** the caption is saved on `waiting[0]` in the Darkroom codec.
- **Rendering:** `PaperUi.Voice` in `DarkroomArea.cs:60` and `EditorActivity.cs:146`. Captions the owner writes by hand also show in Pinyon.

### Pulp (native): 15 declared seats, 7 actually composed
Sources:
- `Pulp.Domain/Voice/VoicedOccasions.cs` (seat, Purpose, audience, fallback)
- `VoiceFactBuilder` and `ChildVoiceFacts` (facts as words, counts via `NumberWords`)
- `ReadingLevel.Guidance` ("Write for a child aged {x}: short words, one idea, nothing frightening.")
- `VoiceLinePlan`

Every seat goes through `VoiceGreeting.ComposeAsync(purpose, facts.Context)` with the default `highVisibility`, so **every seat, including children's, uses the strong model**.

**Caching:** once per (seat, facts hash, day), stored in SharedPreferences. `pulp-watching-voice` is keyed per seat only, so different children overwrite each other's entry. `pulp-title-voice` is keyed per title. **An empty attempt is remembered for the whole day**, so a key that arrives later produces no line until tomorrow.

Composed seats:

| Seat | Purpose | Facts (examples) | Fallback | Display |
|---|---|---|---|---|
| Greeting (`WatchingArea.cs:280-300`) | "a greeting under the Continue Watching header" | "It is {morning/…}." · "This is the first time Pulp has been opened today." · "There are {words} titles in progress." | Hide | `MainActivity` greetingView, `PaperUi.Voice` |
| EmptyContinueWatching | "an invitation on a home page with nothing started yet" | "Nothing has been started yet." | Neutral "Nothing started yet. Discover has this week's stack." | `EmptyState` (voice uses `PaperUi.Voice`; neutral line uses Serif Italic) |
| WhyThis (`TitleActivity.cs:454-470`) | "one line on a title page giving the reason this title was chosen for the viewer" | "The title is {name}." · "It was chosen from the owner's own taste." · "The viewer recently finished {past}, which is like it." | Hide | `PaperUi.Voice` |
| ChildGreeting (`ChildWatchingPage.cs:254`) | "a gentle greeting for a child" | the part of the day plus the reading level; `firstOpenOfDay` is hard-coded false | Hide | greetingView, Pinyon |
| ChildFavorites / EmptyChildFavorites (`FavoritesPage.cs:176`) | "one gentle line above a child's favorites" / "…nothing on it yet" | count plus one visible title name, plus the reading level | Hide / Neutral "No favorites yet." | Pinyon / EmptyState |
| EmptyChildWatching | "…nothing started yet" | | Neutral "Nothing started yet." | EmptyState |
| EmptyChildPlaylists (`ChildPlaylistsPage.cs:185`) | "…none on it yet" | | Neutral "No playlists yet." | EmptyState |

**Declared but never composed.** These always call `Present(ShowNeutral)`:
- EmptyPlaylists ("No playlists yet.")
- EmptyPlaylist
- EmptyRecentlyWatched
- EmptyCalendar ("Nothing in the next two weeks.")
- EmptySports
- StackUsedUp ("That's the week. More on Friday.")

**PlaylistLine** has a fact builder but is wired to no screen at all.

**Guards:**
- `VoicedOccasionTests`: closed list; no error, notice or dog-ear seat; every request names an occasion; only child seats are spoken to a child.
- `VoiceFactBuilderTests`: never another viewer's history, no reading level on adult seats.
- `VoiceLinePlanTests`.
- `EmptyStateNeutralLineTests`.

### Constitution (native): 6 seats, 2 actually composed
Sources:
- `Constitution.Domain/Voice/VoicedOccasions.cs` (id, Purpose, MaxWords)
- `SeatFacts.cs`
- `SeatFallbacks.cs` (wording taken from "book p.18")
- `Constitution.Infrastructure/Voice/SeatComposer.cs`
- `SeatCache.cs`

**How a seat is composed:**
- Through `VoicedMessenger`, with `MaxWords` set per occasion and **strong model** (`HighVisibility: true`).
- Context: "Facts, in order:\n- …\nSay only what these facts say, in a single sentence. Add no figure, name or advice that is not stated."
- Caching: `SeatCache` keeps one voiced line per (seat id, hash of the ordered facts, local day) in a persistent store (`BodySeatLineStore`). A fallback is never stored; a failure is remembered for 10 minutes in memory.
- **The line is never hidden.** The fallback is the plain sentence in **Serif Regular, Ink**; the voiced line is `ScrapContent.Voice` (Pinyon, ink-blue, `TypeSp.Voice`).

**Composed seats:**
- **BodyMonthly** ("one sentence on Body about how the month's signals moved against last month", 35 words). Displayed in `BodyArea.cs:91-99` / `BodySeatNote`. Facts include **figures**: "{Signal} {rose/fell} this month, which is worse/better: {N unit} on average, against {M unit} last month."
- **SignalSentence** ("one sentence on a signal's page setting its latest reading against the owner's own past", 30 words). Displayed in `BodyArea.cs:131-139`. Facts include the latest reading and the past average **as digits**.
- **WorkoutEncouragement** ("one line of encouragement on a workout page, about progress since last time", 20 words). Built by `WorkoutWeekRun.cs:426-440` → `SeatCacheEncouragement` → **printed on the workout PDF pages for the reMarkable** (`PageDraft.Voice`). Facts: exercise, "Load today: {x} {unit}, up from {y}…". The fallback is "{x} {unit} today, up from {y}." **`WorkoutDocumentBuilder.cs:90` defaults `IsVoiced` to `?? true`.**

**Declared but never composed:**
- TodayTopLine and RestDayLine: `TodayModel` always builds the fallback with `IsVoiced: false`, and nothing ever composes it.
- MealSheetIntro: the request builder exists, but `MealDocumentsStep` never calls it.

Today shows "Nothing is planned yet." or "{Kind} at {h:mm}. {dinner}".

**Guards:**
- `VoicedOccasionTests` (closed list, stored ids, no trouble words)
- `SeatComposerTests` (logs the seat id and failure type only)
- `SeatCacheTests`
- `SeatFactsTests.FallbacksMatchPageEighteen`
- `NoFaithContentTests` (no faith words in any fact or prompt)
- `BodyStaysHomeTests` (allows "the facts" for seats, per D98)

---

## 3. Analysis

### Inconsistencies

1. **Four different ideas of what "the owner's voice" is.**
   - **(a)** Built from the profile: Folio, Index, Foreword's welcome, Flyleaf, Score, Pulp, Constitution.
   - **(b)** Asks the model for "their voice" with **no profile at all**: Plate captions and the Foreword prayer line. The Plate prompt hard-codes "dry, warm", which is a guess at the profile.
   - **(c)** Lines written into the code shown in the voice's place: Post's empty Mailbox and Plate's empty Darkroom. The Darkroom puts them in Pinyon, which §4 reserves for voiced messages.
   - **(d)** Lines written in code that encode assumptions about the profile: `WorkoutVoice`'s comment says "The owner's profile bans sincere exclamation marks, hype…".

   §14 is written as if only (a) exists.

2. **Closed lists and guards exist only in the MAUI apps, Pulp and Constitution.** Foreword's welcome, Flyleaf and all of Score pass bare strings ("welcome", free-form Purposes). Nothing in FocusPhone enforces "errors never voiced" for those apps.

3. **Rendering doesn't follow §4 in six places:**
   - Folio's empty desk is Serif Italic.
   - Flyleaf's hello is Sans 14 in paper colour.
   - Post's empty Mailbox is Serif Italic.
   - All printed script lines are in Ink rather than ink-blue: Foreword (dead seats plus the prayer line) and Constitution's PDFs. This may be deliberate for e-ink, but it isn't written down.
   - Pinyon is used for text that isn't composed voice: hand-written captions and the Darkroom lines.
   - Pinyon is used for **young children's** lines in Pulp, which is a readability concern for early readers.

4. **The fallback rule differs from app to app:**
   - Index, Pulp's non-empty seats, Folio's desk line, Foreword, Flyleaf and Score **hide** the line.
   - Constitution **never hides**, falling back to plain Serif.
   - Pulp's empty pages and Folio's empty desk show a neutral line.
   - Post always shows its lines written in code.

5. **Model tier varies.** Folio's desk line and Score's workout note use the fast model. Everything else, including Pulp's children's and empty-page lines and Constitution's encouragement lines, uses the strong model.

6. **Caching ranges from none to persistent:**
   - None: Foreword's welcome (every appearance), Score's greeting (every activity creation), Workout (every track).
   - Once per process: Flyleaf.
   - In memory per day: Index.
   - Persistent per day: Pulp and Constitution.
   - Per stage: Folio.

   Retry after a failure also differs: Index and Constitution retry after 10 minutes, Pulp waits until the next day, and Score's Discover never retries for that crate.

7. **Where the instructions live differs.** Index puts a long instruction inside Context and asks for 18 words while requesting 24 and accepting up to 30. Constitution appends one rule sentence. Pulp, Folio and Score rely on Purpose alone.

### Where the §14 rules are breached or at risk

- **Numbers.** §14 says a voiced line is never built "from a raw amount". These seats break that:
  - Constitution's BodyMonthly and SignalSentence send body readings as digits to Anthropic. This also contradicts CLAUDE.md, which says seats get "words and counts (never a figure)", although `BodyStaysHomeTests` and D98 allow it.
  - Constitution's encouragement sends loads.
  - Index Savings sends a dollar rate. This is a documented exception, D114.
  - Score sends counts and BPM as digits; Flyleaf sends a digit time.
  - The Foreword prayer line, which is outside the stack, sends counts plus verbatim journal text.
- **Errors.** Folio's `VoiceFacts` still voices **a Failed review** ("stopped with an error…") whenever `NeedsAttention` is false, i.e. when the attempt count is under the maximum. The comment in Folio's `ProjectService` says "a stopped book gets no line at all", but that only covers a book that is running and stuck. Folio's own occasions list exists precisely to forbid this.
- **Facts that may be wrong:**
  - Pulp's WhyThis always claims "It was chosen from the owner's own taste" and always passes `ViewerStanding.DeviceOwner` (`TitleActivity.cs:462`), even for a title found by search, or when the other adult is viewing. The `OtherAdult` path is never used.
  - Score's crate note is grounded in `pick.Why`, which is itself model-generated.
  - Index Accounts says "since {Month}" based on an anchor whose spacing can be uneven.
- **Whose voice.** On the partner's phone, Index, Pulp and Constitution (encouragement printed on *her* workout sheets) speak in the **owner's** voice. Pulp's child seats speak to children in a voice learned from adult manuscripts. Its traits may be "dry/wry", while the only guidance added is the reading level.

### Risks

- **Cost:**
  - Foreword composes a strong-model line on every page appearance, and Score on every activity creation.
  - Flyleaf's 2.5 s timeout throws away lines that are still billed.
  - Plate makes a vision call per capture.
  - Score's Workout makes one call per track.
  - The profile's `Examples` list grows without limit and is sent **in every compose call** from every app, so per-call cost rises with every book. There is no prompt caching on the voice calls.
- **Repetition.** "welcome" is the purpose for four different surfaces (Foreword, Flyleaf, Score, Pulp, nearly). Workout and Discover had to fight repetitive openings by adding instructions. Pulp's greeting recomposes up to four times a day as the part of day changes.
- **Stale or overwritten lines:**
  - Plate's empty line changes on every redraw, so it flickers.
  - Plate's auto-caption is written to `waiting[0]` and can **overwrite a caption the owner wrote by hand** for that photo.
  - Pulp's per-seat keys overwrite each other between children.
  - Index's in-memory cache is lost on process death.
- **The profile itself:**
  - `NeedsRelearn` is never acted on, so an old-shape file silences every app until a new draft is ingested. Old drafts are marked `VoiceAnalyzed` and never refine again.
  - Register and traits are replaced on each refine, so the "fold" depends on the model honouring the instruction.
  - Each new draft of the same book refines again from the same opening 12,000 characters, over-weighting that book.
  - `Discard` can't un-learn register or traits.
  - Examples are verbatim manuscript passages stored in **plaintext** Drive JSON and sent in every app's prompt.
- **Logging.** VoicedMessenger logs the Purpose at Debug. Purposes contain no data, but Score's crate Purpose is long.

### Missing or excessive seats

**Dead or declared-only seats** that look finished to anyone reading the docs:
- Foreword: the Brief opening, Day narrator and Checklist coach.
- Constitution: TodayTopLine, RestDayLine and MealSheetIntro.
- Pulp: PlaylistLine and six empty pages that only ever show the neutral line.

**Possibly excessive:**
- Score has three surfaces, one with a call per track.
- Pulp has 15 declared seats, including children's.
- Plate captions every capture.

**Documentation drift:**
- CLAUDE.md describes a Folio welcome and "Post's voice gets one seat" (it has no model).
- It lists the wrong default models.
- It says Constitution's seats get "never a figure".
- `DarkroomVoice` says it is "the only place the hand appears".
- Score comments describe an "hourly seat".
- `EmptyMailbox` says "§14 permits voiced error copy".
