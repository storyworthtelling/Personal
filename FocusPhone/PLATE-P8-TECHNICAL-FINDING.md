# Plate (P8) — Technical Finding

The governance gate between the design interview and planning. Nothing here is built.
It records what was verified, what was decided and why, what the guard test must assert,
the release shape, and the risks that are still open when the phase starts.

P7 added this stage and it paid for itself by reversing one of its own decisions before
any code was written. This one reverses one too — see §2.1.

**Status: approved, then substantially revised during the mockup walkthrough.** Two changes
after approval are large enough that this document should be re-read rather than remembered:
the identity hue settled at magenta 326° on a light cloth with no §3 departure needed
(§2.7 of the approved draft, now §2.6 of the studies record), and **the spine was reversed —
Plate gained a Darkroom** (§0, §2.1). The release shape is now four builds, not one (§5).

---

## 0. The live pain, in the owner's words

The design interview produced one rule and one condition:

> **Plate never keeps anything — every capture is handed somewhere and forgotten.
> But it must never lose one.**

**That rule was reversed during the mockup walkthrough, deliberately, by the owner.** It is
kept above because everything below was built on it and because the reversal is the most
important thing in this document. The rule now is:

> **Nothing leaves Plate until you have chosen it. Nothing stays once you have.**

Plate gained a **Darkroom**: captures wait there to be culled, cropped and tinted, and only
what survives is sent. See §2.1. The owner's gate-1 answers that this supersedes — *"nothing,
it's just gone into Photos"* and *"the Photos button is the whole answer"* — are left in the
list below rather than edited out, because a phase that quietly rewrites what it was told is
a phase nobody can audit.

Everything below is measured against that sentence. The rest of what the owner said,
because the detail matters more than the summary:

- What they point the camera at is **people and moments**. Not paper, not codes, not
  reminders. Those exist but they are not why the camera comes out.
- After the shutter: *"Nothing — it's just gone into Photos."* They do not go back for it.
  The **"Photos" button is the whole after-story** — Plate gets no gallery, no roll, no
  library of its own.
- Three things are wrong with the camera today, and the third was volunteered rather than
  offered: scans vanish into Downloads · it isn't in the system · **"Pictures don't go
  directly into Google Photos."**
- A scan can go to **Drive, Post, or the reMarkable**, and the owner **picks before they
  scan** — the destination is the reason the scanner was opened.
- All four Post handoff moments are real: a card becomes a contact · a face for a contact
  who has none · a statement becomes a balance · an emailed photo opens properly.
- Video, the front camera, and flash/low light are all real absences. **All of it, one
  release** — superseded in the walkthrough once the scope roughly tripled; see §5.
- Plate should **be the phone's camera** — the one that opens when anything asks for a
  picture.

---

## 1. Verified platform facts

Each fact carries how it was established, because the ones that were only reasoned about
are the ones that bite.

**F1 — the photo bug is real and it is in the source.** `CameraActivity.TakePhoto`
(`CameraActivity.cs:113-126`) builds its `ContentValues` with a display name and a MIME
type and nothing else, then inserts into `MediaStore.Images.Media.ExternalContentUri`.
There is no `RELATIVE_PATH`. *(Read directly.)* With that column unset the Images
collection files under its default primary directory, `Pictures/`. Google Photos
auto-backs-up `DCIM/Camera`; every other device folder is opt-in under "Back up device
folders." *(Documented platform behaviour, not measured on this phone.)* So every photo
this app has ever taken is on the phone and outside backup unless the owner switched that
folder on by hand — which is exactly what they reported without being asked. The test plan
must still confirm the mechanism on-device, because "outside backup" and "not visible at
all" are different failures with the same symptom.

**F2 — the scan destination is deliberate, and it is the wrong one.**
`OnActivityResult` inserts the scanner's PDF into `MediaStore.Downloads`
(`CameraActivity.cs:308-315`) with a comment saying it is so "Drive/Photos can see it."
They can see it; the owner cannot find it. *(Read directly.)*

**F3 — the head has nothing to freeze but its identity.** Three files, 364 lines. No
SharedPreferences, no database, no services, no workers, no WorkManager registrations.
`CrashReporter.Install(this, "Camera")` writes under `FilesDir` and the label is display
text keyed to nothing (`Diagnostics/CrashReporter.cs:21-23`). *(Read directly.)* Compare
Post, which had to freeze eleven preference file names and two Drive file names, and Score,
which had to freeze a running service's component name. **Plate's freeze list is the app
id, the signature, and the versionCode floor.** That is the whole of it, and it makes the
rename the cheapest of the four.

**F4 — the versionCode ceiling.** `build-flyleaf.yml` publishes every head at
`100000 + run_number` (`build-flyleaf.yml:78`), and that workflow is at run 108. The
floor exists because the pre-rename `build-focusphone.yml` had reached ~132. So the
highest versionCode Camera can possibly have shipped is **100108**. *(Computed from the
workflow and the run list; not read out of the APK, which would need a download and an
`aapt2` this sandbox has no Android SDK for.)* A signed
`com.storyworthtelling.focusphone.camera-Signed.apk` is on the `apps-latest` release,
uploaded 23 Aug. *(Read from the release.)*

**F5 — CI is dead, and has been since before P7 closed.** Every workflow on `main` failed
3–5 seconds after starting as recently as 02:00 today: Flyleaf run 108, Ledger 51, Post 11,
shared-libs 25, all `failure`. A 3-second failure is the account-level Actions billing
stop, not a code failure. **`build-post.yml` has therefore never produced an APK** — P7
closed with its first build unverified and it is still unverified. *(Read from the run
list.)* The P7 plans say Actions resets 1 September.

**F6 — the Domain suite runs here, and it is the only compiler this phase has.**
`dotnet test FocusPhone/Codex.Tests/Codex.Tests.csproj` — **1519 passed, 0 failed, 1 second**.
*(Run.)* The Android heads do not build in this sandbox and Plate is the one that cannot
build even with the Android SDK stitched in, because `Xamarin.GooglePlayServices.MLKit.*`
fetches `.aar` payloads from `dl.google.com` at build time.

**F7 — P7's on-device gates are all still open.** "Post P7 — On-Device Test Plan (rev 2)"
is in Drive with every `RESULT:` blank. *(Read.)* So the two Post surfaces that hand work
to Plate — the attachment route in `Attachments.cs:76-80` and the contact-photo route in
`RolodexArea.cs:159-173` — are **compiled and unproven**, not working.

**F8 — the guard machinery for intent filters already exists and is already correct.**
`FrozenInvariantsTests.IntentFilters(source)` (`FrozenInvariantsTests.cs:409`) extracts
only the text inside `[IntentFilter(...)]` attributes, and
`SmsRoleComponentsAreDeclaredWithTheirFilters` uses it. That is the repair for P7's bug
where a guard passed with the `[IntentFilter]` deleted, because the action string is also
the constant's value. **Plate's component guard reuses it rather than re-inventing it.**
*(Read directly.)*

**F9 — the balance handoff contract is already written, in Domain, and reusable.**
`Codex.Domain/Post/MoneyMail.cs:203-247` defines `IndexHandoff` — package, receiver,
action, the signature-level permission, and `Extras`, with the balance crossing as exact
text rather than a double. `MoneyMail.Read(subject, body)` is pure and takes text.
*(Read directly.)* A statement photographed and recognised into text can go straight in
with a null subject. **The statement→balance route is nearly free once text exists.**

**F10 — the reMarkable library already takes exactly what the scanner produces.**
`IRemarkableClient.Upload(title, byte[] pdfBytes, string? parentFolderId)` plus
`EnsureFolder(name)`. *(Read directly.)* The document scanner already emits a PDF.

**F11 — the identity wheel is nearly full.** Computed against `PaletteSpec.RoleWheel`
itself rather than the table in DESIGN.md §3, the windows sitting ≥20° from every
chromatic role (excluding near-neutral *resolved* at 0.010 chroma) are:

| Window | Width | Occupied |
|---|---|---|
| 37–65° | 29° | Flyleaf (41°) |
| 105–109° | 5° | — |
| 172–187° | 16° | Score (178°) |
| 227–233° | 7° | — |
| **273–357°** | **85°** | Post (285°) |

*(Computed. `Oklch.HueSeparation` is plain shortest-way-round circular distance —
`Oklch.cs:74`.)* One roomy window remains and Post sits at its low end.

**F12 — five copies of the package id, one guard.** `PolicyContext.CameraPackage`,
`HomeFeatureSet.CameraPackage`, `Flyleaf.Android/Tile.cs:12`, and twice in Post as a
private `PlatePackage` (`Attachments.cs:98`, `RolodexArea.cs:175`). Only the first is
asserted by `FrozenInvariantsTests`. *(Read directly.)* All five are within reach of
`Codex.Domain`, which every one of those files already references.

**F13 — Google cannot be asked what is in a photo.** As of **31 March 2025** the
`photoslibrary.readonly`, `photoslibrary.sharing` and `photoslibrary` scopes are removed and
calls relying on them return `403 PERMISSION_DENIED`; what survives is `appendonly` and
`readonly.appcreateddata`, so an app reads back only what it uploaded itself. Reading the
wider library needs the Picker API, which is a user-driven file picker rather than a query.
*(Verified against Google's own "Updates to the Google Photos APIs" and "Authorization
scopes" pages.)* The API has also never exposed per-item content labels or face groupings —
the nearest thing is a `contentFilter` on search. **And it is moot regardless: a Darkroom
photo has not been uploaded, so Google knows nothing about it by definition.**

**F14 — Claude will describe a photo but will not name the people in it.** So neither the
caption call (§2.7) nor any cloud service supplies *who*. Recognition therefore has to be
local and learned: ML Kit Face **Detection** finds and crops faces on-device but identifies
nobody; identity needs an embedding model and a labelled store the owner builds by naming a
face once. *(Established from the model's own behaviour and ML Kit's documented API surface;
the bundled-model plumbing is unverified here and is what Release D exists to prove.)*

---

## 2. The decisions

### 2.1 The Darkroom — and the spine it replaced

**Nothing leaves Plate until you have chosen it. Nothing stays once you have.**

Plate keeps every capture in a **Darkroom** until it is sent or deleted. The owner reversed
the original spine here for a reason their own gate-1 answers support: someone who shoots a
lot and never goes back is exactly the person who needs a triage step, and nobody culls
inside Google Photos. The Darkroom is where several shots of the same thing become one, where
a crop or a tint happens, and where the rest are thrown away before they reach anything.

**Everything waits in one room, not two.** Photos to be chosen and scans whose handoff failed
share the Darkroom, chosen over a version that separated them under their own headings. One
front door, at the cost of a creative step and a delivery failure sitting in the same list.

**What this costs, stated plainly and accepted by the owner.** Under the old spine a photo was
safe the instant the shutter closed, because it went straight to `DCIM/Camera` and Google
Photos backed it up. Under the Darkroom **Plate holds the only copy** until the owner acts.
An app-data clear, a factory reset, or simply never getting round to it loses pictures that
were never backed up anywhere — which is the same failure this phase exists to fix
(F1), reached deliberately instead of by accident.

The owner was offered four mitigations — auto-send after a week, auto-send only what was
never opened, a dog-ear that refuses to flatten, or nothing — and **chose nothing**:
*"it's my darkroom, my job."* There is therefore **no auto-send, no expiry and no nag.**
This is a knowingly accepted risk, and the on-device test plan must state it as one rather
than test around it.

**The never-lose clause is now load-bearing in a way it was not.** The Darkroom is the only
copy, so its store cannot be a cache: it survives process death, upgrade and reboot, and a
failed handoff keeps its file rather than dropping it. That reasoning still diverges from
`IndexHandoff`'s documented "there is no outbox" — an email that fails to hand over comes
round again on the next sync; **a photograph is unrepeatable** — and both doc comments must
say so, or someone will "fix" one of them.

### 2.2 Photos — the live bug, and where the fix now lands

The bug is unchanged (F1): the capture insert carries no `RELATIVE_PATH`, so every photo this
app has taken has landed in `Pictures/` rather than `DCIM/Camera`, outside what Google Photos
backs up by default.

**What changes is when the fix applies.** Capture now writes into the Darkroom's own store.
`RELATIVE_PATH = DCIM/Camera` is set on the MediaStore insert performed **when the photo is
sent**, not when it is taken — so a photo the owner deletes never enters the camera roll at
all, which is the whole point of culling. A sent photo carries its edits; the original is not
kept, because keeping both would rebuild the roll the Darkroom exists to keep clean.

### 2.3 Scans — chosen first, assembled by us, delivered to the root

Three entry points, picked before scanning: **to Drive**, **to Post**, **to the reMarkable**.
Delivery stays hybrid — share intents for Drive and Post, the shared library for the
reMarkable.

**Plate assembles the document; Google only scans a page.** `GmsDocumentScanner` caps at 50
pages and the owner wants longer, so Plate stops taking its finished PDF and takes the
**per-page images** instead — which the shipped code already requests (`ResultFormatJpeg`
alongside `ResultFormatPdf`, `CameraActivity.cs:135`) and then discards — holds them in a page
tray, and composes the document itself. Page order, naming and assembly are ordinary logic
that lives in `Codex.Domain`/`Codex.Infrastructure` where the test suite reaches it (F6),
which makes this the most verifiable new work in the phase. It costs one dependency: PdfSharp,
already used by Canonizer, from a well-established publisher.

**The reMarkable delivery goes to the account root**, not into a named folder. That uses
`IRemarkableClient.Upload(title, pdfBytes)` — the plain overload DailyLoader has exercised all
along — instead of `EnsureFolder` plus the folder overload. It is a risk reduction rather than
a simplification: CLAUDE.md records the reMarkable v2 cloud path as reconstructed and not
live-verified, **especially the folder/collection wire format**. Sending to the root means
Plate depends only on the part of that library which has actually run.

**QR scanning stays**, as a fourth door set apart from the three destinations. It was nearly
lost: the rev-2 mockups dropped it silently even though the shipped head has it
(`StartQrScan`). One thing changes — today the result appears in an `AlertDialog`, which §7
bans outright; the value goes on the page instead, with a URL still handed to the restricted
browser exactly as now.

**Reading is not a Plate door.** A statement read starts in **Index**, a card read starts in
**Post**. Plate never decides what a piece of paper is for — it is asked, it reads, it
returns. See §3.

### 2.4 Plate is the phone's camera

Unchanged from the approved draft: Plate declares the full camera component set so nothing on
this phone opens a different camera. This is the `ROLE_SMS` shape from P7 with the same trap —
**Android silently omits an app missing a component**, with no warning.

| Component | Why |
|---|---|
| `MediaStore.ACTION_IMAGE_CAPTURE` | another app asks for a picture |
| `MediaStore.ACTION_IMAGE_CAPTURE_SECURE` | the same, from the lock screen |
| `MediaStore.ACTION_VIDEO_CAPTURE` | another app asks for a video |
| `Intent.ACTION_STILL_IMAGE_CAMERA` | the power-button double-press |
| `Intent.ACTION_STILL_IMAGE_CAMERA_SECURE` | the same, locked |
| `Intent.ACTION_VIEW` + `image/*` | **Post's attachment route** — without it, `Attachments.cs` setting the package produces an activity-not-found rather than a picture |

**A requested capture goes through the Darkroom first**, by the owner's decision, taken over
returning it immediately. The consequence to watch on-device: `ACTION_IMAGE_CAPTURE` is a
*blocking* contract — the calling app is sitting in `onActivityResult` waiting, and a detour
through culling and editing can outlive it, with the caller backgrounded or killed. The test
plan must exercise this specifically, because it is the one decision in the phase that makes
another app's behaviour depend on how long the owner lingers.

### 2.5 Camera scope — video, front camera, flash, and a true ratio

All three, as decided: `Xamarin.AndroidX.Camera.Video`, a lens selector, a flash mode, and
`RECORD_AUDIO` — a sensor permission the device owner cannot silently grant, so it costs a
runtime dialog. **Video must degrade to silent rather than refuse** if that dialog is
suppressed by the lockdown.

**The preview window always carries a true capture ratio.** It is never stretched, padded or
cropped to fill the well. The default is **9:16** (the 16:9 sensor crop held portrait), which
at 411dp fills 528dp of a ~694dp well where 3:4 filled 396dp and left 210dp of slack; choosing
3:4 makes the window *shorter* rather than the frame dishonest. That needs a ratio control,
which the first mockups lacked.

### 2.6 Recognition — text now, faces later

**Text** (`Xamarin.GooglePlayServices.MLKit.TextRecognition`) serves the inbound reads: a card
for Post, a statement for Index. The statement route is cheap because `MoneyMail.Read` and
`IndexHandoff` already exist (F9); the card route needs new pure parsing, which is Domain work
and therefore testable here.

**Faces are a separate subsystem in a separate release.** Two facts shape it (F13, F14):
Google's Photos API cannot be asked what is in a picture, and **Claude will describe a photo
but will not name the people in it**. So recognition cannot come from either. It is built
locally, in the order it learns: ML Kit **Face Detection** crops faces → a bundled
MobileFaceNet-class TensorFlow Lite model embeds each one → the owner labels a face once →
later faces are matched by nearest neighbour against the stored labels, with a confidence
floor and an "is this Ashley?" fold when it is unsure.

Nothing about that leaves the device, and the parts that decide anything — thresholds,
tie-breaks, when to ask rather than assert — are pure and testable in `Codex.Domain`. The
model file and the ML Kit/TFLite bindings are the parts that cannot be verified here.

### 2.7 The Darkroom's captions go to Claude, and what that costs

Captions come from a Claude vision call, by the owner's decision. Two consequences are on the
record because they are not obvious:

**The photo leaves the phone before the owner has chosen to send it.** The cull therefore
stops meaning *"the bad ones never reach a cloud"* and starts meaning *"the bad ones never
reach Google Photos"* — including the ones about to be deleted. The owner chose this knowing
it; a cheap mitigation exists if they later want it (caption only what survives a first cull).

**Google cannot be asked instead.** As of 31 March 2025 the `photoslibrary.readonly`,
`.sharing` and `photoslibrary` scopes are removed and return `403 PERMISSION_DENIED`; what
survives reads back only what the app itself uploaded. And a Darkroom photo has not been
uploaded, so Google knows nothing about it by definition (F12).

## 3. The handoff contract, in both directions

P7 shaped this to accept an inbound direction and built nothing speculative. P8 builds the
inbound half — and the walkthrough moved **reading** from the outbound side to the inbound
one, which is the simpler contract: the app that wants the data already knows what it wants,
so Plate never has to work out what a piece of paper is for.

**Inbound — another app asks, Plate looks, Plate returns**

| Trigger | Mechanism | Plate's obligation |
|---|---|---|
| Photo attachment *(exists, unproven — F7)* | `ACTION_VIEW`, `image/*`, `setPackage(Plate)` | declare the VIEW filter (§2.4) and show the image |
| Contact needs a face *(exists, unproven — F7)* | `GetLaunchIntentForPackage(Plate)` | open ready to take a face and hand it back |
| **Read a card** *(new)* | explicit intent from Post's Rolodex | recognise, let the owner correct it, return name/phone/email as text |
| **Read a statement** *(new)* | explicit intent from Index | recognise, let the owner correct it, return via `IndexHandoff` unchanged |
| Requested capture | `ACTION_IMAGE_CAPTURE` | **through the Darkroom first** (§2.4) — and see R10 |

The contact-photo route arrives today as a **bare launch**: Post opens Plate and hopes. P8
gives it an explicit action and a returned result **while continuing to accept the bare
launch**, because the Post already in the field will keep sending it until Post ships again.

**Index does not exist yet.** It is P11. So P8 builds Plate's end of the statement read and
**Post is the only live caller** until then — which means that route ships untested against a
real counterpart, and the test plan must say so rather than imply a passing check.

**Outbound — Plate hands something over**

| Capture | Destination | Mechanism |
|---|---|---|
| Photo, once chosen | Google Photos | MediaStore insert with `RELATIVE_PATH = DCIM/Camera` (§2.2) |
| Scan | Drive · Post | share intent (§2.3) |
| Scan | the reMarkable | `Upload(title, pdfBytes)` — **the account root** (§2.3) |

**Vitality's recipe scan** (P12) is a later consumer of the inbound read path. It is designed
for — an "ask Plate to read this" contract is general, not card-shaped — and **not built**.

## 4. What the guard test must assert

Every one of these is written first and **watched to fail** by reintroducing the bug it
guards. Three of P7's guards passed with the bug present; a guard nobody has watched fail
is not a guard.

1. **The app id is frozen** — `com.storyworthtelling.focusphone.camera`, at the one Domain
   constant, and exactly one csproj declares it as `<ApplicationId>`. *(Extends the
   existing assertion at `FrozenInvariantsTests.cs:26`.)*
2. **The id appears once, not five times** — no `.cs` file outside the Domain constant
   contains the literal. Proven to fail by re-adding one of the copies from F12.
3. **Every camera component is declared with its intent filter** — all six rows of §2.4,
   through `IntentFilters(source)` (F8) so the assertion reads the attribute and not the
   file. Proven to fail by deleting an `[IntentFilter]` while leaving the constant in place
   — which is the exact shape that fooled P7.
4. **`build-plate.yml` exists, publishes to `apps-latest`, and floors the versionCode above
   100108.**
5. **No workflow builds a folder that does not exist** — every `FocusPhone/*.Android` path
   named in any workflow resolves on disk. This is §2.8's bug caught mechanically instead
   of by remembering. Proven to fail by leaving one Camera path in `build-flyleaf.yml`.
6. **Plate's identity tiers stay ≥20° from every chromatic role** — added to the existing
   `IdentityTiersStayClearOfEverySemanticRole` theory, measured through `PaletteSpec.RoleWheel`.
7. **No colour hex or type-size literal outside the registered token files** — the existing
   leak guard, extended to Plate's sources.
8. **All eight fonts are packaged, not six.** `Codex.Ui.Android` globs
   `..\..\Fonts\*.ttf`, so every font ships — but the fatal FONT CHECK in
   `build-flyleaf.yml` asserts only six, and **the two it omits are
   `SourceSerif4-Black`, which every `PaperUi.ScreenHeader` renders in, and
   `PinyonScript-Regular`, which is the owner's voice.** The check exists because a
   `Link`-separator bug can pack a font under a wrong literal name so `Assets.Open` fails
   silently; it would currently miss that happening to the header font or the voice.
   `build-plate.yml` asserts all eight. Proven to fail by removing one from the glob.
   *(CLAUDE.md also says "six shared fonts" and should be corrected to eight.)*
9. **The Index contract is unchanged** — the existing `MoneyMailTests` assertions still hold
   with Plate as a second sender.

---

## 5. Release shape

**Four releases, not one.** The owner's original "all of it, one release" answered a question
about video, the front camera and flash. The scope has since roughly tripled — multi-page
scanning with our own PDF assembly, the Darkroom, an editor, Claude captions and a
face-recognition subsystem — into the one head that cannot be compiled here, with CI down.
Put back to the owner, they chose **guardrails, then the camera, then the Darkroom**, with
recognition in its own release.

| | Release | Contents |
|---|---|---|
| **A** | Guardrails | The rename with app id, keystore and versionCode frozen · `build-plate.yml` · Camera out of `build-flyleaf.yml` · every guard in §4, each watched to fail · the package id collapsed to one constant. **Nothing else.** |
| **B** | The camera | The Photos bug (§2.2) · video, front lens, flash, the true-ratio window (§2.5) · scanning with our own multi-page assembly, QR restored, reMarkable to root (§2.3) · kit adoption and restyle · the inbound reads (§3). |
| **C** | The Darkroom | The room, the editor (crop, tint, filters), culling, and the Claude captions (§2.1, §2.7). |
| **D** | Recognition | Detection → embeddings → the labelled store → matching, and the "is this Ashley?" fold (§2.6). |

**A is deliberately almost empty**, and that is the point: it proves the in-place upgrade and
the update channel before a single feature rides on either. It is also the only release whose
whole content this sandbox can verify, since it is guards, workflow and one constant.

**The merge gate, unchanged: no building until Wednesday, and nothing merges without a green
`build-plate.yml` that has produced a signed APK.** CI is expected back on the 1st (F5); if it
is not, the phase does not merge on a promise. The Domain suite (F6) is necessary and nowhere
near sufficient — it has never compiled a line of this head.

## 6. Open risks

**R1 — There is no compiler for this head, and the release is large.** *The phase's
largest risk, by a distance.* Plate cannot build in the sandbox (F6) and CI is dead (F5).
Every Android line written this phase is unverified until one green build. The mitigation
is structural, not procedural: **every decision lives in `Codex.Domain` where the 1519-test
suite reaches it, and the head stays a thin painter over it.** The kickoff's own warning is
that P7's only working check was local compilation and it caught a real break in Score;
P8 does not have that.

**R2 — The camera role may be suppressed by the lockdown.** The `ROLE_SMS` question again,
and still unanswered for Post (F7). If the device-owner policy prevents Plate becoming the
default camera, Plate is an app you open rather than the phone's camera. Unlike Post's
case, **the fallback is mild** — Plate still works when opened from the shelf; only the
double-press and other apps' picture requests go elsewhere. The Post attachment route
(the `image/*` VIEW filter) is a different mechanism and should survive regardless, which
is worth confirming separately.

**R3 — Four `dl.google.com` bindings, two of them new.** Barcode and DocumentScanner today,
plus TextRecognition (§2.6) and `Camera.Video` (§2.5). Each is a build-time `.aar` fetch
that only CI can perform. A version conflict between them is exactly the class of failure
that cannot be seen from here.

**R4 — `RECORD_AUDIO`.** A sensor permission, so the device owner cannot silently grant it
(the same reason `CAMERA` is granted by dialog at cutover — see `ManifestPermissions.cs`).
Video therefore costs a runtime dialog, and if the lockdown suppresses it the way R2 fears,
video records silent or not at all. **Decide now:** video with no audio is better than no
video, so the capture must degrade rather than refuse.

**R5 — Plate has no Google account and no vault passphrase today.** §2.3's reMarkable route
needs both. This is the largest *new surface* in the phase and it is entirely unexercised
code paths in a head that cannot compile.

**R6 — Post's half of the handoff has never run.** Both routes are compiled and unproven
(F7). Building against them means building against an assumption. The test plan must test
the pair, not Plate alone.

**R7 — The icon.** Two apps shipped the *default* icon for months because of the
`Resources/` folder location and the `[Application(Icon=…)]` gotchas. Plate's existing head
has both right already (`CameraApplication.cs:12`, `Resources/mipmap-*`) — so the risk is
regression during the rename, not first-time setup. The adaptive geometry is unforgiving:
108dp layer, 72dp safe zone, round mask 0.333, so corner content must satisfy
`sqrt(2) × (0.5 − inset) ≤ 0.333`. `IconSpec.PlateImage` / `PlateInset` exist from P7.
`build-plate.yml` needs the fatal ICON CHECK, not the `continue-on-error` one
`build-flyleaf.yml` carries.

**R8 — `build-flyleaf.yml` breaks on the folder rename.** Six references (§2.8). Mechanised
as guard §4.5 because remembering is not a control.

**R9 — Recognition quality is unknown.** How well ML Kit reads a real business card, and
how well `MoneyMail.Read` copes with statement text that was never an email, are both
questions only the owner's actual paper can answer. The test plan brings numbers back
rather than impressions, the way P7's field notes were meant to.

---

**R10 — a requested capture now blocks on the owner.** `ACTION_IMAGE_CAPTURE` is a blocking
contract: the calling app waits in `onActivityResult`. Routing it through the Darkroom (§2.4)
means another app's behaviour depends on how long the owner spends culling, and a backgrounded
caller can be killed. Decided knowingly; it needs its own on-device check rather than a
general one.

**R11 — the Darkroom is the only copy, with nothing to catch a forgotten photo.** No
auto-send, no expiry, no nag, by explicit choice (§2.1). The failure mode is silent and looks
exactly like the bug the phase set out to fix.

**R12 — photos leave the phone to be captioned.** Including ones about to be deleted (§2.7).

**R13 — Release D rests on a bundled model.** A TensorFlow Lite face-embedding model in the
APK, plus ML Kit face detection: two more `dl.google.com`-shaped dependencies (R3) and an
asset whose size and load behaviour cannot be measured from here.

**R14 — the editor is unbudgeted.** Crop, tint and filters are the one part of Release C with
no existing analogue anywhere in the suite, and no shared component to borrow.

## 7. What this supersedes, and what it does not build

**Supersedes:** the P8 roadmap card's framing of the phase as "rebrand + a rethink of the
capture flow." The rename is the *cheapest* part (F3); the phase is a camera rebuild, a
delivery system, and a two-way handoff, with a rename attached.

**Not built this phase, and to be kept true at the end** — P7's plan had to be reissued as
a rev 2 because five items moved from unbuilt to built after it was written:

- **Vitality's recipe scan.** Designed for, not built (§3).
- **A gallery, a roll, or any library of Plate's own.** Explicitly refused (§0). The
  pending tray is not one — it holds only what has not landed.
- **A Drive API client and a `drive` write scope.** The hybrid avoids both (§2.3).
- **Score's remaining four areas**, still not colour-coded per §9. Not P8's unless the
  shared kit forces it.
