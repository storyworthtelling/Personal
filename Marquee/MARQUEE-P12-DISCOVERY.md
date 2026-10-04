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
- **D34 · Trailers** — **play inside Pulp** (embedded player), on phone and TV.
- **D35 · Trailers are only a look** — watching one changes no taste and no history.
- **D36 · The title page** — **poster · synopsis · a voiced line giving the reason this title was
  chosen for you** (from the device owner's taste — a voice seat, D14-of-DESIGN rules apply:
  degrades to hidden) **· the rating and the reasons for it** (the content advisory, e.g. "for
  violence and language") **· runtime or episode count**. No cast, no scores. (Gate 2: a source for
  rating *reasons* — certification alone is common, the advisory text is not.)
- **D37 · Spoilers** — **no episode stills anywhere; official posters only**. Episode titles and the
  official synopsis are fine.
- **D38 · Voice seats** (closed list, DESIGN.md §14; errors never voiced): the title page's
  **why-this** line (D36) · the **Continue Watching greeting** · **empty states** · **a line per
  playlist**. Not the Friday stack opener.
- **D39 · A child's view** — **a gentle voiced line** (a greeting) is allowed for a child.
- **D40 · reMarkable** — nothing is sent; release dates reach the morning page through the Pulp
  calendar and Foreword (D25).
- **D41 · Why-this may name a past title** ("because you finished …") — it may read the device
  owner's own history back to them. (Only theirs: never another viewer's, never a child's, per D23.)
- **D42 · A child's view** — **no Discover stack**; the **same interface otherwise, filtered**
  (D12), with **playlists parents build for them** to browse.
- **D43 · Leaving a child's view** — choosing an adult from a child's view asks for a **parent
  PIN** (the owner's own phone needs none).
- **D44 · The hand-off for a child** — **blocked unless Pulp can confirm the service lands in a
  kids profile.** Honest consequence: if gate 2 finds no route to confirm that for a service
  (D11), a child cannot start that service's titles through Pulp at all — the finding has to say,
  per service, which side of this line it falls.
- **D45 · Who edits children's filters** — **either adult, from Settings**; changes sync to every
  device.
- **D46 · Storage** — **one shared household Drive file, merge-safe**: every change is a dated
  per-item record so two writers (and the TV) merge without losing an edit. Not a vault section
  (the vault is one-writer by design). (Gate 2: cross-account visibility of a file one account
  owns and shares to the other — the same unverified question Index carries.)
- **D47 · Encrypted** with the **vault passphrase** (`VaultEnvelope`), like Index's household file:
  viewing history and children's names are personal. The TV takes the passphrase once.
- **D48 · The TV's account** — the **owner's Google account**, already on the Chromecast.
- **D49 · Offline** — nothing: without network there is nothing to watch, so Pulp says so in one
  sentence.
- **D50 · Platform** — **native Android, one codebase, phone + TV heads** over `Codex.Ui.Android`'s
  Paper & Ink, with the rules in a pure Domain library both heads share. (So the app lives under
  `FocusPhone/` beside the other native heads, or beside them in its own folder — a gate-2 call.)
- **D51 · Region** — **US, English, fixed**.
- **D52 · Kinds of service** — **subscription streaming, free ad-supported, and live TV / sports**.
  Not library services. (Live TV is schedules, not titles: its own questions follow.)
- **D53 · Rent/buy** — offer **only the cheapest high-quality option**, one line, not a price list.
- **D54 · Live TV** — **followed teams and events**: upcoming games show with where to watch, go
  on the Pulp calendar, and sit on Continue Watching. No channel guide.
- **D55 · Sports are never spoiled** — **no scores, ever**. A game **always plays from the
  beginning** and stays **available as a recording until watched**. (Pulp records nothing itself:
  this rests on each service's start-over / cloud-DVR and on a deep link reaching the recording,
  not the live feed. Gate 2 must say per service whether that is possible; where it is not, the
  finding says so rather than hand off to a live feed mid-game.)
- **D56 · Which sports** — **US pro leagues, Olympics/international, men's and women's leagues,
  and some international leagues** (e.g. Australian rules football). Teams are picked in Settings
  and stored in Drive, never in source.
- **D57 · Channel links** — deep-link to the channel where the app supports it; otherwise open the
  service's live section and name the channel.
- **D58 · Old games** — leave Continue Watching **when the service's recording expires**; where
  Pulp cannot know, it asks "still want this?" after two weeks.
- **D59 · Sport for children** — **parent allow-list only**: a child sees a game only for a team a
  parent added for them.
- **D60 · Follows** — **per adult**; every followed game goes on the household Pulp calendar,
  labelled with who follows it.
- **D61 · Full games only** — no highlights, no condensed replays.
- **D62 · Phone tabs** — **six**: Continue Watching (home, identity hue) · Playlists · Discover ·
  **Sports** · **Calendar** · Settings (grey). Six is DESIGN.md §9's drawn ceiling (Score runs six).
- **D63 · The TV gets its own design** — not the phone's binder frame scaled up; drawn separately
  at the mockup gate.
- **D64 · Search by** — **title · person · mood/plain words · team · series or franchise**. Mood
  search is answered by the model over the shared vault Claude key (cost to be estimated at gate 2).
- **D65 · Search scope** — **your services first, then "elsewhere"** below a hairline, marked with
  where and the one rent/buy line (D20, D53).
- **D66 · Calendar tab** — a **dated list of what's coming** (releases on playlists and Continue
  Watching, followed games, plans), grouped by day; the same events as the Pulp Google calendar.
- **D67 · Sports tab** — **organised by league**: **highlights from across the league**, **full-game
  replays of followed teams**, **standings and playoff brackets**, **preseason and postseason
  clearly distinguished**. **Adding a team brings its whole league in** if not already followed.
  (Refines D61: no highlights *of a followed team's games*; league-wide highlights are wanted.
  Standings and highlights reveal results, which D55 forbids for followed games — reconciled in
  D70.)
- **D68 · Plans** — **"plan to watch" on a date**: from a title's action card; goes on the Pulp
  calendar for both adults.
- **D69 · Franchise order** — **release order only**.
- **D70 · The spoiler rule for sport** — while a followed team has an **unwatched recorded game**,
  that league's **standings and highlights are folded away** behind "you're behind on <team>",
  and open once the game is watched or dismissed.
- **D71 · Highlight source** — **official league YouTube channels only, played inside Pulp**
  (embedded, like trailers).
- **D72 · A child's sport** — the **full league view, for leagues a parent allowed** (refines
  D59 from teams to leagues; the D70 hold applies to the child's own followed games too).
- **D73 · Plans name viewers** — a plan picks who it's for, **defaulting to just the person
  making it**.
- **D74 · TV home** — after "who's watching": **Continue Watching in big posters**, then that
  viewer's playlists as rows.
- **D75 · Other TV apps** — reachable **only through Pulp's Settings**.
- **D76 · The voice on the TV** — **wanted**, but its form is open: the owner expects the **whole
  visual dictionary to differ on the TV**, so it is settled with the TV's design language.
- **D77 · Trailers never autoplay** — on the TV or anywhere; a trailer plays only when asked.
- **D78 · TV look** — **explored at the mockup gate** (two or three directions drawn).
- **D79 · A TV design document** — **DESIGN.md does not govern the TV** (nor, later, the watch).
  The TV gets **its own design document**, written and approved at the mockup gate. Light-only,
  the binder frame and the rest of DESIGN.md are not binding there.
- **D80 · Remote** — D-pad/select/back plus **voice search via the remote mic**, **long-press
  select = action card**, and **the phone as keyboard** for TV search.
- **D81 · Idle TV** — **photo-album screensavers from Google Photos**. (Gate 2: since 2025
  Google's Photos Library API only reads app-created content, so a third-party app reading the
  household's albums needs the Picker API; Google TV's own ambient mode already shows Photos
  albums natively, and may be the honest route.)
- **D82 · Cast gesture** — **a TV toggle in the header**: once on, every play goes to the TV until
  switched back.
- **D83 · TV off or busy** — **wake it (HDMI-CEC) and take over**, as Cast does.
- **D84 · Who's watching on a send** — **picked on the phone when sending** (you, together, a
  child — a child's send is subject to D44).
- **D85 · Phone as remote** — **basic play/pause via the playing app's media session**, where the
  TV exposes it (gate 2 measures it, across the device boundary).
- **D86 · Service setup** — tick the service, choose its **plan tier** (ads / no ads, 4K), and
  **map its profiles to viewers** (feeds D11, D44 and D28).
- **D87 · Cost** — Pulp keeps no prices; it **counts hand-offs per service and hands the usage to
  Index**, which shows it beside the subscription charge. (An Index change; its scope — this
  phase or Index's next — is asked separately.)
- **D88 · Adding viewers** — **Settings → Household, either adult**: name, adult or child, and a
  child's rating ceiling.
- **D89 · Avatars** — **drawn monograms**; no photos (no children's pictures in Drive).
- **D90 · Index usage, both halves now** — Pulp publishes per-service hand-off counts over a
  signature-guarded intent (the Post→Index pattern), **and** Index's receiver and its usage line
  beside the subscription charge are built in this phase. (Index needs its own mockup for that
  line.)
- **D91 · On the Flyleaf bookcase** — a **cover-out sibling book**, like Foreword and Index.
- **D92 · Which apps Flyleaf hides** — **exactly the services ticked in Pulp**: Pulp publishes their
  package ids; Flyleaf hides those and only those.
- **D93 · No fallback** — if Pulp is missing or broken, the streaming apps stay hidden. Accepted.
- **D94 · Her phone is unrestricted** — Pulp is a guide there; D92's hiding never applies to it.
- **D95 · TV updates** — the TV head **self-updates with one confirm** (the `SelfOnly` update role
  from `Storyworthtelling.Updates`), not device-owner provisioning.
- **D96 · Continue Watching order** — **most recently watched first**; new episodes and recorded
  games jump to the top on arrival.
- **D97 · Private viewing** — each viewer's **Continue Watching is private**; only shared playlists
  and plans are seen by both.
- **D98 · Watching together** — a hand-off or send with **both adults picked advances the show for
  both**; each still sees it on their own Continue Watching.
- **D99 · Watching ahead is called out — loudly** — on a together show, the one who watched ahead
  gets an **authorial-voice line shaming them for cheating**. A new **voice seat** (adds to D38's
  closed list); it is wit, not an error, so §14 allows it, and it degrades to hidden without a
  profile. Shown only to the one who is ahead.
- **D100 · Playlists hold** — **films, whole shows, games, and film series/franchises**. Not single
  seasons or episodes.
- **D101 · Watched entries** — **struck through in ink and kept**; "hide watched" filters them.
- **D102 · Where the cheating line shows** — on the cheater's show row **and on the TV** (at
  "who's watching", in front of everyone). *Read as: the TV line names the cheater; confirm in the
  TV mockup.*
- **D103 · Playlist order** — **hand-ordered**, drag to reorder; new entries at the bottom.
- **D104 · Model-made playlists** — **from a prompt and from a seed title** ("more like this"),
  resolved against real titles; anything the source cannot find is dropped (Score's `PlaylistPlan`
  rule).
- **D105 · No sharing outside the household.**
- **D106 · Stack size** — **configurable in Settings; default three films and two shows**.
- **D107 · Stack contents** — **anything**, taste first; titles not on our services carry their
  where/rent-buy line (D20, D53).
- **D108 · Two piles** — the stack is **split into films and shows**.
- **D109 · Adding from the stack** — "Add to…" opens an **inline playlist picker**, last-used first.
- **D110 · Turnover** — **Friday 6 am local**, built in the background (Score's
  `FridayPicksWorker` shape).
- **D111 · Vetoes are permanent** for that viewer unless lifted with "Bring it back".
- **D112 · A used-up stack** — a **voiced empty state until Friday**; no refill.
- **D113 · Model cost** — **no cap and nothing shown**; a rejected key is handled by credential
  health.
- **D114 · Cover** — **several cover studies at the mockup gate**; the owner chooses.
- **D115 · Hue follows the art** — the identity hue is **sampled from the chosen cover**, then
  checked against §3's ≥20° rule; no window is picked ahead of the painting.
- **D116 · TV banner** — designed **with the TV language** (D79), not a crop of the phone cover.
- **D117 · Title** — **"Pulp" alone**.
- **D118 · Cold start — copy Score** — no cover-load overlay: Pulp opens **straight onto its home
  area** with the **voiced greeting under the header**, hidden until composed (Score's
  `ScoreActivity` + `VoiceGreeting`, first-open-of-day context included).
- **D119 · Posters** — DESIGN.md §7's **artwork slot**: hairline keyline, never bled, bookplate
  fallback; list rows lead with a small poster.
- **D120 · Rating badges coloured by severity** — the role hues carry the rating's severity (the
  scale is mapped at the mockup gate, e.g. caution for teen, problem for mature). Documents and
  greyscale keep a non-colour cue.
- **D121 · Where-to-watch** — the **service's name in text**, never a third-party logo.
- **D122 · A failed hand-off** — a **Problem dog-ear** with what happened and what to do next;
  never a toast. (How Pulp even *knows* a deep link landed on the app's home rather than the title
  is a gate-2 measurement.)
- **D123 · Freshness** — a **nightly refresh** of everything on playlists and Continue Watching,
  plus a fresh look when a title is opened.
- **D124 · Leaving soon** — marked on the row ("leaves <service> Mar 31") **and put on the Pulp
  calendar** so Foreword prints it.
- **D125 · Diagnostics** — the suite's exportable log from Settings, **the TV's included** (exported
  via the phone). Ids, not content.
