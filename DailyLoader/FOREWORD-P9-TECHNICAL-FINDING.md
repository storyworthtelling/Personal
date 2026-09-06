# Foreword (P9) — Technical Finding

DailyLoader becomes **Foreword**, the morning hub. This is the Gate 2 deliverable: what was
found, what it means, what was decided, and what will be guarded. It follows the P7/P8 pattern
of a written finding approved before any code.

Everything below is either **verified in this session** (marked how) or **explicitly flagged as
unverified**. Where the research contradicted a Gate 1 design decision, the decision was
reversed and the reversal is recorded rather than quietly applied.

Gate 1 asked 200 design questions. Gate 2 asked 60 technical ones.

---

## 0. Executive summary

Five things changed the shape of the phase:

1. **The documents are not vector PDFs.** Every page is a 150-DPI PNG wrapped by PdfSharp. A
   measured comparison of the same five pages: **79 KB vector vs 2,671 KB raster (~34×)**.
   Moving to vector is *cheap*, not a rewrite — the composers already draw to an `SKCanvas`.
2. **`CredentialStore` derives all nine preference keys from `nameof(...)`.** Renaming a C#
   property silently changes the storage key. This is P7's `EmailSyncWorker` hazard multiplied
   by nine, and invisible at the call site.
3. **`build-android.yml` has no `setup-java`** — the exact failure PR #161 fixed on Flyleaf.
4. **The versionCode question is the riskiest line in the phase**, and the suite's own pattern
   would have bricked this app's update channel. Resolved by an owner decision to uninstall.
5. **Local news is dropped** and **Google Maps is adopted for both weather and routing** — two
   Gate 1 decisions reversed on evidence.

---

## 1. Facts — verified in this session

### The frozen surface

**F1.** The app id `com.storyworthtelling.dailyloader` has exactly **two** copies in the repo:
`DailyLoader.Android.csproj`'s `<ApplicationId>` and `DeliveryWorker`'s `[Register]` string.
(Plate had five; this needs no collapsing, only guarding.)

**F2.** `DeliveryWorker` is **already** `[Register("com.storyworthtelling.dailyloader.DeliveryWorker")]`
and `WorkName = "crossword-delivery"` already carries a comment saying it survived a previous
rename. The P7 hazard was anticipated here. It is not yet *guarded*.

**F3 — the new hazard.** `CredentialStore` stores nine values through
`Preferences.Get(nameof(Property), …)`. The C# property name **is** the storage key:

| Property | Key today |
|---|---|
| `NytSessionCookie` | `"NytSessionCookie"` |
| `NytApiKey` | `"NytApiKey"` |
| `RemarkableDeviceToken` | `"RemarkableDeviceToken"` |
| `GoogleAccountEmail` | `"GoogleAccountEmail"` |
| `AnthropicApiKey` | `"AnthropicApiKey"` |
| `CalendarMappingJson` | `"CalendarMappingJson"` |
| `VaultPassphrase` | `"VaultPassphrase"` |
| `LastDeliveryOutcome` | `"LastDeliveryOutcome"` |
| `LastDeliveryAtUtc` | `"LastDeliveryAtUtc"` |

A rename refactor moves the key and the value is silently gone. `VaultPassphrase` is the worst
case: it is deliberately the one credential *not* in the vault, so losing it locks the owner out
of restoring all the others. `MauiProgram` also uses a bare literal key `"driveFileIds"`.

**F4.** Of nine workflows, only `build-flyleaf.yml` and `build-plate.yml` pin a JDK.
`build-android.yml`, `build-ledger.yml`, `build-post.yml`, `build-storyboarder.yml` and
`build-shared-libs.yml` inherit whatever the runner provides.

**F5.** `MainActivity` lives in namespace `DailyLoader.Android.Platforms.Android` with **no**
`[Register]` name. .NET-for-Android derives its manifest component name from the namespace, so a
namespace change moves the component.

### The document pipeline

**F6.** `SkiaPageLayout` renders each page to an `SKSurface`, snapshots it to **PNG at 150 DPI**,
and `PdfVolumeComposer` wraps the images. Its own comment: *"Draws images only (no text), so no
font resolver is required."* Consequences: no text layer, no search on the tablet, and megabytes
per morning.

**F7 — measured.** Same five pages, same text, same Source Serif 4 typeface:

```
VECTOR (SKDocument.CreatePdf)          :    79.2 KB
RASTER (PNG per page @150dpi, pre-wrap):  2670.9 KB   (~34x)
```

A 25-page brief extrapolates to roughly **0.4 MB vector vs ~13 MB raster**, uploaded daily.

**F8 — read from assembly metadata** (SkiaSharp 3.119.4, not from memory):
`SKDocument.CreatePdf(Stream)`, `CreatePdf(Stream, SKDocumentPdfMetadata)`,
`BeginPage(Single, Single) → SKCanvas`, `EndPage()`, `Close()`. The composers already draw to an
`SKCanvas`, so the change is to the *sink*, not the drawing code.

**F9 — verified by inspecting the produced PDF.** Skia's PDF backend embeds and subsets fonts:
`/FontFile2` present, `BaseFont /AAAAAA+SourceSerif4-Regular`, `/Type0` + `/CIDFontType2`.
Real text, real embedded font.

**F10 — verified end to end.** `PdfReader.Open()` reads the Skia-produced PDF, pages merge into a
new `PdfDocument`, and `page.AddDocumentLink()` writes a real annotation
(`/Subtype/Link /Rect[500 40 590 64]`). Output: 10 pages, 160 KB, fonts still embedded. The whole
chain — Skia vector → PdfSharp merge → internal links — works.

**F11.** The composers use **zero** `PaperPalette`. Every colour is `SKColors.Gray`,
`SKColors.LightGray`, `SKColors.Black`, plus two raw `SKColor` RGB fills (`AllDayFill`,
`TimedFill`). Ground is hardcoded white. The `Storyworthtelling.Paper` Document-profile
machinery (`PaperPalette.Resolve`, `DeviceColorProfile.PaperPro/Rm2/Screen`,
`DeliveryProfilePolicy`) already exists and is entirely unused by this app.

### The two named rendering bugs

**F12 — overlaps.** `CalendarPdfComposer.DrawEventBox` always draws at full column width with an
opaque fill and performs no collision detection. Two events at the same time in the same column:
the later-drawn paints over the earlier.

**F13 — work-block overpaint, same root cause, worse symptom.**
`WorkScheduleAugmenter.Augment` does `result.ToList()` and then `result.Add(workBlock)` — the
synthetic 9–5 block is **appended after** the real events, so it is drawn **last**, as an opaque
box over every meeting inside it. This is the owner's "work meetings within the work block do not
show."

### External sources — live-verified

**F14.** xkcd JSON is keyless. Live response confirms the fields: `num`, `alt` (the hover text),
`img`, `title`, `safe_title`, `day`/`month`/`year`, `transcript`.
Endpoints `https://xkcd.com/info.0.json` and `https://xkcd.com/{num}/info.0.json`.

**F15.** Wikipedia `https://en.wikipedia.org/api/rest_v1/feed/onthisday/…` is keyless and
responds. Suitable for the This-Day-in-History factoid.

**F16.** Open-Meteo is keyless with hourly data, free **for non-commercial use** (personal use
qualifies).

**F17 — blocked.** `https://www.trentonian.com/feed/` returns **HTTP 403** (bot-blocked at the
CDN). "The Hamiltonian" does not resolve to a findable publication with a feed.

**F18 — Google Maps Platform.** Both Routes and Weather **require a billing account**. Since
March 2025 the $200 credit was replaced by per-SKU monthly free thresholds: **10,000 calls for
Essentials, 5,000 Pro, 1,000 Enterprise**. Weather is 10,000 free/month then $0.15/1,000.
Foreword's realistic volume: routing ~150/month (**~1.5% of the free tier**), weather ~60/month
(**<1%**). Neither costs money at this scale.

**F19 — keyless routing, live-verified.** OSRM (`routing.openstreetmap.de`) returned Trenton →
Princeton as `duration 1374.4 s, distance 21354.3 m`. Valhalla's FOSSGIS server also responded.
Both are fair-use demo servers with no SLA; Valhalla asks published apps to send `X-Client-Id`.

**F20 — reMarkable honours PDF hyperlinks**, activated by **finger, not the pen**. So drawn
section tabs backed by link annotations will work on the device.

**F21.** The `Storyworthtelling.Remarkable` library parses only `sub` and `exp` from the JWT and
exposes no device model. There is **no known way to ask the cloud whether the tablet is a colour
Paper Pro or a grayscale rM2.**

### The full-article failure

**F22.** The on-device path is `WebViewPageFetcher`: an Android `WebView` with the `NYT-S` cookie,
JavaScript enabled, 60-second timeout. `PlaywrightPageFetcher` exists in Infrastructure but is
**not** wired on Android (`MauiProgram` registers the WebView one) and is `Compile Remove`d for
the Android TFM.

**F23 — the most likely cause.** `NytArticleClient` looks for the article body at **three
hardcoded paths** inside `__NEXT_DATA__`:
`props.pageProps.initialData.data.article.{body,sprinkledBody,bodyWithSponsorship}`.
If NYT changes its Next.js shape, all three miss, and `CrosswordDeliveryService` silently falls
back to the one-paragraph abstract with nothing on the page saying so.

**F24.** Articles are fetched **sequentially** in a `foreach`, each with a 60s timeout — eight
articles is up to eight minutes of WebView work.

### The update channel

**F25 — verified in `UpdateScope.Decide`.** The install rule is a single numeric comparison:

```csharp
if (!installedVersionCodes.TryGetValue(candidate.Package, out var installed)
    || candidate.VersionCode > installed)
```

with the comment *"Older is never installed — the channel does not downgrade, whatever a release
happens to contain."* `InstallCandidate` is `record InstallCandidate(string Package, long VersionCode)`.

**F26 — the real numbers.** The DailyLoader APK in the `apps-latest` release was built by
**run_number 137** on 2026-09-04 21:43–21:49 UTC. Under the current csproj scheme
(`{lastYearDigit}{MMddHHmm}`) its versionCode is therefore **≈609,042,14x**. The provable ceiling
of that scheme for any 2026 build is `6-12-31-23-59` = **612,312,359**.

**F27 — the suite's pattern would brick it.** Flyleaf/Post/Plate use
`VERSION_CODE=$((floor + run_number))` — 100000 or 200000 — injected by the workflow, with the
csproj holding a placeholder `ApplicationVersion=1`. Applying that here yields **200,137**, a
**3,000× drop** from 609 million. `UpdateScope` would skip every future Foreword APK silently and
Android would refuse the install. This is exactly the hazard the phase brief named.

**F28 — the timestamp scheme dies in 2030.** The single year digit means 2026 → `609060040` but
2030 → `001010000`. Monotonic only within a decade.

**F29 — versionCodes are per-package.** Confirmed by `build-plate.yml`'s own comment:
*"versionCodes are per-package, so sharing the number is harmless."* Each app need only exceed its
own installed code.

**F30 — APK weight.** 47.9 MB, which is unremarkable in this suite (Score 37 MB, Ledger 43 MB,
Post 45 MB, Storyboarder 60 MB, Plate 75 MB). Playwright is **already** excluded from the Android
TFM, so it is not a contributor. **Not verified:** a per-section breakdown — the release asset
download is blocked from this environment (HTTP 403 through the proxy). Reported, not diagnosed.

---

## 2. Risks

**R1 — a vision misread can delete a real calendar event.** Gate 1 (Q145–147) put handwriting
corrections on paper: striking a line deletes the event it created, and a rewrite replaces it.
The owner accepted this explicitly — *"it's my calendar and my pen"* — and declined both a
safety net and a restriction to Foreword-created events. **Accepted risk, recorded in the owner's
words.**

**R2 — the merged document's title carries no year.** "Friday, September 5" repeats annually.
Safe only because documents are trashed daily. If trashing ever stops working (see R3), a
year-old document could be matched as yesterday's.

**R3 — trashing yesterday's documents is currently broken** (owner-reported, bug B6). Cause not
yet diagnosed. It is the precondition that makes R2 safe, so it should be fixed before or with the
title change.

**R4 — one morning of read-back will be lost** at the document merge. The new code looks for a
date-named document; the tablet holds "Daily Calendar …" and "Daily Checklist …". Owner accepted
this (Q157).

**R5 — the free routing servers have no SLA** and Valhalla asks for an `X-Client-Id`. Moot given
D12 chose Google Maps, but recorded in case that reverses.

**R6 — Ashley's rM2 renders a colour document in grayscale.** D14 ships one colour document with
no device detection (F21). DESIGN.md requires every role to carry a non-colour cue so it survives
desaturation, but the owner chose to **trust the bible rather than guard it** (T47). If a
colour-only distinction slips in, her rendering degrades and nothing catches it.

**R7 — `Result.InvokeRetry()` on a `PeriodicWorkRequest` has no natural stopping point.** It backs
off within the period, so an unbounded retry could run all day. Mitigated by D9's attempt count.

**R8 — the vault restore is fire-and-forget with a swallowed exception** in
`MainActivity.OnCreate`. On the clean install this phase now depends on (D18), that is the one
path that matters.

**R9 — the smart-task rule is parsed by an LLM.** A misparse could schedule the wrong thing.
Mitigated by D16 (show what it understood; not saved until confirmed).

**R10 — nothing in this phase has met a real tablet.** The reMarkable folder/collection format
remains reconstructed-not-verified (which is why delivery stays at the root, D13). PDF link
annotations are verified as *produced* (F10) and *supported by the device* (F20), but the two have
not been proven together on the actual Paper Pro.

---

## 3. Decisions

Numbered decisions with the reasoning. **Reversals are marked.**

**D1 — Freeze the preference keys, guard them, and migrate on read.** Replace `nameof(X)` with
explicit string constants in one frozen table; a guard asserts the exact nine key strings; reads
fall back to the old key once so even a mistake is recoverable. (T1)

**D2 — Pin JDK 21 in every workflow**, not just this one. The gap in F4 spans four other shipping
apps and the reason is in front of us now. (T2)

**D3 — Move the documents to real vector text.** Justified by F7–F10: 34× smaller, searchable,
sharper on e-ink, and the existing draw calls survive unchanged. (T3)

**D4 — The vector sink lands in the shared `Storyworthtelling.Paper` library, and Folio switches
to it too.** One composer path for the suite rather than DailyLoader diverging. (T5)

**D5 — Images are drawn onto vector pages.** The comic and article photos keep working; text
stays text. (T6)

**D6 — Section tabs are PdfSharp link annotations** over drawn tab thumbs, added at wrap time.
(T4, F10, F20)

**D7 — Full rename to `Foreword.*`** — projects, namespaces, folder — matching Flyleaf/Score/
Post/Plate. The frozen strings stay literal. (T9)

**D8 — `MainActivity` gets a pinned `[Register]` name**, frozen and guarded, so the namespace can
move without changing the component. (T10)

**D9 — Retry is bounded by an attempt count, not a clock.** (T17)

**D10 — Storage moves to EF Core + SQLite**, the repo-prescribed data layer, with a fresh
`InitialCreate` baseline — Foreword has never had a database, so there is genuinely nothing to
migrate from. `Preferences` keeps credentials and simple settings. (T13, T18)

**D11 — REVERSAL: local news is dropped.** Gate 1 (Q169, Q175) named The Hamiltonian and The
Trenton Times. F17 shows one is bot-blocked and the other unfindable. The owner chose to drop the
feature rather than fight the sources. Newsletters and sports feeds remain.

**D12 — REVERSAL of a reversal: Google Maps for both weather and routing.** Gate 1 preferred
Maps (Q75); on learning billing was required the owner moved to keyless (T7); on seeing F18's
actual numbers — ~1.5% and <1% of a free tier that Weather requires an account for anyway — the
owner moved back. One billing account, one key in the **shared vault**. (T19, T25, T26)

**D13 — reMarkable delivery stays at the root.** The folder/collection format has been
reconstructed-not-verified since Phase 4 and #12's fix was to fall back to root. Not worth risking
a daily delivery on. (Gate 1 Q93)

**D14 — Ship one colour document; no device detection.** F21 shows the API cannot tell us the
model, and each install is one person with one tablet. (T34)

**D15 — Prayer privacy is enforced by types plus a guard.** Prayer content lives in a type the
three public composers cannot accept, and a guard — written first and watched to fail — proves
prayer text never reaches a public prompt. Four separate Claude calls keep the isolation
structural. (T35, Gate 1 Q109/Q115)

**D16 — A parsed smart-task rule is shown back as a sentence and is not saved until confirmed.**
(T48)

**D17 — Handwriting extraction sends only the writable pages, with a region map** of what each
region is, since Foreword drew them and knows the coordinates. (T36)

**D18 — OWNER DECISION: DailyLoader will be uninstalled**, so Foreword starts clean at
**major 2**. This resolves the versionCode conflict (F26/F27) without a per-app exception.
Consequences stated and accepted:
  - All nine `Preferences` values are wiped, including `VaultPassphrase`.
  - The vault restores NYT cookie/key, reMarkable token and Anthropic key **once the passphrase
    is re-entered**; the Google account is re-picked.
  - **Release A no longer proves an in-place upgrade**, because there isn't one. Its acceptance
    test becomes: installs clean, the periodic worker schedules and fires on its own, and the
    vault restores. The freezes still earn their place — they protect every *future* upgrade
    (T60), and this is the last clean install.

**D19 — Unified versionCode formula across the suite:**

```
versionCode = major x 10^8  +  minor x 10^6  +  build x 10^3  +  revision
versionName = "major.minor.build.revision"
```

Budgets: major ≤ 21 (int32), minor ≤ 99, build ≤ 999, revision ≤ 999; bump minor when build
passes 999. Every app starts at **major 2**. CI computes all four parts. This encodes the owner's
own compare-major-then-minor-then-build model into the single integer Android actually compares
(F25) — Android's `versionName` is a display string nothing compares, and `versionCode` is one
32-bit int. **The unification ships as its own PR, separate from P9** (T54), trusting the
run_number history for the other apps' floors (T55).

**D20 — First run asks for the vault passphrase first**, and entering it restores everything else
— one field instead of six. The vault restore becomes **visible on first run only**, quiet
thereafter. (T58, T59)

**D21 — Article extraction: instrument before changing.** Do not touch the three `__NEXT_DATA__`
paths yet. First record and surface *why* each article fell back — no `__NEXT_DATA__`, paths
missed, paywalled, timed out — then fix what the numbers actually say. (T11)

**D22 — Article fetch stays serial.** One WebView at a time is the safe thing on Android; the
morning has time. (T12)

**D23 — RSS parsing uses `System.ServiceModel.Syndication`** (Microsoft, handles RSS and Atom),
which clears the dependency bar. (T32)

**D24 — Route every document colour through `PaperPalette`'s Document profile, with a leak
guard** that fails if a composer defines its own colour. (Gate 1 Q81, F11)

**D25 — The shared MAUI kit is a full component library, built to be taken by Folio (P10) and
Index (P11), shipped as its own release.** Pure logic unit-tested; visuals compile-checked in CI
and verified on device. (Gate 1 Q161–163, T16)

**D26 — Post ↔ Foreword handoff uses the Plate→Index pattern** (explicit intent, named receiver,
signature-level permission) — **but the signature permission is verified across a MAUI app and a
native head before two features are designed on it.** (T15)

**D27 — Geocode once, cache forever.** Each distinct location resolves once and is stored; the
owner goes to a handful of places, so after a week it barely calls out. (T21)

**D28 — Quota exhaustion is an ordinary piece failure, not a credential flag.** The existing rule
— only auth rejections reveal a key field — stays intact. (T27)

**D29 — The reading taste profile is stored as Drive JSON**, following Score's
`DriveTasteProfileStore`; raw vetoes stay on-device. It **ranks, never excludes**. (T28, Gate 1
Q188)

---

## 4. Guards

Every guard is **written first and watched to fail**, with the reintroduced bug named in the
commit message. The owner selected all four reintroductions (T38).

| # | Guard | The bug reintroduced to prove it |
|---|---|---|
| **G1** | The nine preference key strings are exactly the frozen table | Rename `NytApiKey` → watch the key guard fail |
| **G2** | No workflow passes `-p:ApplicationVersion` for this app; the csproj/CI split is as decided | Add an override to the workflow → watch it fail |
| **G3** | Prayer content cannot reach a public composer's prompt | Feed prayer text to the brief composer → watch it fail |
| **G4** | No two event boxes overlap; every event inside a work block is drawn | Already red today (F12/F13) — kept failing until fixed |
| **G5** | App id matches in both places (csproj + `[Register]`) | — |
| **G6** | `DeliveryWorker`'s registered name and `WorkName` are unchanged | — |
| **G7** | `MainActivity`'s pinned `[Register]` name is unchanged | — |
| **G8** | versionCode exceeds the recorded floor and stays under int32 | — |
| **G9** | No colour hex or type-size literal outside the token files; no composer defines its own colour | — |
| **G10** | Every workflow pins JDK 21 | — |

**CI checks** (the ICON CHECK pattern — read it back out of the artefact, don't assert intent):
- **VERSION CHECK** — extract the versionCode from the built APK and fail below the floor (T48).
- **APP ID CHECK** — read the app id back out of the APK.
- **Rendered-document artefact** — every PR touching a composer attaches the rendered PDF, so a
  visual regression is reviewable rather than merely green (Gate 1 Q193).

---

## 5. Release breakdown

Small releases, each verifiable on the device before the next (Gate 1 Q189). Each is its own PR
off `main`; none merges until its CI run is green.

| Release | Contents | Proves |
|---|---|---|
| **Pre-A** | Suite-wide versionCode unification (D19), separate PR | The formula, on apps that already work |
| **A — Guardrails** | Rename to `Foreword.*`, pinned `MainActivity`, frozen prefs + migrate-on-read, versionCode major 2, JDK 21 everywhere, all guards, painted icon + label | Clean install; the worker schedules **and fires on its own**; the vault restores from the passphrase |
| **B — The shared MAUI kit** | `Storyworthtelling.Maui`: tab strip, dog-ear, status line, list rows/accordion | The kit stands alone before features lean on it |
| **C — Vector documents** | Vector sink in `Storyworthtelling.Paper`, `PaperPalette` routing + leak guard, Folio switched over | Documents render as text, smaller, on the tablet |
| **D — The calendar page** | Overlap splitting, work block as a container, overflow list, mapping screen, columns as data, per-event overrides | The owner's **top priority** (Gate 1 Q200) |
| **E — The Day document** | Merge checklist in, weather page, outlook, write-space, section tabs with link annotations, date naming | The merged working document |
| **F — Tasks** | Smart tasks, standing items, weather/daylight/season/work-day rules, Google Tasks mirror, away pause | The lawn moves itself |
| **G — The brief** | Voiced opening, xkcd, This-Day factoid, article instrumentation (D21), dedupe, feeds, section order | The reading |
| **H — Prayer** | Prayer document, private Drive transcription, type-level isolation + guard | The fourth seat |
| **I — Ashley's install** | Second install against her credentials and tablet | Nothing is hardcoded to one person |
| **Score refit** | Per-area colour coding, separate PR (Gate 1 Q90) | The bible and the code agreeing |

---

## 6. Deliberately not built

Recorded so that growing one later is a decision rather than a drift.

**N1 — Local news.** Dropped on evidence (F17, D11), not deferred.

**N2 — reMarkable folder delivery.** Root only. The collection wire format stays unproven and a
daily delivery is not the place to test it (D13).

**N3 — Device detection for the render profile.** No API exposes it (F21). One colour document.

**N4 — Re-teaching the handwriting reader.** The owner explicitly rejected claiming a capability
that does not exist: Claude vision has no memory between calls, so a corrections-as-examples
mechanism was declined rather than dressed up (Gate 1 Q142).

**N5 — A correction queue in the app.** Corrections are a paper act on the next day's page
(Gate 1 Q141). "Nothing waiting when I open it" survives intact.

**N6 — A tick affordance anywhere in Foreword.** The phone composes the checklist; only the
reMarkable closes a task. This is an invariant, not an omission (Gate 1 Q12).

**N7 — Prefetching the morning the night before.** It is a morning job (Gate 1 Q132).

**N8 — Story continuity across mornings.** Each morning stands alone (Gate 1 Q131).

**N9 — A safety net on handwriting-triggered deletion.** Offered and declined (R1).

---

## 7. What is not verified

Stated plainly, per the standing constraint. "Built and unit-tested against fakes" is not "works."

1. **Nothing in this phase has run on the tablet.** The link-annotation tabs are proven produced
   (F10) and proven supported (F20), but not proven together on the Paper Pro.
2. **The reMarkable folder format** remains reconstructed since Phase 4.
3. **The bonus-puzzle bug** (owner: "it arrives but it's wrong") is not yet diagnosed.
4. **The trashing bug** (B6) is not yet diagnosed.
5. **The exact installed versionCode** is inferred from build timestamps (F26), not read from the
   APK — the release asset download is blocked from this environment (F30). Moot under D18's
   uninstall, but recorded.
6. **The APK size breakdown** could not be produced for the same reason.
7. **The Google Maps signature permission across a MAUI app and a native head** is unproven
   (D26).
8. **The full-article root cause** is a strong hypothesis (F23), not a confirmed diagnosis — which
   is exactly why D21 instruments before changing anything.

---

## 8. The six bugs this phase must fix

Named by the owner in Gate 1, not inherited assumptions.

| # | Bug | Status |
|---|---|---|
| **B1** | Full articles unreliable; abstract fallback is silent and unmarked | Cause hypothesised (F23); D21 instruments first |
| **B2** | Event overlaps do not render | Root cause found (F12) |
| **B3** | Work meetings inside the work block do not render | Root cause found (F13) |
| **B4** | Calendar mapping is hand-edited JSON needing a rebuild | Becomes a screen (Release D) |
| **B5** | Monthly bonus puzzle "arrives but it's wrong" | Not yet diagnosed |
| **B6** | Yesterday's documents are not being trashed | Not yet diagnosed |
