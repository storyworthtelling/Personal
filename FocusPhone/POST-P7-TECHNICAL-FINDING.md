# Post (P7) — Technical Finding

**Status:** awaiting owner approval. No production code is written against this document until it is approved.
**Scope:** the architectural decisions behind rebranding and rebuilding the FocusPhone Communication head
as **Post**. Produced from a 152-question design interview and a 104-question technical interview.

The one-line summary of what changed: **Post stops being a viewer of other apps' data and becomes the
phone's actual messaging and calling client.** Everything below follows from that.

---

## 0. The live pain, in the owner's words

> "The current frustration with the communication head is in the inability to **compose** messages or **delete**."

That is the spine of the phase. Every architectural choice below is judged first on whether it delivers
those two, and only then on anything else.

The reason both are currently impossible is the same reason: **Post has no Android role.** It reads the SMS
provider and the call log, but it does not own them, so it cannot write to either. `DeleteText` can only hide
a message locally; `EmailComposeActivity` exists but there is no equivalent for texts. Reading is a
permission; writing is a role.

---

## 1. Verified platform facts

These were checked against the actual .NET binding surface and current platform documentation, not asserted
from memory. They are the constraints the design has to live inside.

| # | Fact | Consequence |
|---|---|---|
| **F1** | `RoleManager.RoleSms`, `RoleManager.RoleDialer` and `RoleCallScreening` are bound in .NET Android (API 29+), with `CreateRequestRoleIntent` / `IsRoleHeld` / `IsRoleAvailable`. | The roles are reachable from C#. |
| **F2** | `DevicePolicyManager` has **no** `SetDefaultSmsApplication` in net-android-34/35/36. | The device owner **cannot** grant these roles silently. Each costs one user-consent dialog, once. |
| **F3** | Third-party call recording is closed: the microphone route was removed in Android 10, the Accessibility route in May 2022. The Pixel dialer records because it is **first-party**. | Taking `ROLE_DIALER` **loses** the only working call recording on the device and gains nothing in return. |
| **F4** | `ROLE_CALL_SCREENING` is a **separate role** from `ROLE_DIALER`. | Google Phone may keep spam screening while Post owns the dialer. **Unverified — must be checked on device.** |
| **F5** | RCS is an open GSMA spec on a closed network: provisioning is per-SIM through carrier autoconfig (Jibe in the US), and `android.telephony.ims` is `@SystemApi` behind **carrier privileges** — a signature match against the certificate on the SIM. Apple's iOS 18 RCS goes through the carrier IMS stack the same way. | **We cannot write our own RCS client.** This is precisely why `gmbridge` (scraping Messages-for-Web) exists. |
| **F6** | Google Fi does **not** help with RCS — Fi is an MVNO on Jibe, and SIM carrier certs authorize Google's own apps. But Fi's **"Voicemails by text"** delivers voicemail transcripts as SMS from the caller's number. | Fi **solves voicemail**: with the SMS role, transcripts arrive as ordinary messages. |

### What F3 and F4 mean together

The owner's rule is *"own as much of the UI as possible, deep link Google Phone where necessary."* The first
reading of that was to take the dialer role. **The mockup review reversed it** — see §2.2, which supersedes
what this section originally concluded.

The reversal turned on a question the owner asked: if the call screens go frameless anyway, what is the role
actually buying? The honest answer is *one screen*. Call-log deletion needs `WRITE_CALL_LOG`, a permission
already force-granted; blocking needs the default dialer **or** the default SMS app, and Post takes the SMS
role regardless. So the role bought a differently-typeset ringing screen and cost Google's screening (F4) and
recording (F3) to get it.

What survives from this section is **F3's real consequence**: the design interview's answer that a call row
should carry "a summary of the transcript" is **not deliverable by Post** either way. Even with Google Phone
keeping the dialer, Call Notes writes its transcript into that app's Recents with no content provider and no
API — the share sheet is the only way out. So the agreed fallback stands: the owner's **own note**, offered
when they next open Post rather than at hang-up, since Post is not on screen when a call ends.

---

## 2. The seven decisions

### 2.1 Texts — a native spine with a bridge on top

**Decision:** Post takes `ROLE_SMS`. SMS and MMS run natively through Android's own provider. `gmbridge`
stays, but its job narrows to **RCS and good-quality picture send**.

The reasoning is that the two requirements pull opposite ways and only a hybrid satisfies both. "Compose and
delete" and "nothing slips" require the native path — only the role holder can write to the SMS provider or
truly delete a message. "Photo quality matters very much" and working group chat require RCS, which
(F5) only the scraped bridge can provide.

Consequences accepted:

- Post becomes responsible for **storing every text the phone receives**, in the system provider, like a good
  citizen — so texts survive Post being uninstalled.
- Read receipts and typing indicators are **never sent**.
- Pictures go over the bridge first; native MMS is the fallback. Post must be able to *receive* MMS
  regardless, because it holds the role.
- The notification listener (`MessageBridgeListener`) is **kept and repurposed as a second RCS capture path**,
  under its frozen component name. This reverses an earlier instinct to delete it, and is the safer answer:
  notification access is granted at provisioning and is painful to regain on a locked phone, and the bridge —
  which the owner judges the likeliest thing to break — should not be the only RCS route.

**Bridge required at build time.** The owner's decision, after I raised the cost. The `GMBRIDGE` conditional
is removed, so a build without the `.aar` is not a build. The consequence is real and is recorded here rather
than discovered later: **the Post head can no longer be compiled locally**, because building `gmbridge.aar`
needs Go, the Android NDK and network access, none of which the sandbox has. CLAUDE.md records that
`Codex.Ui.Android` carried a `CS0104` for weeks when CI was the only check, so the mitigation is structural:
**keep the head genuinely thin** — Areas and adapters only — with every rule in `Codex.Domain` and every
service client in `Codex.Infrastructure`, both of which still build and unit-test locally.

### 2.2 Calls — Post dials, Google Phone connects

**Decision (revised during the mockup review — this reverses the original recommendation):** Post does
**not** take `ROLE_DIALER`. It dials; the system dialer connects.

The original recommendation was to take the role and accept losing Google's screening and recording. Working
through the mockups exposed that the trade was much worse than it looked, for three reasons.

**The role buys one screen.** Everything else it appeared to buy turns out not to need it:

- **Deleting a spam call from the log** needs `WRITE_CALL_LOG` — a *permission*, which `PolicyPlanBuilder`
  already force-grants to Post. Not the role.
- **Blocking a number** through `BlockedNumberContract` needs the default dialer **or the default SMS app** —
  and Post is taking the SMS role regardless. Not the role.

So `ROLE_DIALER` uniquely buys the ringing screen and the in-call UI, and nothing else.

**And that screen was going to be frameless anyway.** The call screens drop the binder tabs — nobody
navigates to Settings while the phone is ringing — so what the role bought was a differently-typeset version
of a screen Google Phone already does well.

**What it cost was real.** Google's spam screening (`ROLE_CALL_SCREENING` separability is unverified) and
Pixel's own call recording. On a Pixel 9 that also means **Call Notes** — on-device recording, transcription
and summary. That is a Pixel 9 feature and the owner is on a **Pixel 8a**, so it is likely absent; worth
confirming on device, because if it *is* present the case is stronger still.

**Transcripts were never reachable anyway.** Call Notes writes its transcript and summary into the Recents
tab of Phone by Google, exported only through the share sheet — no content provider, no API. Post's timeline
could never have ingested them. Keeping Google Phone as the dialer does not put call content in Post; it just
means the content exists somewhere, manually shareable.

**The seam is already built.** `ThreadActivity.PlaceCall` fires `ACTION_CALL` with a `tel:` URI and
`CALL_PHONE` is already declared; the connected call goes to whichever app holds the role. The owner dials in
Post — with Post's keypad and Post's contact matching, which searches the merged Rolodex and so beats the
system dialer's — and Google Phone takes the call. `CALL_PHONE` should be added to the policy's force-granted
list so dialling is dialog-free like the rest.

**What Post keeps:** the call in the merged timeline, call-log deletion, blocking, the keypad, and a summary
note offered afterwards. **What it never builds:** `InCallService`, a ringing screen, in-call controls.

#### Taking Call Notes when it exists, without depending on it

Not taking the dialer role means Google Phone keeps recording and — on hardware that has it — transcribing
and summarising. That content is worth having in the timeline, and there is exactly one door: **the share
sheet**. So Post declares an `ACTION_SEND` / `text/plain` intent filter and becomes a share target. Sharing a
Call Notes summary out of Phone drops it onto the matching correspondence, matched to the most recent call
from that number within a short window; ambiguous shares ask which correspondence rather than guessing.

Three properties make this safe to build now:

- **It degrades to nothing.** If Call Notes is absent — likely on the Pixel 8a, which is the owner's current
  device — no share ever arrives and the manual note is simply what happens. There is no broken state and no
  empty affordance to explain.
- **It is not a dependency.** The manual note is the primary path and stays the primary path. The share target
  is an *additional* way to fill the same field.
- **It survives the upgrade.** The owner expects to move to newer hardware eventually, at which point this
  starts working with no further code.

The one thing it must not do is imply a capability the device lacks: Post never advertises "get the transcript
from Phone" as a button, because on the 8a that button would go nowhere. It accepts a share if one comes.

Marking a call as spam after taking it still does three things: delete it from the call log, block the number
through Android's own list, and remember it locally as a backstop.

**Consequence for §5:** Release C largely disappears, and with it the phase's only irreversible trade.

### 2.3 Email — move to the Gmail API

**Decision:** replace MailKit/IMAP-with-an-app-password with the **Gmail API through the existing
`Storyworthtelling.Google` sign-in broker.**

Three reasons, in order: Google keeps narrowing app passwords and this removes that dependency entirely;
the API gives a real trash operation, which is what "filing deletes at the source" needs; and it gives
**server-side search**, which is how the owner's "search everything" works without Post indexing years of
mail locally. It also removes a credential from the vault.

Accepted costs: an OAuth client registered against Post's package and signing certificate, and living in
unverified test-user mode indefinitely — acceptable for a single-user personal app.

Filing an email moves it to **real Gmail trash** (recoverable 30 days). Compose is a **full-headers**
composer — To/CC/BCC, subject, body, attachments — sending **plain text**, reading both. HTML mail is
rendered properly (§2.7). No labels; inbox and sent only. One account.

### 2.4 Voicemail — retire the scraper, use Fi

**Decision:** turn on Fi's **"Voicemails by text"** and parse those SMS into `Voicemail` timeline items,
rendered as voicemails so they thread with that person's calls.

This is the single cleanest simplification in the phase, and it exists only because the owner is on Fi.
`VoicemailNotificationParser` and the notification-scraping path for voicemail are **deleted**. The listener
service itself survives for RCS (§2.1).

### 2.5 The timeline — rebuilt, with tombstones

**Decision:** rebuild the merge model rather than extend it.

`TimelineMerger`, `IdentityResolver` and `NearDuplicateCollapser` were built for a world where texts arrived
from two sources at once and had to be de-duplicated. Once Post holds the SMS role that problem largely
evaporates, and three genuinely new requirements arrive that the current model has nowhere to put: **filing**,
**ghosts**, and **classification**. What is kept is the idea — one thread per person, four channels
interleaved — and the discipline of pure, unit-tested logic in `Codex.Domain`. `IdentityMap`'s
auto-from-Contacts-with-manual-override behaviour is kept as-is; it is already right.

**The tombstone rule — the load-bearing invariant of the whole phase.**

Filing means *delete at the source, keep a ghost copy in Post.* That directly contradicts how
`CommunicationSync.RunReconcileAsync` works today: it mirrors Gmail, so a message deleted at the source is
dropped from the cache. Ghosts and mirroring cannot both be naive.

The resolution:

1. Filing writes a **tombstone** and hides the item immediately. The ghost goes to the archive store.
2. The source delete is attempted, and **retried** while the tombstone stands.
3. Reconcile mirrors the source **except** where a tombstone says the owner filed it — so a
   not-yet-deleted message is never resurrected into the Mailbox.
4. When the source confirms the delete, the **tombstone is dropped**: the item is genuinely gone from the
   source, reconcile cannot bring it back, and the ghost lives on in the archive.

So a tombstone is *temporary, and exists exactly as long as the source and Post disagree.* This is pure
logic and will be unit-tested as such. A source delete that keeps failing raises a dog-ear naming the source
and the error; the item stays filed regardless, because the owner's intent stands.

### 2.6 Storage

SQLite holds **metadata**; bodies and attachments are **files** under app-private storage, referenced by
path — the pattern `FileSystemImageStore` already uses. Files are swept when their message is filed or ages
out. `timeline.db` is **migrated in place** so existing history survives, and its name is added to the
frozen-invariants guard. Target scale: hundreds live, thousands filed.

The **archive store is separate** from the live cache, and is **backed up to Drive as plaintext JSON**
(debounced, on change) — the audio-library pattern. One factual note for the record: this places the full
text of the owner's correspondence in Drive unencrypted, unlike the vault, which is AES-GCM encrypted. That
was the owner's explicit choice; the alternative (reusing `SecretsCrypto` with the vault passphrase) remains
a one-line change if it is ever reconsidered.

### 2.7 The shared web view

**Decision:** extract the hardened WebView out of Flyleaf's `RestrictedBrowserActivity` into
`Codex.Ui.Android` as a shared component, used by **both** the restricted browser and Post's HTML mail
renderer.

This is the owner's "use the Flyleaf web viewer if possible", made structurally honest: one browser engine
configuration, one place to harden, both heads inheriting every fix. Post does not get a second, separately
configured WebView. Links inside mail are intercepted and handed to the restricted browser rather than
navigating in place; remote images load (the owner's decision, accepting tracking-pixel exposure); the
domain is shown in mono beside link text as an anti-phishing cue.

---

## 3. Cross-app handoff — the recommendation

The owner deferred the mechanism and then chose **an Android intent**. There are two consumers: **Index**
(balance emails, thread #4) and **Foreword** (calendar events, which does not exist until P9).

**Recommendation: a signature-permission-guarded explicit intent, and no queue.**

- **Explicit component target** — Post names Index's receiver directly rather than broadcasting, so nothing
  else can intercept it.
- **Signature-level custom permission** — Index declares `protectionLevel="signature"`, so only an app signed
  with the shared repo keystore can send it. This is free: every head already shares one signature for the
  update channel. Without it, any app on the phone could feed Index fabricated balances.
- **Payload: balance and date only.** Deliberately minimal.
- **No outbox.** If Index is not installed or the send fails, Post drops it silently — Index will see the
  email again on a later sync. This is the owner's call and it is the right one: a durable retry queue is
  real machinery to maintain for a case that self-heals.

**Calendar events do not use this path in P7.** Foreword is P9. Post **writes the event itself** for now
(provider to read for conflict checks, API to write), and hands the job over when Foreword lands. Detection
and conflict-checking ride in the same Claude call as classification, so they cost nothing extra.

---

## 4. What the guard test must assert

Extending `Codex.Tests/Guard/FrozenInvariantsTests.cs`, in the spirit of Score's additions:

- **The app id** — exactly one csproj declares `com.storyworthtelling.focusphone.communication`.
- **The prefs migration path** — the literals `account` and `emailSync` still appear in the migration code, so
  the one-time copy-across to the postal names cannot be deleted by accident, orphaning the credentials.
- **`timeline.db` by name** — the ghost archive makes it genuinely irreplaceable.
- **Role declarations** — the manifest components `ROLE_SMS` requires. Losing one silently *disqualifies*
  Post from the role, which is the same class of bug as the device-admin receiver in P5. (`ROLE_DIALER` is no
  longer taken, per §2.2, so nothing is asserted for it.)
- **The share-target filter** — the `ACTION_SEND` / `text/plain` filter that lets Call Notes reach Post.
  Silent to lose and invisible when missing, which is exactly the kind of thing a guard is for.
- **The frozen listener component name** — `…communication.MessageBridgeListener`, now that it is kept for RCS.

The rename is otherwise full, as Score's was: folder `Post.Android`, namespace `Post`, label "Post", app id
untouched, its own `build-post.yml`, versionCode floored above the old counter, display version stepping to
**1.0.x**.

---

## 5. Release shape

Three releases, the riskiest thing last and alone.

| | Contents | Why here |
|---|---|---|
| **A** | Rename + guardrails + guard test · **`ROLE_SMS`** · **Gmail API** · plain compose and real delete on both · kit adoption and restyle · Areas breakup | Delivers the live pain (§0) as early as it can be delivered. Deliberately *minimal*: no attachments, no filing, no ghosts. |
| **B** | Filing, tombstones and the ghost archive · classification and ageing · calendar detection · the Index handoff · attachments · Drive archive backup | The to-do-list model, on a base that already sends and deletes. |
| **C** | Call polish: the summary note, spam marking, call-log deletion | Small, and no longer isolated for risk — §2.2 was revised so Post never takes the dialer role, which removed the phase's only irreversible trade. Foldable into B if it stays this size. |

---

## 6. Open risks

Honestly stated, in the order they would hurt.

1. **The role-consent dialogs may not appear under the device-owner lockdown.** (F2) This is the single
   largest unknown and the whole architecture rests on it. Mitigation agreed: Flyleaf's admin mode
   temporarily relaxes the policy to accept the dialog, then restores it. If that fails too, Release A's
   value collapses to email only. *Verified on device, not before.*
2. ~~**Call screening may not survive the dialer role.**~~ **Retired.** Post no longer takes the dialer role
   (§2.2), so screening and recording stay with Google Phone and this risk is gone.
3. **The bridge dying** — the owner's own pick for likeliest. Mitigated by the native spine (texts keep
   flowing), the retained listener (some RCS survives), and a dog-ear naming the bridge and the error text
   exactly. Not silent.
4. **Losing a role later** — a policy pass, an OS update, a reset. Guarded by an `IsRoleHeld` check on every
   launch, a dog-ear, and a one-tap re-request.
5. **`gmbridge` required at build time** costs local compilation of the head. (§2.1) Mitigated structurally,
   not eliminated.
6. **Classification mistakes age something real out in a day.** Mitigated by promotion being permanent —
   correcting a sender teaches Post for good — and by bills never ageing out at all.

The owner has chosen to **write the plan without prototyping first**, recording these as risks for the
on-device test plan to settle.

---

## 7. What this supersedes

- **Roadmap #15** — the async-sync half is *already landed* (P2): `CommunicationSync` has incremental and
  reconcile modes behind a 30-minute worker, and `ThreadsActivity` already loads off the UI thread with
  300-row paging and a generation counter. Verified, not re-planned. The navigation half is P7's, and the
  649-line `ThreadsActivity` / 504-line `ThreadActivity` are broken up by the Areas structure.
- **Roadmap #4** — narrowed and sharpened. **Balances** cross to Index; **bills** stay in Post showing amount
  and due date. Parsing is general, not limited to Evergreen Home Lending and Cobblestone Finance. Post parses, Index consumes.
- **CLAUDE.md's note that `GmBridgeClient` "compiles to a stub"** — true locally, but CI has always built the
  `.aar`, so shipped APKs carry the real bridge. After this phase the stub path is gone entirely.
- **The design interview's "call transcript summary"** — not deliverable (F3). Replaced by the owner's own
  note on the call.
