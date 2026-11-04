# P14 · Watch — technical finding (gate 1)

**Status:** draft for the owner, round 2 (Q1–Q4 answered; headline 1 corrected). It answers what the owner's 57 answers (A1–A57, in
[`P14-OWNER-ANSWERS.md`](P14-OWNER-ANSWERS.md)) need from the platform, and says plainly which of
them the platform does not allow. Design is in [`WATCH-DESIGN.md`](WATCH-DESIGN.md) (approved, A58).
Owner decisions are **Q1–Q9** in §9. Nothing here has been tried on a watch: every claim is from
documentation or a search on 3 Nov 2026, and is marked *documented* or *unverified*.

People in examples are invented (Sam, Lee, Robin, Juniper).

---

## 0. The headline findings

1. **Calls away from the phone work on Fi (corrected).** The first draft said they could not, on the
   strength of Fi's wearables help page ("Voice calling over LTE is not supported"). The owner's
   own LTE Pixel Watch already makes and takes calls on Fi without the phone, which matches Google's
   Pixel Watch page ("Make calls without your phone", LTE only). The Fi page is out of date or
   describes another case, and the owner's use is the evidence (A59). Calls ring through the
   watch's own Phone app, so **Q1 is settled: the system Phone app rings, and Dash logs the call
   and asks for the dictated note afterwards (A13).**
2. **How texts reach the watch on Fi is the open question.** Fi's same help page says: "Texting syncs your
   watch and phone together using the Google Messages app over the Internet", and the phone must be on
   and online, though it need not be nearby (documented). Post holds the SMS role on the phone, and
   whether Fi's watch texting works at all when Google Messages is *not* the default SMS app is
   unverified. Since that page was wrong about calls, it may be wrong about texts too. **The owner
   chose to test the carrier first (A60):** a device spike finds out whether Fi delivers texts to
   the watch with the phone off, and whether a third-party app (Dash) can be the watch's default SMS
   app.
3. **If the spike says no, the fallback is a route where Post owns texting (A9):** Dash on the watch talks to **Post on
   the phone**, over the Wear Data Layer, which falls back to Google's cloud relay when Bluetooth is
   out of range. Google documents both routes as end-to-end encrypted. Post stays the one place
   texts are sent and recorded. The cost: **the phone must be on and online at home** (A15's "phone
   off" case fails for texts and email), and every text crosses Google's relay.
4. **The face cannot enforce anything.** A Watch Face Format face is resources only (`hasCode=false`),
   and the store refuses one with code (documented). Point is therefore two packages: the face
   (generated XML) and a hidden **Point service** that enforces. That is the fallback the owner
   already chose (A30).
5. **Whether a Pixel Watch accepts a device owner at all is unknown.** On a phone, device owner can be
   set only on a device with no accounts, after a reset. No source covers Wear OS. Without it, the
   watch lockdown is curation: apps hidden and notifications switched off by hand, with nothing
   enforcing them. Silent self-updates (A32) also depend on it (§5). A device spike answers it:
   **Q5**.
6. **TIDAL has no official Wear OS player.** The only Wear OS TIDAL clients are unofficial, and
   Score's rule since P6 is that playback goes through TIDAL's own player module. So **TIDAL on the
   watch is blocked** until TIDAL supports Wear OS; Drive music and podcasts are not affected:
   **Q4** (the owner asked for this to come back, A22).
7. **.NET only is possible, with one alpha dependency.** A Wear OS app is an Android app, so .NET for
   Android builds it, and Microsoft publishes bindings for the Wear libraries. The heart-rate and
   cadence path (Health Services) has only a **prerelease** binding,
   `Xamarin.AndroidX.Health.ServicesClient` 1.1.0-alpha03, published from Microsoft's
   `dotnet/android-libraries` (documented). It qualifies under CLAUDE.md's dependency rule (a trusted
   publisher, and required for the service), but it is alpha: **Q6**.

---

## 1. What the watch is for (A1–A3)

The phone stays home. The watch keeps the owner reachable (Dash), plays music and podcasts (Wave),
records effort and the day (Beat), and tells the time while hiding everything else (Point). The
reMarkable remains the working surface for Constitution: Beat records, it does not plan or show
plans beyond the session in hand (A25, A26).

## 2. Hardware and OS

- **Owner:** Pixel Watch 2, LTE, on Fi, paired to the locked FocusPhone; upgrading next year.
  **Partner:** a newer Pixel Watch (believed PW4), paired to her own phone (A4).
- Pixel Watch 2 received **Wear OS 6** (Android 16) in October 2025 (documented, build
  BP3A.250905.014.W3). Both watches are on Wear OS 6 or later, so the apps target **API 36**, and the
  face can use Watch Face Format **version 2**.
- Both models share the phone's number on LTE when the carrier supports it.

## 3. Each app against the platform

### 3.1 Dash (Post on the wrist)

| Need | Route | Verdict |
|---|---|---|
| Texts while out, Post owning them (A9, A10) | either Dash as the watch's own SMS app (spike), or, as the fallback, Dash ↔ Post over the Data Layer with Post sending and recording on the phone | One of the two; the spike decides |
| Texts with the phone **off** (A15) | if Fi delivers SMS to the watch itself, Dash would need to be the watch's default SMS app (one third-party app claims this works; unverified) | **Device spike first** (A60) |
| Calls while out (A13) | the watch's own Phone app rings over LTE with the shared number; Dash logs the call and takes the dictated note | **Works today** on the owner's watch (A59) |
| Group texts and photos (A10) | Post already composes group MMS; Dash sends the request and Post sends it | Feasible |
| Email subject and sender, people only (A11) | Gmail API from the watch over LTE, or Post relays the lines over the Data Layer | Feasible; Q3 decides which |
| Voicemail, both ways (A13) | Fi's voicemail-by-text is a text, so it follows the text route; audio is a link | Feasible for the transcript; audio unverified |
| Filing and undo-first delete (A14) | Dash asks Post, which already files with tombstones | Feasible |
| Dictation and keyboard (A10) | the system's own input (RemoteInput); no microphone permission in Dash | Feasible |

**The relay cuts both ways:** with Post as the one writer, nothing falls out of sync between the
watch and the phone (A10's "both, in sync"), which number sharing with a second SMS app on the watch
would make hard. Even on a carrier with number sharing, the relay may remain the better route for
texts, with the watch's Phone app used only for calls.

### 3.2 Wave (Score on the wrist)

| Need | Route | Verdict |
|---|---|---|
| Drive music, streamed (A17) | Drive API from the watch over LTE; prefetch the next track | Feasible; needs a Drive token on the watch (§4) |
| Podcasts, curated on either device (A18) | feeds fetched on the watch; queue and positions shared with Score over the Data Layer | Feasible |
| TIDAL (A17, A22) | no official Wear OS player, so Wave remote-controls TIDAL in Score on the phone when the phone is with the owner (A62) | Remote only; no TIDAL with the phone at home |
| One queue across both (A18, A20) | the Data Layer carries the queue; whoever started playback owns it | Feasible; the playback switch is new code |
| Tempo from cadence, crossfading (A21) | Health Services gives steps per minute during an exercise; ExoPlayer/Media3 crossfade | Feasible with the alpha binding (Q6) |
| Listening counted in the taste log (A18) | signals sent to Score on the phone over the Data Layer | Feasible |

### 3.3 Beat (Constitution on the wrist)

| Need | Route | Verdict |
|---|---|---|
| Live HR, cadence and distance in a session (A23) | Health Services exercise client | Feasible (alpha binding) |
| "Ease off" buzz (A24) | zones from history and an age-based maximum, computed on the **phone** and sent as numbers; the watch never sees a birthdate | Feasible |
| Session to the phone, deleted after (A29) | Data Layer, falling back to the cloud relay | Feasible |
| All-day steps, resting HR and sleep, without Fitbit visible (A27) | Health Services passive monitoring covers steps and heart rate, but for sleep it reports only whether the wearer is asleep, **not sleep stages or a sleep score**. So either Fitbit stays, hidden, as the sleep pipe, or Beat records asleep/awake time and the bed carries the rest | **Partly** (Q7) |
| Writing to Health Connect | Health Connect lives on the phone, not the watch, and P13 made writing to it a non-goal; Beat's data goes to Constitution's own store instead | Changes P13's rule only if the owner wants it there |

### 3.4 Point (the face and lockdown)

- The face is Watch Face Format XML generated from `HourShape.For` (WATCH-DESIGN §13), so the face
  and its tests cannot disagree. It is its own package, separate from the Point service.
- **Gestures on the face (A35):** Watch Face Format offers taps on defined regions, not swipes or
  long-presses (the system owns those). The owner's fallback, tiles and the crown, applies.
- **Enforcement** needs the Point service to be device owner on the watch (§5).

## 4. Keys and sign-in on the watch (A38)

- Google's recommended order for a standalone Wear app is: tokens handed over from the phone app over
  the Data Layer, then OAuth with PKCE relayed through the phone, then Google Sign-In (documented).
- The owner chose the **vault passphrase typed on the watch** (A38). That works for what lives in the
  vault (Drive file ids, any future TIDAL token). It
  does **not** give the watch a Google access token, because Post and Score get theirs from the
  phone's account at runtime and no Google token is stored in the vault. So the watch still needs a
  Google sign-in or a token handed over from the phone: **Q3**.
- The passphrase is kept encrypted with the watch's Android Keystore and never logged (A38). Nothing in
  this phase asks for it, stores it in the repository or prints it.

## 5. The lockdown and updates

| Need | With device owner on the watch | Without it |
|---|---|---|
| Hide browser, news and Play Store (A31) | suspended, as on the phone | disabled by hand once (adb), not enforced |
| Only suite notifications (A31) | enforced | switched off per app in Settings by hand |
| Allowlist edited from the phone (A31) | Flyleaf sends the list; Point applies it | not possible |
| Silent updates from `apps-latest` (A32) | `PackageInstaller` without a prompt, as Flyleaf does | each update asks on the wrist, or adb |
| Pushed from the phone (A32) | the phone sends the APK over the Data Layer; Point installs it | same prompt problem |

- **Device owner on Wear OS is unverified.** The phone rule (no accounts, so a reset first) clashes
  with how a Pixel Watch is set up (paired through the phone, which adds the account). If it works,
  it is likely only as: reset the watch, set the owner over adb before pairing, then pair. Whether
  the LTE profile and the shared number survive that is exactly what A30 asks, and is also
  unverified.
- The watch has no Flyleaf of its own. The phone's Flyleaf keeps the policy (the allowlist, A31), and
  the Point service applies it.

## 6. Battery and permissions

- **Battery:** LTE streaming plus all-day sensing is the heaviest load a Pixel Watch takes. Twenty-four
  hours (A7) is unlikely on a PW2 with Wave streaming for hours, and the owner has said charging is
  their problem. Health Services batches passive data to save power, so all-day monitoring costs less
  than an open session. The always-on face is a few percent of pixels (round-1 studies).
- **Permissions, per app (proposed, Android 16 names):** Beat: `health.READ_HEART_RATE`,
  `ACTIVITY_RECOGNITION`, `ACCESS_FINE_LOCATION` (distance only, no route kept, A23),
  `FOREGROUND_SERVICE_HEALTH`, `POST_NOTIFICATIONS`. Wave: `INTERNET`, `FOREGROUND_SERVICE_MEDIA_PLAYBACK`.
  Dash: `INTERNET`, `POST_NOTIFICATIONS`, and no SMS or phone permission on the relay route. Point:
  device admin. Each manifest gets a permission-count guard, as Constitution's does.

## 7. What may never leave the phone, and what newly does

- **Body values** stay on the phone (D165), with one new outbound path: **the session's heart rate,
  cadence and distance travel watch → phone**, possibly via Google's encrypted cloud relay (A29). It
  goes onto `BodyStaysHomeTests`' allowlist as a named, deliberate path. Live HR on the wrist during a
  session is the only Body value shown on the watch (A29).
- **The birthdate** stays in the personal vault on the phone (A24). The watch receives only the zone
  limits, as numbers.
- **Texts and email lines** cross the relay on the Dash route. They are already in Google's systems
  (Fi, Gmail), but the relay is a new copy: worth knowing, and listed in Q2.

## 8. What can be verified here, and what only on a watch

**Here:** the domain logic (day colour, hour shape, zones and "ease off", cadence-to-tempo choice, the
relay's message contract, the queue merge), the generated face XML (validated), and compiling every
watch head with .NET for Android against API 36, including the alpha binding (if it restores here).

**Only on a watch (built, device check pending):** whether calls and texts reach the wrist on Fi under
Post's SMS role; the Data Layer cloud relay with the phone at home; device owner on a Pixel Watch and
what a reset does to LTE; the face rendering and always-on brightness; the four buzzes; Health
Services cadence accuracy; battery over a real day; Drive streaming over LTE; the watch sign-in.

## 9. Owner decisions

| # | Decision | Options the finding sees |
|---|---|---|
| Q1 | Calls away from the phone | **Settled (A59):** they work on Fi today; the system Phone app rings |
| Q2 | How texts reach the watch | **Settled (A60):** test the carrier first (device spike); the Post relay is the fallback |
| Q3 | Google sign-in on the watch | **Settled (A61):** the phone hands its tokens over the Data Layer |
| Q4 | TIDAL on the watch | **Settled (A62):** Wave remote-controls the phone's TIDAL when the phone is nearby |
| Q5 | **Device owner on the watch** is unknown | A device spike first (reset the watch, try, record what happens to LTE); or accept curation without enforcement |
| Q6 | **The alpha Health Services binding** | Accept it, pinned, with a guard; or write a thin Java/Kotlin shim (breaks ".NET only", A40) |
| Q7 | **Sleep without Fitbit:** Health Services gives asleep/awake, not stages | Keep Fitbit hidden as the sleep pipe; or the bed alone carries sleep |
| Q8 | **Where Beat's watch data lands** | Constitution's own store on the phone (as now); or also write it to Health Connect (reverses a P13 non-goal) |
| Q9 | **Phase shape** | Releases A–F as in the plan, starting with Q5's spike, then Point, then Dash (A41) |

---

Sources (fetched or searched 3 Nov 2026): Google Fi wearables help (support.google.com/fi/answer/15273704);
Pixel Watch 4G LTE features (support.google.com/googlepixelwatch/answer/13541778); Wear OS Data Layer
overview (developer.android.com/training/wearables/data/overview); Watch Face Format setup and codelab
(developer.android.com/training/wearables/wff/setup, developer.android.com/codelabs/watch-face-format);
Wear OS authentication (developer.android.com/training/wearables/apps/auth-wear); Health Services
(developer.android.com/training/wearables/health-services); NuGet `Xamarin.AndroidX.Health.ServicesClient`;
dotnet/android-libraries issue 825; Wear OS 6 on Pixel Watch 2 (androidcentral.com, Oct 2025); unofficial
TIDAL Wear OS clients (github.com/luchfilip/Tides-WearOS, github.com/keylimesoda/Untidy).
