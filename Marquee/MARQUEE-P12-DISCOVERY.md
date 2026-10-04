# P12 discovery — decisions log

Working name **Marquee** (under review — see D4). This file records the gate-1 discovery answers
as numbered decisions. **Invented data only:** no real subscriptions, titles, viewing history or
accounts are ever written here; where a decision concerns the household's real services it
records the *shape* ("picked in Settings, stored in Drive"), never the list.

Target: ~200 questions before mockups (owner, round 1).

## Orientation corrections

- **Streaming apps are not suspended on the locked phone today.** The orientation report said they
  were, reading `AppCategorizer` (fail-closed: http-VIEW handlers blocked, Unknown/Mature rating
  quarantined). That class is unit-tested but **no Android head calls it** — the live policy pass
  suspends only browsers, other launchers and the `HardBlock` never-visible categories. The owner
  confirmed streaming apps run. A deep link into one is therefore plausible on the owner's phone
  as the lockdown stands; whether it stays so is a decision below, not an assumption.

## Decisions

- **D1 · Services in scope** — the household's services are **picked in Settings** from the
  provider's catalogue and stored in the household's Drive data, never in source.
- **D2 · Hand-off and playback** — streaming apps should be **hidden (off the Flyleaf tray) but
  available** on the owner's phone. The owner's **preference is playback inside the app**, while
  accepting it is likely impossible; gate 2 must measure in-app playback honestly (embedded web
  players, DRM, terms) before hand-off is settled as the answer.
- **D3 · Branch** — work moves to a `claude/` branch (`claude/marquee-p12`) so the existing
  branch-test triggers apply, at the owner's direction.
- **D4 · Name** — "Marquee" is not settled; the name gets its own review in discovery.
- **D5 · Name direction** — none of Frontispiece / Gatefold / Vignette / Marquee landed; explore
  **framing, serials, pulp**.
- **D6 · Home and areas** — the home area is **Continue Watching**; **Playlist** and **Catalogue**
  are further binder tabs. (Replaces the roadmap's "tonight answer": there is no pick-for-you
  screen; what you're partway through is the front page.)
- **D7 · Viewers** — **both installs, as separate viewers**, each with their own watching state:
  what one person watches on their own service profile is theirs. The household also has
  **children's profiles with no devices of their own**, so an install can **switch to a child**,
  and a child's view carries **parental filters**. (Technical consequence for gate 2: no service
  publishes a per-profile watch-history API, and deep links do not choose a service profile —
  how Marquee knows what was watched is a decision below, not an assumption.)
- **D8 · Reach** — the streaming apps leave the Flyleaf tray and are reachable **only through
  Marquee**, so every arrival is at a specific title, never an app's home feed. (A Flyleaf change,
  carried in this phase's plan.)
- **D9 · Name direction, narrowed** — keep exploring along **pulp** (the cheap serial fiction
  press), not Serial / Passe-partout / Feuilleton.
- **D10 · Watched state** — the app **is the front page**, so it **learns history from its own
  use** (what was handed off, as whom), and **manual marking** is always available beside it. No
  history import, no third-party tracking service.
- **D11 · Choosing the service profile** — not settled by a sentence on the hand-off. Gate 2 must
  **investigate per service** whether any route selects (or lands on) the right service profile,
  accepting a different approach per app. The owner offered the household's subscription list;
  the survey covers the major US services regardless, so the list stays out of the repo.
- **D12 · Children's filters** — **rating ceiling per child, plus a parent's allow-list (titles
  above the ceiling approved for that child), minus a parent's block-list** (titles hidden even
  under the ceiling — the owner's example was a named preschool series). Block beats allow beats
  ceiling.
- **D13 · The TV** — ideally the app also becomes the **home screen of the household's Google
  Chromecast streaming stick**. Keep it in view from the start: a TV head is D-pad-driven, is where
  watching actually happens, and changes the hand-off and MAUI-vs-native questions. (Gate 2:
  whether a third-party app can be the home on Google TV, and what that costs.)
- **D14 · The device** — a **Chromecast with Google TV** (Android TV underneath; the app can be
  installed on it).
- **D15 · Phone and TV are equal partners** — both are full apps over the same data, and the phone
  **casts to the TV seamlessly**: picking a title on the phone starts it on the TV. (Gate 2: a
  third party cannot drive a service's own Cast receiver, so "seamless" most plausibly means the
  phone hands the title to *this app's own* TV head — Cast Connect or a LAN message — which then
  deep-links into the service on the TV. To be measured, not assumed.)
- **D16 · Name** — **Pulp**, by the owner's choice after three rounds (Marquee → framing/serials
  → pulp). Working name from here; the folder and identifiers follow once frozen at gate 2.
- **D17 · Viewers per device** — a **phone is its owner's viewer** (plus switching to a child);
  the **TV opens on "who's watching"** over every household viewer, children included.
- **D18 · Playlists** — **named playlists**, as many as wanted; each is personal or shared.
- **D19 · Discover (was "Catalogue")** — renamed **Discover** and **modelled on Score's Discover**:
  **search is a must**, plus a **curated-to-taste stack of movie/TV posters**, and **a trailer
  plays with one tap** from there. (Gate 2: trailer source and whether it can play in-app.)
- **D20 · Not on our services** — say where it streams instead and **offer rent/buy**; it can still
  go on a playlist, where it waits, marked.
- **D21 · New episodes** — the show **surfaces on Continue Watching**, and once a **release date is
  set the episode is added to a calendar**, so **Foreword has already printed it** on the morning
  page. No push.
- **D22 · The stack** — **weekly, Friday replaces it**, nothing carries over; a veto strikes the
  poster through and "Bring it back" lifts it; vetoes feed the taste profile (Score's model).
- **D23 · Whose taste** — the **device owner's**. **Children's viewing preferences are not
  tracked** — no taste profile for a child, ever.
- **D24 · Learning** — **passive, from what's watched, plus the stack's vetoes**. No ratings.
- **D25 · Release calendar** — **one household Pulp calendar**, shared to both accounts; Foreword
  maps it (to everybody, or as the owner chooses on its Columns screen).
- **D26 · The TV's viewer** — the TV **opens at "who's watching" every time it powers on**, and its
  stack follows whoever was picked (an adult's own stack; a child, having no taste profile per
  D23, gets none). *Read from the answer; confirm at the mockup gate.*
- **D27 · A child's history** — **progress only**: a child's Continue Watching remembers where
  they are; never used for taste; old entries age out.
- **D28 · Two homes for one title** — open on the service with the **best quality** (resolution,
  then fewer ads — see D29).
- **D29 · Ad tiers** — Settings records **each service's plan tier**; a title shows **"with ads"**
  where that applies, and the tier breaks ties in D28.
- **D30 · Finished** — **try to learn it from the service/device first**; **fall back to assuming
  watched once handed off**; the viewer can **mark unwatched**. (Gate 2 candidates: there is no
  public history API, but Android exposes the *active media session* of other apps to a
  notification-listener app — title, position, duration — on the phone and on Google TV; Google
  TV's "Watch Next" row is privileged and likely unreadable. To be measured.)
- **D31 · Staleness** — a show **stays on Continue Watching until removed**.
- **D32 · Upcoming titles** — playlists may hold unreleased titles, **dated and dim (~45%) until
  release**; the date goes to the Pulp calendar (D25).
- **D33 · Shared playlist editing** — **both adults equally**; last edit per item wins; removals
  are undo-first.
