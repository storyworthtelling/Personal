# Plate (P8) — Technical Finding

The governance gate between the design interview and planning. Nothing here is built.
It records what was verified, what was decided and why, what the guard test must assert,
the release shape, and the risks that are still open when the phase starts.

P7 added this stage and it paid for itself by reversing one of its own decisions before
any code was written. This one reverses one too — see §2.1.

**Status: awaiting the owner's approval.**

---

## 0. The live pain, in the owner's words

The design interview produced one rule and one condition:

> **Plate never keeps anything — every capture is handed somewhere and forgotten.
> But it must never lose one.**

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
  release.**
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

---

## 2. The decisions

### 2.1 The spine, and the outbox P7 refused

**Plate never keeps anything; every capture is handed somewhere and forgotten. But it must
never lose one.** The owner added the second clause and it is the load-bearing one.

The mechanism is a **pending tray with retry**, chosen over "durable write, then hand off."
That is a deliberate reversal of a decision `IndexHandoff` states in its own doc comment:

> There is no outbox. If Index is not installed or the send fails, Post drops it — Index
> will see the same email on a later sync, so the case self-heals, and a durable retry
> queue is real machinery to maintain for something that fixes itself.

**That reasoning does not survive the move from Post to Plate, and the difference is the
whole justification: an email that fails to hand over comes round again on the next sync.
A photograph is unrepeatable.** Post drops a balance because the source still has it;
Plate cannot drop a scan because Plate's capture *is* the source. Two apps will therefore
disagree about outboxes in the same repo, on purpose, and both doc comments must say so —
otherwise the next reader "fixes" one of them.

The tray is the P7 tombstone shape reused, not a new invention: a durable record, hidden
from the ordinary view, retried on a trigger, dropped when the handoff is confirmed, and
folded into the **`DogEar`** when it needs to say something. It is not a gallery — it holds
only what has *not* landed, so a working Plate shows an empty one, which is to say shows
nothing at all.

All of the tray's logic is pure and lives in `Codex.Domain` — what is pending, what to
retry, when to give up, what the fold says. That is not tidiness; it is the only way any of
it gets tested this phase (F6).

### 2.2 Photos — the live bug

Set `MediaStore.IMediaColumns.RelativePath` to `DCIM/Camera` on the capture insert, so a
photo lands where Google Photos already backs up rather than in a folder the owner would
have to know to enable. This is the "handed somewhere and forgotten" rule applied to the
case that matters most, and it is the phase's one true bug fix.

The display-name prefix changes from `FocusPhone_` to `Plate_`; it is display text keyed
to nothing (F3), and existing files keep their names.

### 2.3 Scans — the destination is chosen first

Three entry points, not one scanner and a chooser: **Scan to Drive**, **Scan to Post**,
**Scan to the reMarkable**. The owner picks before scanning because the destination is the
reason they opened the scanner.

Delivery is **hybrid**, as approved:

- **Drive and Post — share intents.** Both already have an app on this phone that accepts
  a PDF, and Post's is a first-party app whose package is already named in Domain. A share
  is honest, costs nothing, and is exactly the spine's shape.
- **The reMarkable — the shared library.** `Storyworthtelling.Remarkable` already uploads
  PDF bytes into a named folder (F10), which no share intent will do, and DailyLoader and
  Canonizer already pair the device token.

**The cost the hybrid does not avoid, stated plainly:** the reMarkable device token lives
in the vault, the vault lives in Drive, and reading it needs a Google account and a vault
passphrase — **neither of which Plate has today.** So the hybrid still buys one Google
sign-in and one passphrase field in Plate's settings; what it avoids is a Drive API client
and a `drive` write scope. If the sign-in proves worse on-device than it looks on paper,
the documented fallback is a share intent to the reMarkable app (which is already
allowlisted in `PolicyContext`), at the price of losing the named folder.

A share intent cannot confirm delivery. Under §2.1 that is survivable: the scan is in the
tray until the owner marks it gone, and an unconfirmable handoff is one the tray keeps.

### 2.4 Plate is the phone's camera

Plate declares the full camera component set, so nothing on this phone ever opens a
different camera. This is the `ROLE_SMS` shape from P7 and it carries the same trap:
**Android silently omits an app that is missing a component** — it does not warn, the app
simply never appears as an option. The set:

| Component | Why |
|---|---|
| `MediaStore.ACTION_IMAGE_CAPTURE` | another app asks for a picture |
| `MediaStore.ACTION_IMAGE_CAPTURE_SECURE` | the same, from the lock screen |
| `MediaStore.ACTION_VIDEO_CAPTURE` | another app asks for a video |
| `Intent.ACTION_STILL_IMAGE_CAMERA` | the power-button double-press |
| `Intent.ACTION_STILL_IMAGE_CAMERA_SECURE` | the same, locked |
| `Intent.ACTION_VIEW` + `image/*` | **Post's attachment route** — without this, `Attachments.cs` setting the package produces an activity-not-found, not a picture |

That last row is the one that is easy to miss and the only one another app already depends
on (F7). It is also the one that reconciles "no gallery" with "an emailed photo opens
properly": Plate keeps no roll of its own pictures, but it will display one handed to it.

### 2.5 Camera scope — video, front camera, flash

All three, one release, as decided. This means `Xamarin.AndroidX.Camera.Video`, a lens
selector, a flash mode, and `RECORD_AUDIO` — which, like `CAMERA`, is a sensor permission
the device owner **cannot** silently grant, so it costs a runtime dialog (see R4).

### 2.6 Text recognition — cards and statements

`Xamarin.GooglePlayServices.MLKit.TextRecognition`, a fourth binding on the one head with
no compiler. Both routes are in, as decided. The statement route is cheap because
`MoneyMail.Read` and `IndexHandoff` already exist (F9); the card route needs new pure
parsing (a name, a phone, an email out of recognised lines) which is Domain work and
therefore testable here.

### 2.7 The identity hue — 345°, and a recorded deviation

**Recommended: 345°.** 32° clear of the problem rose at 17° (the tightest role), 60° from
Post, 56° from Flyleaf, 167° from Score. A crimson — and a darkroom safelight is the one
light a photographic plate can be developed under, so the name and the only available
window happen to agree.

**The deviation, recorded as §3 requires.** §3's route is *sample the painting, then check
the sample against the roles*. This inverts it: the window was computed first (F11) and the
painting will be made to land in it, because with 85° of usable wheel left and Post already
in it, a sampled hue would more likely than not have to be moved anyway. §3 already has
precedent for a departure — Post's hue came from the binding cloth rather than the
painting, and that was written down. This is written down for the same reason.

The committed tiers get the same treatment as every other ladder: registered in §19,
leak-guarded, and added to the `IdentityTiersStayClearOfEverySemanticRole` theory so the
claim is measured rather than asserted.

### 2.8 The workflow — `build-plate.yml`, and the five places that break

Plate takes **its own workflow**, following Post's precedent, for a reason that is Plate's
alone: it is the only head that needs `dl.google.com` at build time, so it is the slowest
and flakiest build in the repo, and isolating it means a Plate binding failure stops
shipping Plate rather than stopping Flyleaf and Score too.

**versionCode floor 200000.** `build-plate.yml`'s `run_number` restarts at 1 while the head
has shipped at up to 100108 (F4), and a lower code is simply rejected by the update
channel. 200000 is Post's number; versionCodes are per-package, so Post and Plate sharing a
floor is harmless and one number is easier to remember than two.

**A fatal APP ID CHECK** that reads the frozen id back out of the built APK, as
`build-post.yml` does. A green build then proves the id survived the rename.

**And the part that breaks silently: Camera must come *out* of `build-flyleaf.yml` in all
five places** — the `paths:` trigger (line 9), the three `for proj in …` loops (79, 109,
129), the artifact upload path (156) and the rolling-release path (176). That is six
references, not five; the loops are three of them and each has to be edited. Miss one and
`build-flyleaf` starts failing on a folder that no longer exists — `if-no-files-found:
error` on the upload makes that fatal — or, worse, keeps quietly publishing nothing.
(There is a seventh mention, at line 52, in a comment explaining why the runner needs
network access. It is not functional, but it stops being true and should go with the rest.)
**This is the bug that meant Score never reached the rolling release**, and the workflow's
own comment says so at line 170. The guard in §4 exists because of it.

### 2.9 One package id, not five

Collapse the five copies (F12) to the single `Codex.Domain` constant and have Flyleaf and
Post reference it. The constant is renamed to `PlatePackage`; the *value* is frozen, the
C# identifier is not. This is not tidying — four of the five copies are unguarded, and the
two in Post are the ones the handoff depends on.

`PolicyContext.OwnerPackagePrefix` keeps its deliberate duplicate in
`Storyworthtelling.Updates` (Domain must stay dependency-free there, and a parity guard
holds them equal). That is a different situation and is left alone.

---

## 3. The handoff contract, in both directions

P7 shaped this to accept an inbound direction and built nothing speculative. P8 builds the
inbound half.

**Post → Plate** *(exists, compiled, unproven — F7)*

| Trigger | Mechanism | Plate's obligation |
|---|---|---|
| Photo attachment | `ACTION_VIEW`, `image/*`, `setPackage(Plate)` | declare the VIEW filter (§2.4) and show the image |
| Contact needs a face | `GetLaunchIntentForPackage(Plate)` | open ready to take a face and hand it back |

The contact-photo route arrives today as a *bare launch* — Post opens Plate and hopes. P8
should give it an explicit action so Plate knows why it was opened and what to send back,
while continuing to accept the bare launch, because the version of Post in the field will
keep sending it until Post ships again.

**Plate → Post / Index** *(new)*

| Capture | Destination | Mechanism |
|---|---|---|
| Business card | Post's Rolodex | explicit intent, signature-permission guarded, carrying recognised name/phone/email as text |
| A face | Post's Rolodex | the same, carrying a content URI |
| Scan | Post as an attachment | share intent (§2.3) |
| Statement | Index | **`IndexHandoff` unchanged** (F9) — balance and date only, as exact text |

Index's contract is reused byte-for-byte rather than copied. Post's side is new and should
be defined in `Codex.Domain` beside `IndexHandoff`, in the same shape and with the same
signature-level permission, so there is one pattern in the repo and not two.

**Vitality's recipe scan** (P12) is a later consumer of the card path. It is designed for —
the recognised-text handoff is a general shape, not a card-shaped one — and **not built**.

---

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
8. **The Index contract is unchanged** — the existing `MoneyMailTests` assertions still hold
   with Plate as a second sender.

---

## 5. Release shape

**One release, as decided.** Not because it is the low-risk shape — it is not — but because
a camera that cannot take a video or a selfie is not the camera this phone needs, and
splitting it would leave the head half-renamed across a phase boundary.

Build order inside the release is not negotiable, because the guardrails are what make the
rest safe to get wrong:

1. **Guardrails.** The rename with app id, keystore and update channel byte-identical.
   `build-plate.yml`, Camera out of `build-flyleaf.yml`, the id collapsed to one constant,
   and every guard in §4 written first and watched to fail.
2. **The live bugs.** `RELATIVE_PATH` (§2.2) and the scan destination (§2.3).
3. **Kit adoption and restyle.** The identity hue registered, the shared kit adopted, the
   `AppCompat` theme reconciled with `PaperTheme`.
4. **Features.** The camera scope (§2.5), the scan routes, the handoff both ways (§3), the
   pending tray (§2.1), text recognition (§2.6).

**The merge gate, as instructed: no building until Wednesday, and no merge without a green
`build-plate.yml` that has produced a signed APK.** CI is expected back on the 1st (F5); if
it is not, the phase does not merge on a promise. The Domain suite (F6) is a necessary
condition and nowhere near a sufficient one — it has never once compiled a line of this
head.

---

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
