# Local model feasibility — replacing Claude/Gemini with an on-device open-weights model

**Status:** investigation only. No code has changed. Written 22 Oct 2026.

**Question:** could the AI built into the suite run on a free, open-weights model installed on
the phone (the Pixel 8a: Tensor G3, 8 GB RAM) instead of calling Claude or Gemini?

**Short answer:** for about half the call sites, yes. Those are the small, fast-tier jobs where the
model reads something we hand it and returns a short structured answer, and several of them are the
most private data the suite sends out (mail bodies, the prayer page, the budget worksheet). For the
rest, no. Folio's whole-book reading and its image generation are far beyond a phone. The
recommendation jobs depend on what the model knows about the world, and a 2–4 B model knows too
little to make them worth keeping. **A local model would save very little money**: the bill is
almost all Folio, and Folio cannot move. The reasons to do this are privacy and working offline.

---

## 1. The candidate model and runtime

| | |
|---|---|
| **Model** | **Gemma 4 E2B / E4B** (released 2 Apr 2026). **Apache 2.0**: this is the first Gemma under a plain open-source licence, so it can be redistributed without the old Gemma terms. Text, image and audio in; JSON output enforced by constrained decoding; function calling. |
| **Runtime** | **LiteRT-LM** (Google AI Edge), which runs on the CPU, the GPU (OpenCL) or the NPU on Android. Its API is Kotlin. |
| **Size** | E2B file is **2.58 GB** (a Tensor-specific build is about 3.1 GB). Peak working memory is reported at around 0.6–0.7 GB, because the per-layer embeddings stay on disk and the vision/audio encoders load only when a task needs them. Google gives no E4B file size; expect roughly 4 GB. |
| **Context** | 32k tokens in the LiteRT-LM build (the model card says up to 128k). |
| **Speed** | Google's published figure is **52 tok/s decode and about 3,800 tok/s prefill on a Galaxy S26 Ultra** (GPU). **No published Pixel 8a figure exists.** Third-party reports for 2024-era phones put decode at roughly 10–25 tok/s. Plan on the low end until it has been measured on the device. |

Alternatives I looked at and do not recommend:
- **Gemini Nano through ML Kit's Prompt API.** It is not open weights, it depends on AICore, and
  Google's support list names Pixel 10-class devices with no confirmed Pixel 8a support.
- **llama.cpp through a .NET wrapper (GGUF Gemma or Qwen).** It is easier to call from C#, but on
  Tensor G3's Mali GPU it is effectively CPU-only, so it is several times slower than LiteRT-LM.
  Keep it as the fallback if the binding below proves unworkable.

## 2. Every call site, rated

Fourteen clients make the suite's model calls. "Fit" means how well a 2–4 B on-device model would do
the job, given what each one is asked to do.

### Good fit: move these

| Client | App | What it does | Why it fits |
|---|---|---|---|
| `MailReadingClient` | Post | Classifies a message and detects a proposed date. ≤2,000 chars in, ≤400 out, JSON. | A short, bounded classification task. **Privacy win:** today the whole message goes to Claude. Every failure is already silent ("no idea"), so a weaker model degrades safely. |
| `ClaudeTaskReader` | Foreword | Turns one typed sentence into a structured task rule. ≤512 out, JSON. | One sentence into a schema is exactly what constrained JSON decoding is for. A failure already keeps the sentence as an unparsed task. |
| `DarkroomCaptionClient` | Plate | Writes a one-line caption for one or more photos. Fast tier. | Gemma 4 handles images. It also removes the base64 upload of private photos. |
| `VoiceClient.ComposeAsync` (fast tier) | all | Writes short situational lines in the owner's voice from a stored profile. | The profile is supplied in the prompt and the output is a sentence or two. Already degrades to neutral copy. |

### Possible, but measure before trusting it

| Client | App | Concern |
|---|---|---|
| `ClaudeJournalTranscriber` | Foreword | Handwriting on a full reMarkable page. A small vision model reading cursive is the biggest quality unknown in this list. **It is also the biggest privacy win**: CLAUDE.md records that "private" currently means "read by a model"; a local model would make it mean "never left the phone". Worth an evaluation on real pages before deciding. |
| `ClaudeVisionHandwritingExtractor` | Index (worksheet read-back) | Handwritten **figures**. A misread digit is a wrong number in the budget, and nothing looks broken when it happens. Only switch with a confidence check (for example, totals that must add up) and side-by-side testing. |
| `ClaudePastoralComposer` | Foreword | 200 tokens, on the strong tier today. A drop in quality would show, but the prompt is short and the privacy case is the same as the journal's. |
| `VoiceClient.ComposeAsync` (high-visibility lines) | all | Deliberately on the strong model because "cadence and wit" are what the owner reads it for. Expect a visible drop. |
| `TasteProfileClient` | Score | Reasons over evidence we hand it (no world knowledge needed), but writes up to 1,500 tokens: about a minute of decoding on a 8a. Fine for a background job. |
| `ShelfGlyphClient` | Flyleaf | A shelf name in, an SVG path out. Small models draw poor paths. Low stakes, because `GlyphPath` validation already falls back to the monogram, but expect the fallback to fire often. |

### Poor fit: keep on Claude

| Client | App | Why not |
|---|---|---|
| `HiddenTrackNamer` | Score | A pure recall question ("what is the bonus track after track 12 on this album"). Small models make these up confidently. |
| `DiscoveryClient` | Score | Music recommendations draw on broad catalogue knowledge. `PlaylistPlan` already drops invented ids, so a small model would mostly produce shorter, blander playlists rather than wrong ones. |
| `MoodClient` | Pulp | The same problem for film and TV titles. Pulp also runs on the partner's phone, whose hardware is unknown. |
| `VoiceClient.RefineAsync` | Folio | Distils the voice profile from a whole manuscript, which is too long for a 32k context. |

### Not feasible

| Client | App | Why not |
|---|---|---|
| `ClaudeAnalysisClient` | Folio | Fifteen readings of the **whole manuscript** (~130k tokens each) on the Sonnet and Opus tiers. That does not fit a 32k context. Even if it did, prefilling it fifteen times at a few hundred tok/s would take hours of a hot phone per draft, and the judgement Folio exists for is the strong-model work. This is also where nearly all the money goes (about $3.38 a draft). |
| `GeminiImageClient` | Folio | Scene and cover illustrations with reference images, to keep characters consistent. The on-device option (the MediaPipe Image Generator, Stable Diffusion 1.5-class, ~15 s per image on a high-end phone) is a different league of quality and has no reference-image consistency. |

## 3. What it would take

1. **Bind the runtime.** LiteRT-LM is a Kotlin API built on coroutines and `Flow`, which Xamarin
   binding tools handle badly. The workable route is a small Java shim AAR that exposes a plain
   callback-or-blocking API (`load(path)`, `generate(prompt, schemaJson) → string`) and is bound
   like `Codex.GmBridge.Binding`. This is the largest unknown in the engineering, and the first
   thing to spike.
2. **Put one model on the phone, not one per app.** Four apps each shipping 2.6–4 GB is out of the
   question, and two copies loaded at once would compete for 8 GB of RAM. One app holds the model
   and serves the others through a **signature-protected bound service**, the same trust boundary
   the suite's cross-app broadcasts already use. That service is a new exported surface on the
   locked phone, so where it lives is the owner's call (Flyleaf is the obvious host, since it is
   always present).
3. **Get the model onto the device.** It cannot ride the APK or the `apps-latest` release (GitHub
   release assets cap at 2 GB). Download it once over Wi-Fi from Hugging Face, check its SHA-256
   against a pinned value, and keep it in the host app's private storage. Apache 2.0 makes
   redistribution legal if we ever want our own mirror.
4. **Keep the existing seam.** Every client already has the same shape: build a prompt, take back
   text or JSON, and fall back quietly on any failure. A local implementation of the same
   interface (`IMailReadingClient`, `ITaskReader`, and so on) can be chosen per call site, falling
   back to Claude when the model is absent, still downloading, or busy. No caller has to change.
5. **Run in the background.** The good-fit jobs already run in the background (mail sync, the
   morning delivery, the Darkroom). Long runs should go through a foreground service, as Folio's
   do, and should prefer charging so heat and battery stay out of the owner's day.

## 4. Recommendation

- **Don't expect savings.** The money is in Folio's readings and image generation, and both stay
  on Claude and Gemini. Moving the small fast-tier calls saves very little.
- **Do it for privacy, starting small.** The best first target is **Post's `MailReadingClient`**:
  it is bounded, it already fails silently, it runs in the background, and it stops whole emails
  leaving the phone. **Foreword's journal transcription** is the most valuable target, but only
  after it has been measured on real pages.
- **First step: a measurement spike, not a migration.** Build the Java shim, load Gemma 4 E2B on the
  8a, and record three things: tokens per second (decode and prefill, GPU and CPU), peak memory
  alongside Flyleaf, and output quality on (a) a set of **invented** mail samples against the
  current Claude labels, and (b) a few journal pages read on the device only, with nothing
  committed. Those numbers decide whether section 3 is worth building.

## 5. Not verified

There is no Pixel 8a speed figure for Gemma 4, and none was measured here. One open LiteRT-LM issue
reports a Pixel 8a GPU-backend bug (endless `<pad>` output on Gemma 3 270M; CPU was fine), so check
GPU output on the device itself. The E4B file size, the binding approach, and handwriting quality
on real pages are also unproven.

### Sources
- [Blazing fast on-device GenAI with LiteRT-LM — Google Developers Blog](https://developers.googleblog.com/blazing-fast-on-device-genai-with-litert-lm/)
- [Get started with LiteRT-LM on Android](https://developers.google.com/edge/litert-lm/android)
- [Gemma 4 launch, Apache 2.0 — The Next Web](https://thenextweb.com/news/google-gemma-4-open-models-apache-2-launch)
- [Gemma 4 E2B LiteRT-LM model files — Hugging Face](https://huggingface.co/huggingworld/gemma-4-E2B-it-litert-lm)
- [LiteRT-LM issue #3280 (Pixel 8a GPU backend)](https://github.com/google-ai-edge/LiteRT-LM/issues/3280)
- [ML Kit GenAI APIs overview](https://developers.google.com/ml-kit/genai)
- [MediaPipe Image Generator for Android](https://developers.google.com/edge/mediapipe/solutions/vision/image_generator/android)
