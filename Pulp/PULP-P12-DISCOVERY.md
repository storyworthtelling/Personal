# P12 discovery — Pulp — decisions log

Name **Pulp** (D16; was the working name Marquee). This file records the gate-1 discovery answers
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
- **D126 · PINs are per adult** — each adult has their own PIN to leave a child's view (stored
  salted-hashed, never plain).
- **D127 · One ceiling** — a child's ceiling is an **age band mapped to both MPA film and US TV
  ratings**.
- **D128 · Unrated** — **hidden from a child unless allow-listed**.
- **D129 · Block-list scope** — by **title, franchise/series, or whole service**. Not keywords.
- **D130 · A child can "ask"** — a child marks a filtered title "I want this"; it reaches the
  parents' phones as a request (a dog-ear notice, not a buzz) to approve onto a playlist.
- **D131 · A child can search**, filtered by D12/D127–D129.
- **D132 · Screen-time: daily allowance and bedtime cutoff, per child** — and **a child cannot
  start something they would run out of time for**: a title whose runtime (an episode, a film)
  exceeds the minutes left in today's allowance, or would run past bedtime, will not hand off.
  Time is counted from hand-offs (and the media session where gate 2 finds one, D30), so it is an
  honest approximation, said as such.
- **D133 · A child's trailers** — only for titles that pass the child's filter.
- **D134 · Shared time** — spent **per child picked**, **except when an adult is in the picked
  group**, which spends no child's allowance.
- **D135 · Out of time** — "About 40 min left today" at the top; **titles too long dim (§8
  disabled) with "too long for today"**; shorter ones stay playable.
- **D136 · Extra time** — a parent grants "+N min today" **with their PIN**, on the spot or from
  their own phone.
- **D137 · By day** — ~~school nights (Sun–Thu) and weekends (Fri–Sat)~~ **revised by the owner:
  school nights are read from Google Calendar**, so holidays and vacations get the weekend
  allowance rather than being penalised. Two allowance/bedtime pairs per child (school night /
  not); which calendar marks school days is D154.
- **D138 · Data cost** — **free sources only**; gate 2 states what that costs in coverage.
- **D139 · Sports data** — **the same rule**; gate 2 says whether free sports data covers D56.
- **D140 · Attribution** — a quiet credits line in **Settings → About**, where terms allow.
- **D141 · What leaves the device** — providers see **title ids and search words only**, never
  names, viewers or history; the model sees taste summaries, **never a child's data**.
- **D142 · First run (phone)** — enter the vault passphrase; if the household file exists
  everything appears, otherwise Settings asks for services, then viewers. No tour.
- **D143 · First run (TV)** — **paired from a phone**: the TV shows a short code, the owner's phone
  pairs and hands over the passphrase on the home network. No typing on the TV.
- **D144 · Service sign-ins** — Pulp signs in to a service **only where that unlocks real watch
  history** (if gate 2 finds one); its token lives in the **personal vault**. Otherwise Pulp never
  touches a service password.
- **D145 · The Pulp calendar** — **created by the owner's install** in the owner's account, shared
  to the other adult, then found by id (#13).
- **D146 · Calendar contents** — **new episodes of shows on Continue Watching, releases of playlist
  titles, followed games** (and plans, D68). **Leaving-soon dates are not on it** — this narrows
  D124 to the row only.
- **D147 · Whose, on the shared calendar** — one person's show **does appear, labelled with
  whose** ("New episode — <show> (<name>)").
- **D148 · Timing** — **timed where a time is known** (games, live premieres), **else all-day**.
- **D149 · Changes** — a moved date or rescheduled game is **moved silently** by the nightly
  refresh.
- **D150 · Continue Watching rows** — poster · title · next up · where · **a thin pencil progress
  mark** ("E5 of 10").
- **D151 · Tapping a row opens the title page**; play is on the page.
- **D152 · Caught up** — a show with nothing left out **leaves Continue Watching** and returns when
  a new episode lands (D21). Refines D31: "until removed" applies while there is something to watch.
- **D153 · Finished for good** — leaves Continue Watching for good, **stays struck through on
  playlists** for an easy rewatch.
- **D154 · Where school days come from** — **Foreword's calendar mapping**: Foreword already maps
  each child's calendars to that child's column, so Pulp reads the mapping (it lives in the
  account's personal vault, `PersonalVault.CalendarMappingJson`) rather than asking again. Each
  Pulp child viewer is linked to a Foreword column. (Open for gate 2: what in a child's mapped
  calendars *means* "school day" — a recurring school event, or a break marked as one — has to be
  decided from the real shape of those calendars, described not copied.)
- **D155 · Per child** — each child's school days come from their own mapped calendars.
- **D156 · Tomorrow decides** — tonight is a school night if **tomorrow** is a school day.
- **D157 · Unreadable calendar** — fall back to **Sun–Thu**, with a quiet dog-ear for a parent.
- **D158 · Title page bar** — **Play** (primary; here or TV per the header toggle) + **Add to…**
  (secondary). The trailer sits on the page by the poster.
- **D159 · Episodes** — a **season accordion** (current season open), episodes as rows: number,
  title, official synopsis, watched state; tap to play that episode.
- **D160 · Manual marking** — **long-press** an episode or title → action card: Watched /
  Unwatched / Watched up to here; undo-first.
- **D161 · A child's title page** — the **rating chip only**, no advisory text.
- **D162 · Settings groups** — **Services · Household · Discover · TV · Vault · About ·
  Diagnostics**. (No Sports group: teams are added from the Sports tab, D67.)
- **D163 · The other adult's settings** — **only her own viewer and her teams**. This conflicts
  with D45 (either adult edits children's filters) and D88 (either adult adds viewers) — put back
  to the owner in the next round (D166).
- **D164 · No PIN for children's settings** on an adult's own phone.
- **D165 · TV settings** — **all of them on the TV, behind an adult's PIN**.
- **D166 · Resolves D163** — **both adults manage the children** (filters, time, allow/block-lists,
  requests). The other adult's phone may also change **her own viewer and teams**. **Services and
  adding/removing viewers stay the owner's** (narrows D88).
- **D167 · Rights follow the person** — her PIN on the TV opens the same set as her phone.
- **D168 · Children's requests** — reach **both adults**; **first answer settles it**, and the other
  phone's notice goes.
- **D169 · A declined request** quietly leaves the child's list; no message.
- **D170 · Search field** — pinned under Discover's header; **empty shows the stack**, typing
  replaces it with results in place.
- **D171 · Mood search is explicit** — a **Title / Mood chip** beside the field.
- **D172 · Results** — **top 20, "more" below**; yours first, elsewhere after the hairline; the count
  line reads matches of total.
- **D173 · A person page** — name, then **their titles by release, yours first**. No photo, no
  biography.
- **D174 · Who's watching (TV)** — pick **one or more viewers, then Go**; the group decides
  filters, time (D134) and "together" (D98).
- **D175 · Mixed group** — **the strictest child's filter** applies while a child is picked.
- **D176 · No Discover stacks on the TV at all** — supersedes D26's "stack follows whoever was
  picked". The TV's Discover is search and trailers.
- **D177 · The TV forgets on power-off** (D26).
- **D178 · Switching to a child (phone)** — a **secondary button in the bottom bar**. (DESIGN.md §7
  shows the bar only when a screen has a primary action; where the switch sits on screens without
  one is drawn at the mockup gate.)
- **D179 · A child's view is marked by a line with the child's name under the header.** No
  monograms anywhere except the choice page.
- **D180 · Monograms are ink only** — no per-viewer colour.
- **D181 · A child's phone time does not count** — the allowance is spent **only on the TV**.
- **D182 · A game is watched** when the media session reaches its end; otherwise the D30 fallback
  (handed off = watched); unwatched can always be marked.
- **D183 · "Dismiss" on the D70 hold is a peek** — opens the standings/highlights once without
  removing the game; the hold returns.
- **D184 · Preseason / playoffs** — a **status chip on each game**.
- **D185 · Competitions** — following a team brings **all its competitions**.
- **D186 · Release order** — **phone first, then sports, then the integrations** (Flyleaf hiding,
  Index usage). The TV's place in that order moves out with D187.
- **D187 · The TV is its own phase** — P12 ships the **phone**; the **TV head gets its own phase
  with a design pass first** (the P14 watch precedent). Every TV decision recorded here (D13–D15,
  D17, D26, D63, D74–D85, D95, D102, D116, D143, D165, D167, D174–D177, and the TV half of D132's
  allowance, which D181 spends only there) **carries to that phase as its starting brief**, not
  into P12's plan. P12 keeps the shared data (D46) TV-ready and the phone's send-to-TV toggle out
  until the TV exists.
- **D188 · App id `com.storyworthtelling.pulp`**, one id for phone and (later) TV, frozen on its
  first build.
- **D189 · Folder `Pulp/` at the repo root** — its own solution (`Pulp.Domain`,
  `Pulp.Infrastructure`, `Pulp.Android`, `Pulp.Tests`), referencing `Codex.Ui.Android` for
  Paper & Ink. (This log moved from `Marquee/` with it.)
- **D190 · Children in P12** — **filters, requests and per-adult PINs now**; the **allowance,
  bedtime and school-day calendar (D132–D137, D154–D157) arrive with the TV phase**.
- **D191 · No TV path in P12** — hand-off is to the phone's own app; the title page names the
  service so it can be started on the TV by hand.
- **D192 · Who's watching on the phone** — **Play asks only when casting** (a TV-phase matter);
  in P12 Play is simply the current view's viewer (the owner, or the child switched to). How
  "together" (D98) is marked without a cast is D194.
- **D193 · Integrations stay in P12** as its last release: Flyleaf hiding (D92) and Index usage
  (D90).
- **D194 · "Together" is deferred to the TV phase** — with it, the cheating line (D99, D102). In
  P12 every viewer's progress is their own.
- **D195 · Voice facts about the other adult** — **only together-show progress** (so a TV-phase
  matter); never anything from the other's private Continue Watching.
- **D196 · Mockup round 1** — **cover studies + Continue Watching, a title page, Discover (stack
  and search), a playlist**. Round 2: Sports, Calendar, Settings, children's views, action cards.
- **D197 · Discovery closes** at this round (~200 questions asked across 50 rounds); gate 2 next.

## Carried to gate 2 (the technical finding)

Raised by the answers above and owed a measured answer, not an assumption:

1. **In-app playback** (D2) — embedded web players, Widevine in a WebView, each service's terms.
2. **Choosing / confirming a service profile** per service (D11, D44) — and therefore which
   services a child can start through Pulp at all.
3. **Finished detection** — other apps' media sessions via a notification listener (D30, D182);
   per-service sign-ins that expose real history (D144).
4. **Deep links** — per-service schemes, title vs home landing, how a mis-landing is detected
   (D122), channel links for live TV (D57), and recordings vs the live feed (D55).
5. **Availability source** — free only (D138), US (D51), subscription + free + live (D52),
   cheapest rent/buy (D53), rating *reasons* (D36), trailers (D19, D34), posters only (D37).
6. **Sports data** — free schedules, standings, brackets for D56's leagues (D139); official league
   YouTube highlights (D71).
7. **Shared household file** — a two-writer merge-safe format (D46), encrypted (D47), and its
   cross-account visibility.
8. **The Pulp calendar** — created and shared from the owner's account (D145).
9. **Index hand-off** (D90) and **Flyleaf hiding** (D92) — the two cross-app contracts.
10. **Frozen identifiers** — `com.storyworthtelling.pulp` (D188), the Drive file names, any
    registered component names, guarded by tests from the first build.

## Gate 3 decisions

- **D198 · The cover idea** — **06 · To be continued**: the serial's cliffhanger, a figure hanging
  from a ledge above a night city. Round 2 varies crop and style on it.
- **D199 · Round 2 outcome** — the **original round-1 painting (06)** is the best of the cliffhanger
  studies. Round 3 shows **that painting projected onto a screen, with a bucket of popcorn on a
  table in front**.
- **D200 · The cover** — **3·4, the book-lined den**: the cliffhanger painting projected on a
  screen at the end of a book-lined room, popcorn on a side table beside an armchair
  (`Pulp/design/covers/study-r3-04.jpg`).
- **D201 · A contrast hue** — the identity is a **contrast colour** to the warm painting, sampled
  from the painting's own projected night sky (cool pixels average 232°), set at **230°**, the
  centre of the free window, so every tier clears the in-progress teal and the reference slate by
  ≥20° after hex rounding. Ladder vividness (measured 0.075 vs vivid 0.095) and the deep night-blue
  cloth are offered in the design book for the owner's choice.
- **D202 · Identity: Marigold, 63°** — supersedes D201's blue. OKLCH **63°**, mark chroma 0.095,
  on the suite's shared lightness ladder: fill `#CEB7A3` · ground `#BF9E82` · mark `#86541F` ·
  text `#6B4219` · deep `#4B3015`; **cloth `#F6A24E`** (light binding, rule/flourishes/title in
  ink, ink on cloth 6.25:1 — Score's and Plate's construction). 22° from Flyleaf's amber and 22°
  from the caution ochre; the only yellow-orange that clears §3's floor at every tier after hex
  rounding. To be registered in DESIGN.md §3/§19 and the hue-separation guard at implementation.

## Gate 2 decisions

- **D203 · TMDB is out** — its API terms forbid use in connection with an AI-based application or an
  LLM query-response system, which Pulp is (mood search, model playlists, voiced lines).
- **D204 · Refresh cadence** — **weekly refresh + a daily changes feed + a fresh look on open**,
  inside the availability source's free 1,000 requests a month. Supersedes D123's "nightly".
- **D205 · Rating reasons, owner's ask: query and summarise IMDb.** Checked: IMDb's Conditions of Use
  forbid "data mining, robots, screen scraping … except with our express written consent", and the
  MPA's own ratings site forbids robots and page-scraping likewise. Put back to the owner (D207).
- **D206 · Sports, owner's ask: each league's own website.** Checked: MLB's own JSON
  (`statsapi.mlb.com`) carries an explicit notice permitting "individual, non-commercial, non-bulk
  use"; the NHL's (`api-web.nhle.com`) is public but carries no terms; the NBA's CDN refuses plain
  requests (403); the NFL publishes none; most league sites' HTML terms forbid robots. Put back to the
  owner (D208).
- **D207 · Rating reasons dropped** — the title page shows the **rating chip only** (narrows D36).
- **D208 · Sports sources** — **league feeds where their terms permit, ESPN's JSON for the rest**,
  one adapter per source behind one interface; each league's source is named in Settings → About.
  Today that is MLB's own feed (explicitly permitted for individual, non-commercial use) and ESPN for
  every other league in D56, including AFL and the women's leagues.
- **D209 · Person and franchise search dropped** — search is **title, mood and team** (narrows D64).
  Franchises can still be held on playlists (D100) where the availability source groups them; a
  franchise *search* is gone.
- **D210 · Children and service profiles** — the **attestation route**: a child may start titles
  only on services where the owner has PIN-locked every adult profile and ticked "adult profiles are
  PIN-locked" for that service in Settings. Any other service blocks a child's hand-off (D44).
- **D211 · Gate 2 approved** — `PULP-P12-TECHNICAL-FINDING.md` as written, with §6 resolved.

## Gate 3 — screens, one at a time

Process (owner, D211): one screen per round; every decision, question and approval is put as chips.
- **D212 · Screen 1, Continue watching — approved**, with the binder colours as drawn (home
  marigold · Playlists reference slate · Discover positive leaf · Sports action pine · Calendar
  in-progress teal · Settings grey; caution ochre kept off the strip) and the row as drawn (poster ·
  title · next up · where · pencil progress mark with count).
- **D213 · Home's bottom bar** — secondary **switch to a child and back** ("Watch as a child" /
  "Back to me"); primary opens **Recently watched** for an adult, **Favorites** for a child.
- **D214 · Two new pages** — **Recently watched** (adult) and **Favorites** (child), each to be
  drawn and approved in its own round.
- **D215 · Rating chip, four steps** — kids (G, TV-Y, TV-Y7) positive leaf · family (PG, TV-G,
  TV-PG) resolved grey · teen (PG-13, TV-14) caution ochre · mature (R, NC-17, TV-MA) problem rose;
  unrated is a hairline outline with no fill. Each step also differs by label, so the chip reads
  without colour.
- **D216 · Choosing the home per title** — the other home is listed under the best one; tapping
  "open there instead" makes it that title's home from then on (overrides D28 for that title).
- **D217 · Episodes show number and title only**, the next one included; a tap expands the official
  synopsis in place.
- **D218 · Screen 2, the title page — approved** with D215–D217.
- **D219 · Discover's piles are literal stacks** — one poster at the front, the rest fanned behind,
  swipe to flip, "1 of N".
- **D220 · Vetoes stay struck until Friday**, with "Bring it back".
- **D221 · Add / veto / trailer are small buttons beside the front poster** (not long-press).
- **D222 · Screen 3, Discover (revision 2) — approved.**
- **D223 · A playlist has no bottom bar.**
- **D224 · Caught-up shows dim on a playlist** — a new season matters only to a viewer who was caught
  up: the row stays the show (D100 holds), dimmed, reading "Caught up · Season N out <date>", and
  brightens when the season lands. A show with unwatched episodes is unaffected by an upcoming season.
- **D225 · Screen 4, a playlist — approved**, rows as drawn.
- **D226 · Recently watched keeps everything, forever** (the viewer's own record; private, D97).
- **D227 · One row per show**, grouped under the day it was last watched, showing the last episode
  and — until the show is complete — a pencil progress mark across the whole show.
- **D228 · Films and shows only** — games are not listed there.
- **D229 · Screen 5, Recently watched — approved** with D226–D228.
- **D230 · A child's favorites come from three places** — the child's own star on a title page, a
  parent's pick, and the titles the child has watched most; each poster says which. Only titles
  passing the child's filter can appear.
- **D231 · A child sees three tabs** — Watching, Playlists, Discover. No Sports, Calendar or Settings
  in a child's view (narrows D42/D72 for the phone: a child's sport waits for the TV phase).
- **D232 · A voiced line on Favorites, moderated for the child's age** (short words, one idea,
  nothing frightening) — adds a seat to D38/D39's closed list.
- **D233 · The voice's reading level for a child** comes from the child's rating-ceiling age band
  (D127); no birthday is stored.
- **D234 · Screen 6, Favorites (revision 2) — approved.**
- **D235 · League order** — closest to the end of its season first; preseason and off-season last
  (an off-season league is one line with its return date).
- **D236 · Games shown** — unwatched recordings plus the next game per followed team; the full
  schedule is one tap into the league.
- **D237 · The hold covers revealing games** — while a team's recording is unwatched, any upcoming
  game whose existence would reveal a result (a playoff fixture) is held with the standings and
  highlights (extends D70).
- **D238 · League and team badges** — beside each league name and on each game, from the sports
  feed; treated as content in an artwork slot, not palette.
- **D239 · Championships are always followed** — every followed league's championship game or
  series appears whether or not a followed team is in it; under a hold it shows the date with "teams
  held".
- **D240 · Championships: followed leagues only.**
- **D241 · Leagues and teams fold** — a folded league is one line (badge, name, state); a league opens to teams, a team to its games. Only the league closest to season's end, and its teams with unwatched recordings, start open; what the viewer opens stays open.
- **D242 · The spoiler hold is a single tag** — a 'Spoilers' tag on the league's line; no explanation card and no 'held' lines; held items are simply absent; tapping the tag peeks once (refines D70, D183, D237, D239).
- **D243 · Screen 7, Sports (revision 5) — approved**, logos and game chips kept.
- **D244 · The Calendar page shows the viewer's own and shared items only** — own shows and teams,
  plans they're in, shared playlists. The household Google calendar still carries everyone's,
  labelled (D147), for Foreword.
- **D245 · Two weeks ahead.**
- **D246 · No bottom bar on Calendar.**
- **D247 · Screen 8, Calendar — approved** with D244–D246.
- **D248 · A service row is short** — name and tier; profiles, the PIN-lock attestation and channels
  live on the service's own page.
- **D249 · An unlocked service carries one neutral "Adults only" badge** (no warning text, no colour).
- **D250 · Screen 9, Settings — approved**; the groups (Household, Discover, Vault, About,
  Diagnostics) and the service page are built from the recorded decisions without further drawings.
- **D251 · Plan to watch leaves the action card** — plans are made from the title page only.
- **D252 · Adults favorite for a child** from the action card ("Favorite for a child…", pick the
  child); it lands on that child's Favorites as a parent's pick (D230).
- **D253 · A child's star is the request** — no separate "I want this" on the card (narrows D130).
- **D254 · Favoriting for a child lives under Add to…** — each child's Favorites is a destination in
  the picker, beside the playlists (supersedes D252's separate card action).
- **D255 · A parent's favorite above a child's ceiling also allow-lists it** for that child (D12);
  a block-list entry still wins and that child's Favorites is greyed in the picker.
- **D256 · Screen 10, the action card (revision 3) — approved.**
- **D257 · The picker has checkboxes** for adding to several lists at once; each tick takes effect
  as made; unticking is undo-first. Playlists stay last-used first (D109).
- **D258 · A child's row in the picker carries one neutral badge** — "Above band" (ticking
  allow-lists, D255) or "Blocked" (greyed, can't be ticked).
- **D259 · Screen 11, the picker (revision 2) — approved.**
- **D260 · What waits behind the dog-ear** — a hand-off that landed on the app's home (D122); a key
  rejected or a feed unreachable; a household-file problem.
- **D261 · The lookup allowance is never a notice** — recorded in Diagnostics only. (The owner ticked
  it in the first question and chose "Diagnostics only" in the second; the specific answer is
  taken.)
- **D262 · Screen 12, notices — approved.**
- **D263 · Home's bottom bar never changes** — "Recently watched" stays the primary on an empty home.
- **D264 · Every empty page is voiced** (D38), each with a neutral Serif italic fallback.
- **D265 · Screen 13, empty states — approved.**
- **D266 · The icon crop holds the screen and the popcorn together.** Cover title band kept (Serif
  SemiBold, ink on marigold).
- **D267 · The launcher icon is full bleed** — the painting fills the mask, no marigold border and no
  keyline (the shelf cover keeps the binding).
- **D268 · Screen 14, the book (revision 2) — approved.**
- **D269 · Index's usage line counts times opened through Pulp** (hand-offs) per calendar month.
- **D270 · "Not opened in Pulp since …" appears after two months unused**, in the same muted ink.
- **D271 · Screen 15, Index's usage line — approved.**
- **D272 · Gate waived for the undrawn pages** (owner's decision): a service's own page, the
  Household group, search results and mood search, standings / bracket / a league's schedule, the
  prompt-or-seed playlist flow, a film's title page, and Vault + first run are **built from the
  recorded decisions without mockups** and reviewed in the running app. Every other surface was
  drawn and approved.
- **D273 · Gate 3 closed** — fifteen screens approved (D212–D271); the published design book is
  https://claude.ai/artifact/M5cdj33HaqxnXsdvv2bu4N and committed copies live in `Pulp/design/`.
