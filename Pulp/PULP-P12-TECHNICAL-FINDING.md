# Pulp (P12) — Gate 2 technical finding

**Status:** for the owner's approval. Answers the ten items `PULP-P12-DISCOVERY.md` carried to gate 2,
surveys the data providers the way Score's P6 WS2 survey did, names the frozen identifiers, and says
what only the owner's phone can prove. Every figure below was read from the provider's own page or
the platform's own source on 2026-10-04; links are at the end. **Nothing here names the household's
real services, titles or teams** — where a decision concerns them it records the shape.

---

## 0. The answer in one page

1. **Pulp cannot be the player** (D2). Every major service refuses playback in a mobile browser and
   sends the user to its app; getting round that means spoofing a desktop browser inside a WebView,
   which breaches each service's terms and would still play at Widevine L3 (SD) at best. **Hand-off is
   the answer**, and the owner's stated expectation was right. §2.1.
2. **TMDB — the obvious metadata source — is ruled out by its own terms.** Its API terms (updated
   2023-10-20) forbid using TMDB "in connection with … a machine learning (ML) or artificial
   intelligence (AI) based Application" and "interactive query-response system (including large
   language model …)". Pulp's mood search, model-made playlists and voiced why-this lines make it
   exactly that. §1.
3. **The availability source is Movie of the Night's Streaming Availability API**: deep links down to
   the episode, video quality, add-ons, rent/buy prices, **expiry dates**, a changes feed, posters,
   overview, cast, seasons and episodes, 66 countries — and **commercial use allowed, no AI clause**.
   Its free tier is **1,000 requests a month**, which does not cover D123's nightly refresh; §1.3 has
   the budget and the decision it forces.
4. **Three gaps no free source fills** and which the owner has to rule on (§6): **rating reasons**
   (D36 — no free API carries the MPA/TV advisory text), **person and franchise search for films**
   (D64 — only TMDB has them, and TMDB is out), and **licensed sports data** (D139 — the free options
   are either crippled or unofficial).
5. **No route chooses a service profile on hand-off** (D11), on any service surveyed — but there is a
   **configuration route that satisfies D44**: lock every adult profile on a service with that
   service's own profile PIN, and a hand-off for a child can then only land on a child's profile or a
   PIN prompt. Pulp cannot verify it; the owner attests to it per service in Settings. §2.2.
6. **Finished detection is real on Android** (D30): an enabled notification listener can read other
   apps' active media sessions (title, position, duration). On the locked phone Pulp cannot be granted
   that itself, but **Flyleaf already is a notification listener** and can hand the sessions over.
   Whether each service publishes its title into the session is device-only. §2.3.
7. **Deep links go to the service's package explicitly** (`Intent.setPackage`), so Flyleaf's pinned
   restricted browser can never intercept a hand-off as an ordinary web link. §2.4.

---

## 1. Provider survey

Judged, in the order that decides: **terms that permit this app** (personal, non-commercial, with
AI features — D64, D104, D36/D38), **coverage of D52** (subscription, free-with-ads, live), **deep
links** (D8, D44), **cost against D138's free-only rule**, then rate limits and keys.

### 1.1 Titles and availability

| Source | Terms for Pulp | What it carries | Free tier | Verdict |
|---|---|---|---|---|
| **Streaming Availability API** (Movie of the Night) | Commercial use allowed; **no AI/ML clause**; attribution required where data is shown; no redistribution | Per-title and per-episode streaming options: `link` (always) and `videoLink` deep links, `quality` (SD/HD/UHD), `type` (subscription/free/addon/rent/buy), `price`, `expiresOn`/`expiresSoon`, `availableSince`; show `overview`, `genres`, `cast`, `directors`, `rating` (an aggregate score), `runtime`, `seasonCount`/`episodeCount`, `imageSet`, `seasons[].episodes[]` with `overview`; `/changes` (new/removed/updated/expiring/upcoming); title search; filtered search; top lists | **1,000 requests/month**, 1,000/hour cap; Pro $29/mo | **In — primary.** The only free source with deep links and expiry dates under terms Pulp can meet. |
| **TMDB** | Free for non-commercial use, **but forbids use "in connection with … an AI based Application" and with LLM "query-response systems"**; 6-month cache cap; logo required | Everything (search, people, collections/franchises, certifications with descriptors, trailers via YouTube keys); its watch-providers data is JustWatch's and carries **no deep links** | Generous | **Out** — the AI clause. Raised in §6 in case the owner wants a narrower reading. |
| **Watchmode** | Free tier non-commercial; attribution | Deep links and episode-level sources **on paid tiers only** ($349/mo+) | 2,500 credits/mo, no deep links, no episodes | **Out** — the features Pulp needs are paid. |
| **JustWatch** | Partner/commercial agreements only; the public GraphQL used by hobby apps is not licensed | — | — | **Out.** |
| **Reelgood** | No public API | — | — | **Out.** |
| **TVmaze** | CC BY-SA, keyless, ≥20 calls/10 s per IP | Shows, seasons, episodes, **air dates and a streaming ("web") schedule**, people and cast for shows | Free | **In — for episode release dates** (D21, D146) and show-cast. Films are not covered. |
| **OMDb** | Personal use only ("you may not build a business") | IMDb-sourced `Rated` (MPA and US TV rating) | ~1,000/day with a free key | **In — for the rating chip** (D120, D127). It carries the rating, **not the reasons**. |
| **YouTube Data API** | Google API terms; a key from the owner's Cloud project | Trailer search; a league channel's uploads (D71) | 10,000 units/day (a search costs 100) | **In — for trailers and highlights**, replacing TMDB's video keys. |

**Rating reasons (D36).** No free source in the survey carries the advisory text ("for violence and
language"). TMDB's `release_dates`/`content_ratings` have a `descriptors` array whose US coverage is
unmeasured — and TMDB is out anyway. The MPA publishes reasons on its own site but offers no API. §6
puts the choice to the owner.

### 1.2 Sports (D54–D61, D67, D70–D72)

| Source | Terms | Coverage | Verdict |
|---|---|---|---|
| **ESPN site JSON** (`site.api.espn.com`) | **Unofficial and unlicensed** — no published terms grant use; can change without notice | Measured today: scoreboards return 200 for NFL, NHL, WNBA, NWSL **and AFL**; events carry `season.type` (preseason/regular/postseason, D184) and `broadcasts` (network names, e.g. a national network); standings return 200 | Free, keyless — **the only free source covering D56** |
| **TheSportsDB** | Free tier usable but **crippled** (schedules return 1 event, search 1 result, no standings or TV listings); premium $9/mo | Broad, including AFL; premium adds standings, TV listings by channel, highlight links | **Free tier out; premium is the licensed answer** |
| Official league APIs | Partner-only | — | Out |

Either way **"which service carries this game"** is a mapping Pulp owns: the feeds name a *network*
("a national sports network"), not the household's live service. The household's live services and
the channels each carries are entered once in Settings (D86's service setup grows a channel list).

### 1.3 The free-tier budget, and what it forces

Streaming Availability's free 1,000 requests a month against D123 (nightly refresh of everything on
playlists and Continue Watching, plus a fresh look on open):

| Use | Per month (estimate, household scale) |
|---|---|
| Nightly refresh of ~60 tracked titles (D123 as written) | ~1,800 — **over budget on its own** |
| Weekly refresh of the same 60 | ~260 |
| `/changes` once a day for the household's services, expiring + new | ~60–120 |
| Title opens and searches | ~300–450 |
| Friday stack resolution (D110), 5 titles × ~2 calls | ~40 |

**Weekly refresh + the daily changes feed + on-open lookups fits (~700–870/month)**, and the changes
feed is what actually catches "leaving soon" (D124) and "arrived on our service" (D20's waiting
entries) between weekly passes. D123 has to move from nightly to that shape, or the owner accepts the
$29 Pro tier. §6.

---

## 2. The carried questions, answered

### 2.1 In-app playback (D2) — not possible, by terms and by mechanism

The major services decline playback in mobile browsers and redirect to their apps (Netflix and
Disney+ help pages say so directly). The only route into Pulp would be a WebView spoofing a desktop
browser, granting `RESOURCE_PROTECTED_MEDIA_ID` for Widevine — which (a) breaches each service's terms
of use, (b) plays at Widevine L3, so SD, and (c) needs the service's web sign-in inside an embedded
WebView, which Google-account sign-in already refuses (`disallowed_useragent`, the wall Score hit).
**Ruled out; hand-off is the design.** The one thing Pulp *does* play is a **YouTube trailer or
highlight** (D34, D71), embedded through the IFrame player in a WebView — which since 2025 must send a
Referer (YouTube error 153 otherwise), done by loading the player page with an https `baseUrl`.

### 2.2 Choosing a service profile (D11) and the children's line (D44)

No surveyed service documents a deep link or intent extra that selects a profile; community catalogues
of Android TV deep links list title links only. **There is no profile-choosing route.** What there is:

- **The configuration route (recommended).** Every major service lets a profile carry its own PIN.
  If the owner PIN-locks **every adult profile** on a service, a hand-off for a child lands on the
  child's profile or on a PIN prompt the child cannot pass. Pulp cannot see this; the owner **attests**
  to it per service in Settings ("adult profiles are PIN-locked"), and D44 then reads: *a child may
  start titles only on services the owner has attested*. That is honest about who guarantees what.
- **Kids-only apps** (a separate kids app a service publishes) need no attestation — the app *is* the
  child's profile.
- **An accessibility service reading the service's screen** to detect which profile is active was
  considered and **rejected**: fragile across app updates, and an accessibility grant on the locked
  phone is the broadest permission on the device.

### 2.3 Finished detection (D30, D182)

Android's own source, `MediaSessionManager.getActiveSessions(ComponentName)`: *"This requires the
MEDIA_CONTENT_CONTROL permission … You may also retrieve this list if your app is an enabled
notification listener."* A `MediaController` exposes the playing app's package, metadata (title when
the app publishes it), playback state and position. So:

- **Her phone (unrestricted, D94):** Pulp itself can be an enabled notification listener — one
  toggle she grants.
- **The owner's phone:** the lockdown removes that Settings toggle, and no device-owner API grants
  notification-listener access. **Flyleaf already is an enabled listener** (`TileNotificationListener`),
  so Flyleaf reads the sessions of the services Pulp tracks and hands Pulp *package, title, position,
  duration* over a signature-guarded channel — a third cross-app contract beside D90 and D92.
- **Unknown until a device:** whether each service publishes the *title* and *position* into its
  session while playing on the phone (some only do so while casting). Where it does not, D30's
  fallback (handed off = watched) stands.
- Media-session play/pause from the phone (D85) is the same API — and a TV-phase matter (D187).

### 2.4 Deep links (D8, D55, D57, D122)

- **Links come from the availability source** (`videoLink` where present, else `link`) — https URLs
  on the service's own domain, down to the episode.
- **Pulp sends them as `ACTION_VIEW` with `setPackage(<the service's package>)`.** That matters on the
  locked phone: Flyleaf pins `RestrictedBrowserActivity` as the default web handler, and an
  unverified App Link would otherwise fall to it and show the Blocked page. An explicit package goes
  straight to the app that declares the domain. The service → package table is public catalogue data
  and lives in Domain.
- **Detecting a mis-landing (D122):** the media session again — if no session for the expected title
  appears within a window after hand-off, Pulp raises the D122 dog-ear. Where a service publishes no
  title, Pulp cannot tell the title page from the app's home; it says so rather than guess.
- **Recordings, not the live feed (D55):** no surveyed source links to a cloud-DVR *recording* of a
  game, and the sports feeds' event ids do not map to a live service's ids. **Honest consequence:**
  Pulp can open the live service (or its channel, where a channel link exists — D57) and name the
  game, but cannot land on the recording, and cannot promise the service's own screens are
  spoiler-free. Device-only: whether the household's live service offers a hide-scores setting.

### 2.5 The availability source in the app

Thin `HttpClient` layer in Infrastructure over Streaming Availability, TVmaze, OMDb and the YouTube
Data API, each faked in tests. Domain holds the rules: best-quality home (D28: quality, then no-ads),
the one rent/buy line (D53: cheapest at HD or better), leaving-soon from `expiresOn` (D124), the
stack's piles (D106–D108), the children's filter (D12, D127–D129). Keys (Streaming Availability,
OMDb, YouTube) live in a new **`SecretsVault.Pulp`** section of the shared vault; TVmaze and ESPN need
none. **No key in source** — a guard (§3).

### 2.6 Sports data (D139) — see §1.2 and §6.

### 2.7 The shared household file (D46, D47)

- **Shape:** `storyworthtelling-pulp-household.json` in the owner's Drive, `VaultEnvelope`-encrypted
  with the vault passphrase. Every entry is a record with an id, a **hybrid logical clock** stamp and
  the writer's install id; merging is last-writer-wins per record, deletes are tombstones. That makes
  the merge commutative, so the two phones (and later the TV) converge whatever order they write in.
- **Drive has no conditional write in v3**, so two writers can still race between read and write.
  The publisher therefore reads, merges, writes, **re-reads and re-merges once**; a lost write
  survives on its writer's phone and lands on its next pass. Tested with interleaved fakes.
- **Cross-account visibility** — the other adult's install reads and writes a file the owner owns.
  With the full `drive` scope a file *shared with* an account is visible to it (`sharedWithMe`) and
  writable if shared as editor. **Owner action:** share the file once. Unverified on a device — the
  same open question Index carries.
- **Private viewing (D97) inside a shared file:** both adults hold the passphrase, so "private" is a
  display rule, not a cryptographic one. Stated plainly rather than implied otherwise.

### 2.8 The Pulp calendar (D25, D145–D149)

Calendar API v3: `calendars.insert` in the owner's account, then `acl.insert` with role `writer` for
the other adult; Pulp stores the calendar id and finds it by id (#13). Needs the `calendar` scope on
the owner's install — a new consent screen, asked for on its own (Foreword's precedent with People).
Foreword then maps it like any other calendar.

### 2.9 The two cross-app contracts, plus a third

| Contract | Direction | Shape |
|---|---|---|
| Usage counts (D90) | Pulp → Index | Explicit broadcast to `com.storyworthtelling.index.UsageReceiver`, signature permission `com.storyworthtelling.index.RECEIVE_USAGE`, payload *service id, month, hand-off count* — the Post → Index pattern exactly |
| Hidden streaming apps (D92) | Pulp → Flyleaf | Explicit broadcast to Flyleaf with the ticked services' package ids, signature permission `com.storyworthtelling.focusphone.launcher.RECEIVE_HIDDEN`; Flyleaf persists the set and filters its tray |
| Playback sessions (D30, §2.3) | Flyleaf → Pulp | Explicit broadcast of *package, title, position, duration* for packages Pulp named, signature permission `com.storyworthtelling.pulp.RECEIVE_SESSIONS` |

All three are signature-level, free because every head shares one keystore, and each gets a contract
test on both sides, as `IndexHandoffContractTests` does today.

### 2.10 TV-phase notes, recorded but not built (D187)

Being the Google TV home and the Photos screensaver (D81 — the Photos Library API only reads
app-created content since March 2025; Google TV's own ambient mode already shows albums) go to the
TV phase's brief.

---

## 3. Frozen identifiers and guards

Frozen from the first build — nothing fails loudly when these move, so each gets a guard in
`Pulp.Tests/Guard/PulpInvariantsTests.cs`:

| Identifier | Value | Why |
|---|---|---|
| App id | `com.storyworthtelling.pulp` (one constant, `PulpIdentity.ApplicationId`) | The update channel installs by package id |
| Household file | `storyworthtelling-pulp-household.json` | Found by name on first discovery, then by id |
| Vault section | `SecretsVault.Pulp` with `[JsonPropertyName("Pulp")]` | The vault has no naming policy; a rename silently empties the section (Foreword's lesson) |
| Permissions | `com.storyworthtelling.pulp.RECEIVE_SESSIONS`, `com.storyworthtelling.index.RECEIVE_USAGE`, `com.storyworthtelling.focusphone.launcher.RECEIVE_HIDDEN` | A cross-app contract is only as stable as its names |
| Workers | `FridayStackWorker`, `WeeklyRefreshWorker` registered Java names + unique work names | WorkManager persists rows by class name |
| SharedPreferences keys | string constants, never `nameof` | Folio's and Foreword's lesson |
| versionCode band | **9** (`SuiteVersionBands`) | Clear of every app already shipped; one place decides |

Standing guards the plan will carry: **Pulp opens no playback stream of its own** (no ExoPlayer/Media3
reference, no `MediaPlayer`, no protected-media grant; the only WebView player is the YouTube IFrame,
loaded from one class), **no provider key in source** (a scan for key-shaped literals across `Pulp/`),
no hex or type-size literal outside token files, the hue-separation guard extended to Marigold (D202),
and the children's filter order (block beats allow beats ceiling, D12).

## 4. What only the owner's phone can prove

- Each service's deep link landing on the **title**, not the app's home, when sent by explicit package.
- Whether each service publishes **title and position** into its media session on the phone.
- Flyleaf → Pulp session hand-over under the lockdown.
- The **profile-PIN** configuration actually blocking a child on each service (the attestation is the
  owner's, the test is too).
- The household file visible to, and writable by, **her** account.
- The live service's channel links and whether it can hide scores.
- The free-tier budget (§1.3) against real use for a month.

## 5. Owner actions (cannot be done from here)

Create the Streaming Availability free key, an OMDb key and a YouTube Data API key in the owner's
Cloud project (all into the vault through Pulp's settings); share the household file with the other
adult's account; PIN-lock adult profiles on the services children use; grant notification access on
her phone.

## 6. Decisions this finding needs

1. **TMDB:** accept that it is out (recommended), or read its AI clause narrowly and use it only on
   paths that never touch a model — which the clause's "in connection with" wording does not obviously
   permit.
2. **Refresh cadence:** weekly + daily changes feed + on-open (recommended, free), or keep nightly on
   the $29/mo tier.
3. **Rating reasons (D36):** drop the reasons line, or show a model-written advisory clearly labelled
   as such, or show reasons only where a source has them (rare).
4. **Person and franchise search for films (D64):** answer them through the model (it names the
   titles; the availability source resolves them; anything unresolved is dropped, Score's rule), or
   limit person search to shows (TVmaze).
5. **Sports data:** ESPN's unofficial feed (free, covers every league asked for, may break without
   notice) or TheSportsDB premium ($9/mo, licensed).
6. **Children and profiles:** the attestation route of §2.2.

---

### Sources

- TMDB API terms: https://www.themoviedb.org/api-terms-of-use · watch providers:
  https://developer.themoviedb.org/reference/movie-watch-providers
- Streaming Availability API: https://www.movieofthenight.com/about/api ·
  terms https://github.com/movieofthenight/streaming-availability-api/blob/main/TERMS.md · OpenAPI spec
  `openapi.yaml` in the same repository (schemas `show`, `streamingOption`, `change`; `/changes`)
- Watchmode: https://api.watchmode.com/
- TVmaze: https://www.tvmaze.com/api · OMDb terms: https://www.omdbapi.com/legal.htm
- TheSportsDB: https://www.thesportsdb.com/documentation · ESPN site JSON measured directly
  (`site.api.espn.com/apis/site/v2/sports/{sport}/{league}/scoreboard`, `/apis/v2/sports/football/nfl/standings`)
- Android `MediaSessionManager.getActiveSessions` — AOSP `frameworks/base`,
  `media/java/android/media/session/MediaSessionManager.java`
- Netflix help, browser support: https://help.netflix.com/en/node/2090 · Disney+ troubleshooting:
  https://help.disneyplus.com/article/disneyplus-troubleshoot-app
- YouTube embed error 153: https://developers.google.com/youtube/terms/required-minimum-functionality ·
  https://iframely.com/help/501993-you-tube-error-153-in-android-web-view
- Android TV deep-link catalogue: https://community.home-assistant.io/t/android-tv-remote-app-links-deep-linking-guide/567921
