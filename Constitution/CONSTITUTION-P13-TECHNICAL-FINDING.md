# Constitution (P13) — Gate 2 technical finding

**Status:** accepted for planning. The owner delegated the review and answered Q1–Q7, recorded as
D80–D87 in `CONSTITUTION-P13-DESIGN-NOTES.md` (§8 gives each answer). It sits between the approved design
book and the plan: the book says what Constitution looks like (D1–D79 in
`CONSTITUTION-P13-DESIGN-NOTES.md`), and this file says what the code, the platform and the
providers allow, what has to be built that the book assumes already exists, and what only the
owner's phone can prove. Every fact below was read from this repository's own source, the platform's
own reference assembly, or the provider's own page on **2026-10-17**; paths and links are given.

**Nothing here names the household's real people, devices, readings or accounts.** Where a finding
concerns them it records the shape. Examples use the design book's invented household.

Sources are cited as **brief §** (the UX design brief), **book p.N** (the approved design book, page
N), **Dn** (a design-notes decision) and file paths.

---

## 0. The answer in one page

1. **Native, not MAUI.** Constitution should be a native .NET 10 Android head over
   `Codex.Ui.Android`, Pulp's route. The scrapbook screens (crinkled ground, taped scraps at a tilt,
   clippings, index cards, chart paper — D41–D48) are Canvas painting, the same work Flyleaf's
   bookcase already is; the native kit already carries the `DogEar`, `WaitingActivity`,
   `ActionCard`, `BinderTabs`, `Checkbox`, `AccordionPicker`, `PaperUi.Segmented` and the voice
   greeting; and **Health Connect is a platform API in `Mono.Android`** (§1.3), so the head talks to
   it directly with no binding package. §1.1.
2. **Health Connect needs no new dependency.** `Android.Health.Connect.HealthConnectManager`
   (API 34+) is in the `Microsoft.Android.Ref.36` reference assembly with Java-friendly
   `IOutcomeReceiver` callbacks, and every record type Body charts is there (weight, body fat,
   resting HR, HRV-RMSSD, steps, sleep sessions, active calories, exercise). Microsoft also publishes
   `Xamarin.AndroidX.Health.Connect.ConnectClient` 1.1.0.4, but its API is Kotlin `suspend`
   functions, which bind awkwardly; the platform API is the better fit. §1.3.
3. **Google Keep cannot be written from a personal account.** The brief puts groceries on "the
   Keep list … Constitution adds and removes only its own items" (brief, Meal planning). The Keep
   API is for **enterprise administrators** (domain-wide delegation) and has **no update method** —
   notes can be created, listed and deleted, not edited item by item. **Needs an owner decision**
   (Q1). §1.6.
4. **NYT Cooking forbids exactly what the planner does.** A recipe page carries a complete
   schema.org `Recipe` block (ingredients and steps), readable even signed out, so a pasted link
   *can* be read. But cooking.nytimes.com's own robots.txt forbids automated data-mining and "the
   development of any software, machine learning, artificial intelligence (AI), and/or large
   language models", disallows `/search` and `/recipe-box`, and blocks `anthropic-ai`. A weekly
   Claude-chosen "stretch" recipe needs search, and the planner and taste profile feed the recipe
   to Claude. **Needs an owner decision** (Q2), the same kind Pulp made about IMDb (Pulp D205).
   §1.7.
5. **The Flyleaf bookmark is driven by posted Android notifications**, so a bookmark without a
   push needs a new contract. `TileNotificationListener` reads other apps' *posted notifications*,
   and `BookshelfView.DrawBookmark` colours the ribbon from the channel's importance (high → Problem
   rose, low → Resolved grey, otherwise → In-progress teal). Earning a bookmark today means posting
   a notification, which the brief forbids ("Nothing is ever pushed"). Its colours also can't show
   the **Reference slate** that page 19 draws. **Needs an owner decision** (Q3). §1.9.
6. **Two of the approved tab cloths are role colours.** The Workouts tab (D40, D45) is exactly the
   **Problem** mark `#9E434B`, and the Body tab is exactly the **Action** mark `#38734A`. So a Problem
   fold waiting on the Workouts page, and the undo fold in Action pine, would wear their own tab's
   colour. That is the one thing `AreaHueTests` forbids. **Needs an owner decision** (Q4). §1.2.
7. **Two tablets may mean two reMarkable accounts.** The vault holds one `RemarkableSecrets.DeviceToken`,
   and anything uploaded to a reMarkable account syncs to every tablet on it. If both tablets share
   one account, each adult's private workout and digest lands on the other's tablet too. If they
   don't, the partner's install needs its own token, which the shared vault has no room for.
   **Needs an owner decision** (Q5). §1.4.
8. **Plate has no recipe errand.** "I ate something else" is a plain `IMAGE_CAPTURE` errand, which
   Plate already serves (`Codex.Domain/Plate/Errands.cs`). "Scan a recipe card … through Plate's
   multi-page scan tray" (brief, book p.14) is not an errand: Plate's two read actions are
   `READ_CARD` and `READ_STATEMENT`. A third, `READ_RECIPE`, is Plate work, and the partner's phone
   needs Plate installed to scan at all (Q6). §1.8.
9. **On the locked phone, the watch and scale data may never arrive.** The Pixel Watch reaches
   Health Connect through Google's companion app, and a smart scale through its maker's app. Both
   are third-party packages, which Flyleaf's fail-closed `AppCategorizer` quarantines unless
   they're allowlisted (`PolicyContext.AllowlistedPackages` lists only GitHub, Gemini, Maps and
   reMarkable). Body on the owner's phone needs that allowlist widened. §1.10.

Everything else the book draws can be built as drawn, with the gaps listed in §2.

---

## 1. Facts, verified in this session

### 1.1 Native head over `Codex.Ui.Android` (recommendation)

| Need (source) | Native kit today | MAUI kit today |
|---|---|---|
| Binder frame, five tabs (D40, book p.11) | `BinderFrame`, `BinderTabs` | `BinderArea` |
| Dog-ear with notice + undo states (D77, book p.18) | `DogEar` + `WaitingActivity` (Index draws its own Waiting page) | `DogEar` + `DogEarState` |
| Long-press action card over a PaperDeep haze (book p.12) | `ActionCard` | — |
| Segmented Month/Quarter/Year, This week/Recipe box (book p.13, 16) | `PaperUi.Segmented` | style only |
| §7 checkbox rows (D72) | `Checkbox` | list-row style |
| Inline accordion groups in Settings (book p.17) | `AccordionPicker` | accordion picker |
| Crinkled ground, taped tilted scraps, torn edges (D41–D48) | Canvas (Flyleaf's `BookshelfView` is the precedent) | `GraphicsView` drawables |
| Health Connect (brief, Body) | `Mono.Android` platform API, direct | needs `Platforms/Android` glue |

Pulp (P12) is the last head built and the closest shape: `Pulp.Domain` (pure), `Pulp.Infrastructure`,
`Pulp.Android` (references `Codex.Domain`, `Codex.Ui.Android`, `Storyworthtelling.Voice`,
`Storyworthtelling.Updates`) and `Pulp.Tests`. Plate shows a native head can also reference
`Storyworthtelling.Paper` (`FocusPhone/Plate.Android/Plate.Android.csproj`), which Constitution needs
for its documents. **Recommendation: Constitution.Domain / .Infrastructure / .Android / .Tests on
Pulp's template.** Native is the default. Choosing MAUI would mean porting the action card and
Waiting page into the MAUI kit first.

### 1.2 Identity, tabs and materials

- **Identity ladder (D10).** 230° clears every role and sibling by ≥ 20° (book p.2), and the Flyleaf
  plate's ink inversion holds for the light cloth. `BookClothPalette.MarksInvertToInk` inverts at a
  Rec. 601 luma above 0.6, and `#82ACC1` measures **0.635**, so the shelf draws the rule and title
  in ink as page 3 and D12 require (`FocusPhone/Codex.Domain/Apps/BookClothPalette.cs`). The margin
  is thin, so a guard should pin it.
- **Tab cloths (D40, D45), measured against `PaperColorTokens`.** The book's tab CSS (book p.11–18):

  | Tab | Inactive cloth | Active ink | Is it a role token? |
  |---|---|---|---|
  | Today | `#A4C1D0` | `#15546E` | Constitution fill / text |
  | Workouts | `#9E434B` | `#82333B` | **Problem** mark / text |
  | Meals | `#383E44` | `#383E44` | not registered anywhere |
  | Body | `#38734A` | `#295E3A` | **Action** mark / text |
  | Settings | `#C0BDB9` | `#54514B` | Resolved fill / text (§9's grey) |

  Two things follow:
  - D45's **deep** inactive cloths depart from DESIGN.md §9, which says an inactive tab is cut from
    the area's **fill** tier. D45 records the choice, but DESIGN.md isn't amended yet.
  - `AreaHueTests` holds §9's restraint rule that "a page must not wear a role hue that is the
    colour of the tab it sits behind". The Problem fold (D77) on the Workouts page and the
    Action-pine undo fold anywhere under the Body tab both break it. Post's two exceptions are
    listed in that guard; see Q4.
- **Index-card tokens.** Book p.11 builds meal cards from "Index's own" stock `#FFFEF8`, head rule
  `#E4A7A2` and body rules `#D6E3EF` at a 22 dp pitch. Only `NoteCardHex` is shared
  (`Storyworthtelling.Paper/MaterialTokens.cs`); the two rules and the pitch are private to
  `Ledger/Ledger.Domain/Design/MaterialTokens.cs`. For "the same object in both apps" to be true,
  they move to the shared file and Index re-points, or Constitution keeps a parity-guarded copy.
  Moving them is the cleaner of the two.
- **The scrapbook materials are new materials.** DESIGN.md §2 says paper is "one consistent warm
  cream, same color and grain everywhere". The crinkled exam-table ground (D43), tape, magazine
  stock with halftone, the doctor's-chart grid and receipt paper (D44, D46) need registering in
  `MaterialTokens.cs` and DESIGN.md §2/§19 as a recorded departure, the way Flyleaf's bookcase is
  one (§1/§5). The notes list this as an open item. The mockups' CSS values are the starting point
  (for example tape `rgba(244,236,206,.62)` and chart grid `rgba(76,103,135,.10)`, which is the
  Reference mark at 10%).

### 1.3 Health Connect

- **The platform API is in the reference assembly.** `Microsoft.Android.Ref.36` 36.1.53 (nuget.org)
  carries `Android.Health.Connect.HealthConnectManager` with `ReadRecords(ReadRecordsRequest,
  IExecutor, IOutcomeReceiver)`, `Aggregate`, `AggregateGroupByPeriod`/`ByDuration` and
  `GetChangeLogs`/`GetChangeLogToken`. It also has the record types `WeightRecord`,
  `BodyFatRecord`, `LeanBodyMassRecord`, `RestingHeartRateRecord`,
  `HeartRateVariabilityRmssdRecord`, `HeartRateRecord`, `StepsRecord`,
  `ActiveCaloriesBurnedRecord`, `TotalCaloriesBurnedRecord`, `ExerciseSessionRecord` and
  `SleepSessionRecord`, and the permissions `ReadWeight`, `ReadHeartRateVariability`,
  `ReadSleep`, `ReadHealthDataInBackground` and `ReadHealthDataHistory`.
- **No binding package is needed.** `Xamarin.AndroidX.Health.Connect.ConnectClient` 1.1.0.4
  (Microsoft) exists and would satisfy the dependency rule, but the platform API is already in the
  box. Constitution adds no Health Connect dependency.
- **Per-signal permission (brief, Body; book p.16).** The read permissions are per record type, so
  "asked the first time its chart is opened" maps one-to-one onto the API.
- **No backfill (brief).** Without `ReadHealthDataHistory`, Health Connect returns only the 30 days
  before the first grant. The brief wants nothing before install day anyway, so the app filters on
  its own install date and never asks for history.
- **Read-only (brief).** No write permission is declared, and a guard asserts that.
- **Two platform rules that fail silently** (to verify against the platform docs during the plan's
  spike, then guard):
  1. A Health Connect client must declare the permissions-rationale / privacy-policy activity
     (`VIEW_PERMISSION_USAGE` with the `HEALTH_PERMISSIONS` category). Without it the permission
     request opens nothing. It's the same kind of trap as Post's four `ROLE_SMS` components.
  2. **Background reads** need `READ_HEALTH_DATA_IN_BACKGROUND`, which depends on the platform
     version. Without it, a WorkManager worker cannot read HRV, sleep and resting heart rate for the
     morning recovery check (brief, Workout planning: "Under-recovered … swaps the session to
     mobility"). The fallback is the last values read while the app was open.

### 1.4 reMarkable delivery and ink read-back

- **Upload, list, download and trash exist** (`Storyworthtelling.Remarkable/IRemarkableClient.cs`).
  Constitution delivers at the **root** (brief: "the tablet's root (transient)"), the only path
  Foreword has exercised. Folders stay reconstructed and unverified.
- **Read-back is a vision read of the flattened PDF**, not stroke geometry. `DownloadByTitle`
  returns the cloud-rendered PDF "annotations included when the cloud can render them", and
  `Storyworthtelling.Handwriting.ClaudeVisionHandwritingExtractor` transcribes it. Index's monthly
  worksheet is the precedent for a box-by-box read: `WorksheetRegions` (one source of rectangles
  for the painter and the reader), a fresh extractor per call because the instruction varies, and
  a **0.75 confidence threshold** below which a box is never guessed but marked for the Waiting
  page (`Ledger/Ledger.Infrastructure/Worksheet/WorksheetReadBackService.cs`). The ink targets (D14–D18)
  and the Caution confirm card (book p.18) follow that pattern exactly.
- **"Any mark is a tick" (D18) and "a reason beats a tick" (D17)** are instruction text plus a pure
  resolver in Domain. They are testable with fakes, but the model's judgement on real pen marks is
  device-only.
- **The "opened" signal (brief risk).** `RemarkableDoc` carries only id, title and bytes. The
  listing parser reads no last-opened field, and the v2 read path is marked unverified in source.
  **Plan on the 3-day fallback as the real behaviour** for trashing the digest.
- **One token, two tablets.** The shared vault has one `RemarkableSecrets.DeviceToken`
  (`Storyworthtelling.Voice/Secrets/SecretsVault.cs`), and pairing in Foreword or Constitution
  restores it in the other (D75). That covers the owner's tablet. The partner's tablet either:
  - shares the account, in which case every document reaches both tablets (Q5), or
  - has its own account, which needs a per-install token. The token goes in each person's
    `PersonalVault`, not the shared vault; Foreword Release I's lesson is that a per-install value
    in the shared vault is restored onto the wrong phone.
- **Page size (D13).** 445 × 594 pt matches the **reMarkable 2**'s glass (1872 × 1404 px at 226 ppi
  = 596 × 447 pt). The **Paper Pro**'s glass is larger (2160 × 1620 px at 229 ppi ≈ 679 × 509 pt),
  so the same page shows about 1.14× larger there: a 28 pt box is about 11.3 mm. That still matches
  the 3:4 shape, so nothing is cropped. "A point on the page is a point on the glass" is exact only
  on the rM2. That isn't a problem, just worth knowing for the ink-target sizes.
- **Morning timing.** The brief says ink is read during "Foreword's morning run". Index reads its own
  worksheet in its own worker, and nothing in Foreword knows Constitution's documents. The
  recommended reading is a Constitution worker scheduled just after Foreword's delivery window,
  rather than Foreword reading another app's pages (Q7).
- **Delivery times.** A recipe "an hour before the cook block" and a workout re-sent "if the forecast
  flips, but only before the slot" are WorkManager jobs with an initial delay, accurate to minutes.
  Exact alarms (`SCHEDULE_EXACT_ALARM`) are denied by default to new apps on Android 14 and aren't
  needed for "about an hour before".

### 1.5 Documents (`Storyworthtelling.Paper`)

- **`VectorPdfSink.BeginPage(width, height)` takes any page size**, so 445 × 594 needs nothing new.
  `PaperTypeScale` is the printed ladder: Display 33, Title 21, Heading 13.5, Body 12, Secondary
  10.5, Label 9.75, Mono 10.5.
- **The dose at 54 pt (book p.5) is on no rung.** The guard that no composer picks its own size
  will fail on it, so the plan registers a rung for it in `PaperTypeScale` (a "Dose" rung, recorded
  in DESIGN.md §19) rather than writing a literal. The step times at the Title rung in Cascadia
  (book p.8) are already on the ladder.
- **Greyscale (brief, Design rules).** `DeliveryProfilePolicy.ForRemarkable(colorDevice)` already
  returns the Document profile for each device, and every target is drawn muted with non-colour cues
  (book p.4). The brief asks for **one render** for both tablets. The meal sheet is identical on
  both (book p.7), so one Document-greyscale render is enough, and the header rule `#476F83` greys
  out harmlessly.
- **The health-poster illustrations (D20, D21)** are generated at run time with Gemini (the vault's
  `Gemini.ApiKey`) and checked by a Claude vision pass. Being generated from exercise descriptions,
  they hold no household data, but they are **runtime** art: cached in app storage and backed up to
  Drive, never committed.

### 1.6 Calendar, weather, groceries

- **Calendar.** Constitution writes to each person's **primary** calendar (brief, Calendar), which
  needs the full `calendar` scope (`Storyworthtelling.Google/GoogleScopes.Calendar`; Foreword
  already asks for it). Pulp's narrower `calendar.app.created` only covers a calendar the app
  created. Pulp's extended-property key that never includes the date is the pattern for "a hidden
  key so Constitution can find it again". **Work hours (brief)** are readable from the
  `PersonalVault.WorkScheduleJson` that Foreword's `VaultSync` publishes.
- **Weather.** `Storyworthtelling.Google.GoogleWeatherClient` reads **current conditions only**. The
  bad-weather swap (brief) needs the hourly forecast for the slot, and "poor air" needs Google's Air
  Quality API. Both use the same Maps/Weather key, as extensions to the shared library.
- **Groceries: Keep is out.** The Keep API overview
  (developers.google.com/workspace/keep/api/guides) says it is "used to create apps allowing
  enterprise administrators to manage Google Keep notes", and authorises through domain-wide
  delegation. The `notes` resource has create, get, list and delete, but **no update and no item
  add or remove**. A consumer account can't use it, and even an enterprise one couldn't keep "only
  its own items" on a shared list. The **Google Tasks API** can: Foreword already holds the `tasks`
  scope, a "Groceries" task list per household works on consumer accounts, items are added and
  removed singly, and a completed item reads back as "bought" (brief: "ticked = bought = in
  stock"). Q1 has the options.

### 1.7 NYT Cooking

- **A pasted link is readable.** A signed-out GET of a public recipe page returned 200 with a
  schema.org `Recipe` JSON-LD block containing `recipeIngredient` and `recipeInstructions` (and
  `isAccessibleForFree: false`).
- **The terms say no.** robots.txt at cooking.nytimes.com, read today, states that content is for
  personal, non-commercial use, and that "use of any device, tool, or process designed to data mine
  or scrape the content using automated means is prohibited without prior written permission". It
  names "(2) the development of any software, machine learning, artificial intelligence (AI),
  and/or large language models (LLMs)" among the prohibited uses. It disallows `/search` and
  `/recipe-box`, and gives `User-agent: anthropic-ai` a full `Disallow: /`.
- **No public API** covers search or a recipe box (brief risk, confirmed: none found).
- **What this does to the brief:**
  - The weekly **stretch recipe chosen by the planner** needs search, which is disallowed.
  - A **pasted link** is the owner fetching one page they're entitled to, but its text would then
    be given to Claude, for the taste profile, planning and step re-timing. That is the "AI" use the
    terms name.
  - `NytSecrets.SessionCookie` already holds the household's NYT session (Foreword uses it for the
    crossword), so authentication isn't the obstacle; the terms are. See Q2.

### 1.8 Plate errands

- **Photo errand: exists.** `Errands.For` maps `IMAGE_CAPTURE` to `Errand.Photo`, which returns to
  the caller (`HasAWaitingCaller`). "I ate something else" (D65–D67) aims it at Plate's package
  explicitly, so the locked phone can't route it elsewhere.
- **Recipe scan: missing.** Plate's inbound reads are `READ_CARD` and `READ_STATEMENT`
  (`Codex.Domain/Plate/ReadRequest.cs`), and the multi-page `ScanTray` only delivers to Drive, Post
  or the reMarkable. Constitution needs a `READ_RECIPE` action that scans 1–N pages through the tray
  and returns the page images to the caller transiently. The **uncertain words with two
  alternatives** (book p.14) come from a Claude vision read in Constitution, not ML Kit, which gives
  a confidence but no alternatives. The images are dropped after the read (brief: "Only the text is
  kept; the image is not"). That is Plate work and a frozen contract, and a guard should hold both
  sides equal.
- **The partner's phone.** The brief has her scanning recipes and sending deviation photos from an
  unrestricted phone. Without Plate installed there, the photo can fall back to any camera's
  `IMAGE_CAPTURE`, but the recipe scan has no route (Q6).

### 1.9 Flyleaf: the shelf and the bookmark

- **The shelf (D78, D79).** Each wrapped row of `BookshelfLayout.PlaceBookRows` already gets its own
  ledge rectangle, so "every row stands on its own full ledge" is a painter change, as page 19
  says. What Constitution's book needs:
  - `PolicyContext.ConstitutionPackage`;
  - a `CoverResources` entry in `HomeActivity` (`cover_constitution`; the code comment records
    that a missing entry silently shows the launcher icon instead, as happened to Foreword and
    Folio);
  - a `BookClothPalette` binding to `DesignTokens.Identity.Constitution.Cloth`;
  - the existing `FrozenInvariantsTests` cover guard extended to it.
- **The bookmark is drawn at the spine edge already**: `DrawBookmark` anchors at `r.Left` ("the
  book's left edge … as the spine"), matching D79.
- **It is lit by posted notifications.** `TileNotificationListener` broadcasts each posting package's
  highest channel importance, and `NotificationHue` maps 2 → Problem, 0 → Resolved, anything else →
  In-progress. To earn the brief's bookmarks (a waiting scan, the box under ten, three errors),
  Constitution would have to post notifications:
  - on the locked phone the status-bar disable mask suppresses their alerts;
  - on the partner's unrestricted phone they would show in her shade, which is the push the brief
    forbids.

  Constitution can also never ask for the **Reference slate** that page 19 draws. Q3 has the
  options.
- **A colour disagreement inside the book.** Page 18 and D77 colour a waiting scan **In-progress
  teal**, while page 19 draws the same notice's bookmark in **Reference slate**. Q3 settles both.

### 1.10 The lockdown and Body's data sources

- `AppCategorizer` clears first-party packages by the `com.storyworthtelling.` prefix, so Constitution
  itself is safe. It quarantines Unknown- or Mature-rated third-party apps unless they're in
  `PolicyContext.AllowlistedPackages`, which lists GitHub, Gemini, Maps and reMarkable
  (`FocusPhone/Codex.Domain/Policy/PolicyContext.cs`). `CategoryCatalog` seeds Fitbit's *shelf
  placement* only, not its clearance.
- Body's sources write to Health Connect **through their own apps**: the watch through Google's
  companion/Fitbit app, the scale through its maker's app. If those are suspended, Health Connect
  is silent and the charts wait forever. So the allowlist grows by those packages; their exact ids
  are owner and device facts.
- Health Connect's own permission UI is a platform module on Android 14+. Whether the policy pass
  ever touches it is device-only.

### 1.11 Vault, Drive and the household

- **Shared vault.** A new `SecretsVault.Constitution` section, pinned with
  `[JsonPropertyName("Constitution")]` (Foreword's and Pulp's lesson), holds the household
  **Sleep Number login** (brief: "one household SleepIQ login in the shared vault"). NYT reuses the
  existing `Nyt` section rather than a second copy.
- **Personal vault.** **Health Connect choices are per-install facts**, so despite D75's list they
  belong in each person's `PersonalVault`, not the shared vault. Restoring the owner's choices onto
  the partner's phone would be Foreword Release I's bug again. The same goes for a per-install
  reMarkable token if Q5 says two accounts.
- **The household file.** The brief wants "one shared, encrypted household file … both installs read
  and write". The writes are split:
  - the owner's install plans (D74);
  - the partner's install writes scans, "Cook this soon" and its own deviation estimates, and takes
    over planning while the owner's install is down.

  That is Pulp's merge-safe household file exactly: `HybridLogicalClock`, records with tombstones,
  `VaultEnvelope`, and **never write over a file that cannot be decrypted** (Pulp G14), in
  `Pulp/Pulp.Domain/Household/`. Constitution needs the same machinery. The choice is between
  extracting it into a shared library (touching Pulp under its own frozen guards) and a second copy
  held equal by a parity guard. The plan should take the extraction. **Taking over planning** needs
  a lease record (who plans, with a heartbeat), so two installs never plan the same week.
- **Body's backup** is a separate encrypted personal Drive file per adult (brief: "kept forever …
  encrypted personal Drive backup"). It is `VaultEnvelope` through `IDriveTextFile`, and never
  written over a file it can't decrypt.

### 1.12 Score, Index and the voice

- **Score.** `Codex.Domain/Music/TempoPool.cs` holds `TempoBands`: Easy ≤ 125, Steady ≤ 150, Hard
  above, a 90–180 dial, and `Names` = Easy / Steady / Hard. Constitution references `Codex.Domain`
  (as Pulp does) and prints `TempoBands.Of(bpm)`'s name beside the number (D25). **No cross-app
  contract is needed**: the owner sets Score's dial by hand. A guard pins the printed band name to
  `TempoBands.Of`.
- **Index → Constitution grocery budget (brief: "Index sends the grocery budget and actual spend
  over a signed broadcast").** This is new, and the first contract where Index is the **sender**.
  It follows G16's shape:
  - explicit component `com.storyworthtelling.constitution.BudgetReceiver`;
  - signature permission `com.storyworthtelling.constitution.RECEIVE_BUDGET`;
  - payload *month, budget, spent so far* as exact decimal text (Post → Index's rule).

  Index already maps Plaid's `FOOD_AND_DRINK_GROCERIES` to its Groceries category
  (`Ledger/Ledger.Domain/Model/PlaidCategoryMap.cs`). Only the owner's install plans, so only it
  needs the broadcast.
- **Voice.** `Storyworthtelling.Voice.VoicedMessenger` composes in the owner's voice and degrades to
  a neutral fallback, never throwing. That is exactly the six seats' rule "never hidden" (book
  p.18). It differs from Index, whose seats hide. A closed `VoicedOccasions` list with its guard
  (Folio's pattern) keeps the seats at six and errors out of them.

### 1.13 Version band and frozen identity

- Bands in use (`Storyworthtelling.Updates/VersionCode.cs` `SuiteVersionBands`, and each workflow's
  `version-code` step): **1** (Flyleaf, Score, Post, Plate, Index), **7** (Foreword), **8** (Folio)
  and **9** (Pulp). **Constitution takes band 10**: a new package with nothing shipped, so the entry
  is `new("com.storyworthtelling.constitution", 10, 0)`.
- Proposed app id **`com.storyworthtelling.constitution`**, one constant
  (`ConstitutionIdentity.ApplicationId`), and `PolicyContext.ConstitutionPackage` equal to it,
  guarded on both sides.

---

## 2. What the book draws that needs building first

None of these blocks a page as drawn. Each is a prerequisite the book assumes, and a likely PBI.

| # | Book / source | What doesn't exist yet | Where it lands |
|---|---|---|---|
| G1 | p.3, D12 | IconStudio crops the plate with `CentreSquare` (`Tools/IconStudio/AdaptiveIconRenderer.cs`). The approved icon B needs a **source crop rectangle** on `IconSpec` (rows 190–974 of the 864 × 1184 painting, centred columns, square), validated square and in bounds | `Tools/IconStudio` + `IconStudio.sln` tests |
| G2 | p.1, D9 | `Branding/constitution-cover.jpg` isn't in the repo yet. It is among the design book's published files and must be committed from there (invented art) | Branding |
| G3 | p.2, D10 | The Constitution ladder in `Tokens.cs`, `AppIdentityTokens.cs`, `DesignTokens.Identity.Constitution` and DESIGN.md §3, plus the hue-separation guards | four token files |
| G4 | p.11, D43–D48 | Crinkle ground, tape, clipping, index-card rules, chart grid and receipt as registered material tokens + DESIGN.md §2 departure | `MaterialTokens.cs`, DESIGN.md |
| G5 | p.11, D45 | The Meals steel cloth; the D45 deep-tab departure recorded in §9; and Q4's outcome | DESIGN.md §9, `AreaHueTests` |
| G6 | p.5 | A registered "Dose" rung for 54 pt | `PaperTypeScale`, DESIGN.md §19 |
| G7 | p.14, brief | Plate `READ_RECIPE` errand | Plate + `Codex.Domain/Plate` |
| G8 | p.12, p.13 | The undo dog-ear on a native screen exists (`DogEar`); the "Cook this soon" inline success and the Swap card's greyed days (D51) are new compositions of `ActionCard` | Constitution.Android |
| G9 | p.19 | The full-weight ledge on wrapped suite rows; Constitution's cover entry and cloth binding | Flyleaf |
| G10 | brief | Hourly forecast + air quality in `Storyworthtelling.Google` | shared library |
| G11 | brief | Pulp's household-file machinery as a shared library | new shared project |
| G12 | brief | Sleep Number client (unofficial SleepIQ endpoints), a bed-side mapping per sleeper, and its "Waiting for the bed" state (p.16) | Constitution.Infrastructure |

---

## 3. Risks

| # | Risk | Effect if it bites | Mitigation |
|---|---|---|---|
| R1 | Sleep Number access is unofficial (brief) | Sleep chart stops; the voice says so calmly (p.16) | Watch first, bed only when the watch reads zero (brief); Problem fold + bookmark; nothing else depends on it |
| R2 | Read-back reads pen marks wrongly (D17, D18) | A skipped workout counted done, a denied dinner approved | 0.75 threshold → Caution confirm card (Index's rule); the targets' rectangles go to the reader; silence keeps its written meaning |
| R3 | The cloud's flattened PDF omits annotations | No ink at all; every workout reads as skipped | Day-one device check; the read reports "document found, nothing read" to Diagnostics, never silently |
| R4 | "Opened" signal absent | Digest stays 3 days | Treated as the real behaviour (§1.4) |
| R5 | Health Connect background reads unavailable | Morning recovery swap uses stale values | Read on every app open; record the gap in Diagnostics |
| R6 | Lockdown suspends watch/scale apps (§1.10) | Body empty on the owner's phone | Allowlist the packages; device check |
| R7 | Calendar placement wrong (brief: "the dealbreaker") | Trust lost faster than any other failure | Household events first, then workouts (brief); pure placement engine with a property-test suite over invented calendars; owner edits obeyed silently |
| R8 | Two planners | Double-booked or duplicated weeks | Single-writer planning with a lease record (§1.11) |
| R9 | Partner's calendar share not done | Her cook and shopping events never appear | Owner action; the plan treats it as a gate for Meals |
| R10 | Claude unavailable at planning time | — | Rules-only fallback plan (brief) |
| R11 | Cost | Daily vision reads ×2 tablets, weekly planning, image generation | Measure on the first real week, as Folio did; cache the instruction prefix |
| R12 | NYT terms (§1.7) | — | Q2 |

---

## 4. Frozen identifiers and their guards

Nothing fails loudly when these move, so each gets a guard in
`Constitution.Tests/Guard/ConstitutionInvariantsTests.cs` from the first build.

| Identifier | Value (proposed) | Why |
|---|---|---|
| App id | `com.storyworthtelling.constitution` (`ConstitutionIdentity.ApplicationId`) | The update channel installs by package id |
| `PolicyContext.ConstitutionPackage` | same | Flyleaf's shelf, cover and allowlist key off it |
| versionCode band | **10** (`SuiteVersionBands`) | Unused; one place decides |
| Vault section | `SecretsVault.Constitution`, `[JsonPropertyName("Constitution")]` | A rename silently empties it |
| Drive files | `storyworthtelling-constitution-household.json`, `storyworthtelling-constitution-body.json` (per account) | Found by name on first run |
| Household record kinds | one constant set, like Pulp's `HouseholdKinds` | A kind is a wire name |
| Workers | registered Java names + unique work names (morning read-back, delivery, forecast check, Body sync) | WorkManager persists rows by class name |
| SharedPreferences keys | string constants, never `nameof` | Folio's and Foreword's lesson |
| Contracts | `RECEIVE_BUDGET` (Index → Constitution), `READ_RECIPE` (Constitution → Plate), and Q3's bookmark contract if chosen | Pinned equal on both sides by content-read guards |
| Health Connect manifest | the rationale activity + read-only permission set | Missing pieces fail silently (§1.3) |

Standing guards the plan should carry: no Health Connect write permission; no hex or type-size
literal outside token files; the six voice seats as a closed list with errors excluded; no
population norm, score, streak or good/bad colour on a Body chart (brief: "neutral ink"); Body data
never leaves the install except to its own encrypted backup; no key-shaped literal under
`Constitution/`; faith content kept out of composed lines (brief); and a frozen-cover guard for the
Flyleaf entry.

---

## 5. Release breakdown (proposed; the plan refines it)

The brief orders **Body first, then the other pillars**.

| Release | Delivers to the owner |
|---|---|
| **A — Identity and frame** | Icon and cover through IconStudio's crop (G1–G3), the app on Flyleaf's suite shelf on its own ledge (G9), the five-tab scrapbook shell (G4, G5), Settings and vault, CI band 10 |
| **B — Body** | Health Connect per signal, the doctor's-chart scraps, the signal page, the bed, the monthly voice seat, the encrypted Body backup |
| **C — Workouts** | Assessment week, the planner, calendar placement, the workout document, morning read-back, Skip/Swap with undo |
| **D — Meals** | Household file, recipe box and scan (G7), the empty box to 10, the weekly sheet, recipe document, groceries (Q1), the Index budget, "I ate something else" |
| **E — Digest and notices** | Saturday digest, the Waiting page's full set, the Flyleaf bookmark (Q3) |
| **F — On the devices** | The device checks in §6 |

---

## 6. Not verified until a device

Stated plainly: "built and tested against fakes" is not "works".

1. Health Connect permission requests opening at all (the rationale activity), per signal, on both
   phones; background reads.
2. Watch and scale data reaching Health Connect on the locked phone (§1.10).
3. The cloud's flattened PDF carrying pen strokes, and the vision read of ticks, circles, numbers
   and the "No, because" field on both tablets.
4. The documents on a Paper Pro and an rM2: target sizes under a real pen, greyscale legibility.
5. The SleepIQ endpoints, login and sleeper-side mapping.
6. Calendar writes to the partner's shared primary calendar.
7. The Plate `READ_RECIPE` round-trip and the photo errand under the lockdown.
8. Flyleaf's second row and bookmark on the phone.
9. The Index budget broadcast between the separately installed apps.
10. The in-place update channel installing band-10 builds, and `SelfOnly` on the partner's phone.

---

## 7. Owner actions (cannot be done from here)

- Partner shares her primary calendar with the owner's account, with edit rights (brief).
- The Sleep Number household login into Settings (it goes to the vault).
- Name the watch companion and scale apps' package ids for the allowlist.
- Re-register Google sign-in for the scopes Constitution asks for (calendar, Drive, Tasks if Q1
  picks Tasks).
- Answer Q1–Q7.

---

## 8. Questions for the owner

| # | Question | Options (recommended first) |
|---|---|---|
| Q1 | Groceries can't go on a Keep list (§1.6) | **(a) a Google Tasks list "Groceries"**, items added and removed singly, a tick reads back as bought · (b) printed on the meal sheet only, no phone list · (c) Keep by share-intent (a new note each week, nothing read back) |
| Q2 | NYT's terms forbid automated reading and AI use (§1.7) | **(a) drop NYT; the stretch dish is a Claude-composed recipe or a household card not cooked lately** · (b) pasted links only, fetched once on the owner's tap, never searched — a judgement call against the stated terms, like Pulp's PWHL ruling · (c) the owner scans an NYT recipe like a family card |
| Q3 | Bookmark without a push (§1.9) | **(a) a signed broadcast, Constitution → Flyleaf, carrying a role (Problem / Reference) per notice, mapped to the ribbon; no notification posted** · (b) post silent min-importance notifications on the owner's phone only, and accept grey/teal ribbons rather than rose/slate · (c) no bookmark. Also: a waiting scan is teal (p.18) or slate (p.19)? |
| Q4 | Workouts = Problem rose, Body = Action pine (§1.2) | **(a) shift both cloths a little off the role hues to registered area tokens** (still red and green) · (b) keep them and list the two clashes as recorded exceptions in `AreaHueTests`, as Post's are · (c) recolour the folds on those pages |
| Q5 | The two tablets (§1.4) | **(a) separate reMarkable accounts, a per-install token in each person's personal vault** · (b) one shared account, documents titled per person and both adults see everything on both tablets |
| Q6 | The partner's phone has no Plate (§1.8) | **(a) install Plate on her phone through the update channel; "I ate something else" falls back to any camera** · (b) recipes are scanned only on the owner's phone |
| Q7 | Who reads the ink | **(a) Constitution's own morning worker, scheduled after Foreword's delivery** · (b) Foreword reads Constitution's documents as part of its run |

**Answers (owner):**
- Q1 → **D81:** a new Keep note each week, through Keep's share intent; nothing read back.
- Q2 → **D82:** no scraping; an official API or written permission only, after a spike. Until then, no NYT route.
- Q3 → **D83:** on the partner's phone (no Flyleaf), ordinary notifications; on the owner's, the Flyleaf bookmark.
- Q4 → **D84:** cloths may share hues with each other and with roles; amend the guard and §9.
- Q5 → **D85:** pair the same way as Foreword, through the vault's shared token.
- Q6 → **D86:** the partner uses the system camera; the owner scans through Plate's tray (`READ_RECIPE`).
- Q7 → **D87:** Constitution's own morning worker.
- Native vs MAUI → **D80:** native.

---

### Sources

- Platform: `Microsoft.Android.Ref.36` 36.1.53, `ref/net10.0/Mono.Android.xml`, namespace
  `Android.Health.Connect` (nuget.org flat container)
- `Xamarin.AndroidX.Health.Connect.ConnectClient` 1.1.0.4 (Microsoft), nuget.org
- Google Keep API overview: https://developers.google.com/workspace/keep/api/guides · `notes`
  resource: https://developers.google.com/workspace/keep/api/reference/rest/v1/notes
- NYT Cooking robots.txt: https://cooking.nytimes.com/robots.txt · terms:
  https://help.nytimes.com/hc/en-us/articles/115014893428-Terms-of-Service
- reMarkable 2 / Paper Pro display resolutions (manufacturer specifications)
- Repository files as cited inline
