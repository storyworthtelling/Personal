# P14 · Watch — the owner's answers (gate 1, designer round)

Recorded 31 Oct 2026, from 204 chip questions asked before any finding or build. Each answer is
numbered **A1…** so the technical finding, the plan and the design notes can cite it. Nothing here
is reopened without the owner; where an answer may be technically impossible, the finding says so
and brings it back (marked **⚠ finding**). No personal data: people are Sam, Lee, Robin, Juniper.

## 1. The shift in scope

- **A1 — P14 is mainly Score and Post on an LTE watch, so the phone can stay home.** "Focus on the
  world rather than the technology" continues: the watch is for staying reachable and listening to
  music or podcasts, and little else. The reMarkable already carries most of what Constitution does.
- **A2 — Constitution earns a place through one case:** the planned workout drives Score's tempo on
  the wrist, the watch records effort, and the watch replaces Fitbit as the Body data source (§5).
- **A3 — Outings without the phone:** whole days, work, kids' activities, errands and church, runs,
  walks and the gym.

## 2. Hardware

- **A4** — Owner: Pixel Watch 2, LTE, on Google Fi with the phone's number shared; upgrading to the
  newest Pixel Watch next year. Partner: a newer Pixel Watch (believed PW4), paired to her own phone.
- **A5** — Target **both** PW2 and PW4 (lowest common Wear OS). Mockups show both sizes.
- **A6** — Worn on the left wrist, day and night except while charging (in the evening). Pixel Buds
  for audio. The owner's watch already works paired to the locked FocusPhone.
- **A7** — Battery: aim for 24 h with everything on; charging is the owner's problem, not the
  design's. Low battery gives up the always-on face and the email lines first; system low-battery
  notice only.

## 3. Post on the watch

- **A8** — Reachable by anyone in the Rolodex. Calls, SMS and group texts, email, voicemail.
- **A9 — Post must own the SMS route on the watch** (not Google Messages as transport). ⚠ finding:
  if it cannot, bring it back with evidence; do not fall back silently. Calls: the finding decides
  whether the system Phone app rings.
- **A10** — Texts land on both devices, in sync. Start new texts as well as reply; group texts read
  and reply-all; photos shown. Reply by voice dictation or keyboard.
- **A11** — Email: subject and sender only, people only (Post's sorting), never vibrates. The watch
  fetches through the Gmail API itself over LTE.
- **A12** — Every text vibrates; calls ring and vibrate; unknown numbers ring only if they call twice
  in a few minutes. Overnight: favourites (starred in the Rolodex) and repeat callers only.
- **A13** — Calls use the earbuds when paired, the watch speaker otherwise. Placing a call: the whole
  Rolodex, or by voice ("call Lee"). Voicemail: transcript and audio both. The call note is dictated
  on the watch.
- **A14** — Post's first screen is **People waiting**. Filing from the watch is allowed; deleting is
  undo-first; read on the watch counts as read on the phone.
- **A15** — With the phone at home and off, calls, texts, music and email lines must all still work.
  No message when LTE drops.
- **A16 — RCS is turned off for the number** (settles Post's outstanding owner decision; owner
  action: turn off chat features).

## 4. Score on the watch

- **A17** — Same as the phone except Discover; everything streams, nothing downloads. The whole
  library is visible. Streaming over LTE freely, prefetching the next track.
- **A18** — One queue shared by both devices; podcast lists curated on either and synced; plays from
  the watch count toward the taste profile; phone's speed and skip-silence settings apply.
- **A19** — Browse by artist, album, playlist, genre, song and voice search. Controls: play, pause,
  skip and volume on the crown. No lyrics. Home screen is Now playing.
- **A20** — Either device can control the other. The one paired to the headphones plays; whoever
  starts playback owns it, with a button to switch; moving mid-song is a tap.
- **A21 — Tempo is a key feature.** Cadence sets BPM for running, walking, cycling and rowing (watch
  motion only); strength follows the plan's band; when cadence and plan disagree, cadence wins;
  changes crossfade at once over 5–6 s.
- **A22** — TIDAL: the household is believed to be on the Family plan; one login per person (her
  token in her personal vault). ⚠ finding: if TIDAL's sanctioned player cannot run on Wear OS,
  bring it back.

## 5. Constitution on the watch

- **A23** — During a workout: rest timer (its own buzz), sets/reps counter (tap anywhere), and above
  all **recorded effort**: heart-rate zones, duration against plan, pace and cadence. Running shows
  cadence, live HR and distance. GPS records distance only, no route.
- **A24** — Effort zones from history plus an age-based maximum; the birthdate lives in the personal
  vault. Only **worrying** overexertion buzzes (above max zone, or HR not recovering in rest), with
  the plain words "Ease off". Everything else goes to the digest and shapes future plans. No
  "not medical" line.
- **A25** — The watch is secondary evidence beside the ink; disagreements are noted in the digest,
  calmly. The owner taps Start; the watch shows the morning's skips and swaps. No unplanned sessions.
- **A26** — The watch needs the exercise list and sets/reps, rest lengths, tempo bands and target
  duration and zone. During a workout only calls come through; texts arrive in rests.
- **A27 — Fitbit is no longer needed and never visible.** Constitution on the watch records sleep,
  steps and resting HR all day; it may use Fitbit or Health Connect in the background if that is
  easier. Sleep: watch and bed both, compared. ⚠ finding: battery and scope cost; P13 made writing to
  Health Connect a non-goal.
- **A28** — Outside workouts, one silent card: a reminder to log food when away at the dinner hour.
- **A29** — Body values on the watch: live HR during a workout only. Session data goes watch → phone
  only, over the Wear Data Layer (cloud route when apart), and is deleted from the watch once handed
  over. Her sessions go to her phone only.

## 6. Face, lockdown and the watch's own language

- **A30 — The face is the Flyleaf analogue, and that app enforces the lockdown** (a face plus a hidden
  Flyleaf-watch service if Wear OS keeps faces code-free; Watch Face Format for the face is fine).
  A watch reset is acceptable only if the number and LTE survive the re-pair.
- **A31** — Blocked: browser and news, Play Store, and notifications from outside the suite. The
  allowlist is edited **from the phone** in Flyleaf's Admin mode; there is no admin on the watch.
  Allowed so far: Wallet, Assistant/Gemini (general), Clock alarms, Google Maps, SOS and fall
  detection. Fitbit hidden. Fitbit's nudges and celebrations off.
- **A32** — Updates arrive both pushed from the phone and pulled from the `apps-latest` release.
- **A33 — The watch has its own design language, sharing nothing with Paper & Ink**, in its own file
  with its own token files and leak guards. Phone app names stay bookish; watch apps get new names,
  the same on her watch; names come from an exploratory round.
- **A34** — Face: time and date only; nothing anxious (no counts or unread marks). Always-on, very
  dim. Analogue, plus abstract studies: a sun/daylight arc, sparse marks, and **polygons whose sides
  give the hour (1 = dot, 2 = line) and whose hue gives the minute** (a full colour wheel each hour).
- **A35** — Gestures on the face (swipe, long-press, double-tap) if Wear OS allows; otherwise tiles
  and the crown. Side button stays default. Tiles: Now playing (Score), People waiting (Post).
  Four buzz patterns: call, text, ease off, rest end.
- **A36** — Mood: calm, then eyes up. Glass with light through it leads; stone and slate second.
  Sundial and pocket field kit as seeds (a scout turned manager); map and compass, trail blazes and
  merit badges as motif only (nothing is earned — no streaks, goals or scores). Gently follows the
  time of day. Abstract or illustrated, studies decide.
- **A37** — No voice seats on the watch. One short sentence at most; relative times; errors are a
  quiet line in the app, never a buzz.

## 7. Keys and safety

- **A38** — Standalone apps take the **vault passphrase once on the watch** and keep it encrypted at
  rest (Android Keystore), never logged. A lost watch is wiped remotely. Screen lock stays default.
  Message text on the watch: whatever is needed.

## 8. Her watch

- **A39** — Score and Constitution only (her phone runs Score too); no Flyleaf face, no lockdown.
  Workout and effort features the same as the owner's.

## 9. Process

- **A40** — Stack: **.NET only**. Device checks by the owner from a committed test plan; real watch
  only, no emulator.
- **A41** — Release order: face and lockdown first, then Post. **A PR per release.**
- **A42** — Before build the owner approves face studies (8–10, both sizes, real size and enlarged),
  a watch design-language book, screen mockups per app, and names and icons (hand-drawn studies
  before IconStudio). **Next step: face studies, then the technical finding.**
- **A43** — Post's open device risks (role consent under the lockdown) are listed as P14
  dependencies.

## 10. Carried items

- **A44** — Pulp's main-build failure on the vault-ownership change is fixed first, in its own small PR.
- **A45** — D152 stays: the Sunday deadline waits up to 12 hours for the lease claim.
