# Storyboarder

Turns a Google Docs novel into a multi-volume **graphic novel**. It reads the whole manuscript,
builds a private design bible of characters and settings, storyboards the book into volumes →
pages → panels, then renders every panel and assembles **one PDF per volume**. Because image
generation is rate-limited, the work runs in a **background worker** and is fully **resumable** —
it keeps piecing the book together across pauses and restarts until it's done.

- **Analysis** (design bible + storyboarding) uses **Claude** (Anthropic).
- **Image generation** uses **Google Gemini** (an image model that takes reference images, so
  characters/settings stay visually consistent).
- **Doc selection** uses an in-app **Google Drive picker**; the backend fetches the text via the
  Google Docs API.
- **Interface** is a continuously-running worker plus a minimal web status page (live progress over
  SignalR; download finished volume PDFs).

## Architecture

`Storyboarder.sln` — four `net10.0` projects:

- **Storyboarder.Domain** — pure logic, no external deps: chunking, design-bible merge, rate
  limiter, backoff, the pipeline state machine, page-layout math, entities, and collaborator
  interfaces.
- **Storyboarder.Infrastructure** — EF Core + SQLite `DbContext`, the Claude/Gemini/Docs HTTP
  clients, SkiaSharp page composer, PdfSharp volume composer, and the `GraphicNovelProcessor`
  (advances one novel by one unit of work per call; all state in the DB).
- **Storyboarder.Api** — ASP.NET Core host: controllers + Swagger, the SignalR `ProgressHub`, the
  `GraphicNovelWorker` background service, and the static status page (`wwwroot/`).
- **Storyboarder.Tests** — xUnit + Moq; pure-logic unit tests, in-memory-SQLite data tests,
  fake-HTTP client tests, the end-to-end pipeline test, and API integration tests.

## Configuration

Nothing secret lives in `appsettings.json`. Provide secrets via **user-secrets** (dev) or
**environment variables** (deployment). Environment variables use `__` between section and key.

| Setting | Env var | What it is |
| --- | --- | --- |
| `Claude:ApiKey` | `Claude__ApiKey` | Anthropic API key (analysis/storyboarding) |
| `Claude:Model` | `Claude__Model` | e.g. `claude-sonnet-4-6` |
| `Gemini:ApiKey` | `Gemini__ApiKey` | Google **Generative Language API** key (image generation) |
| `Gemini:Model` | `Gemini__Model` | e.g. `gemini-2.5-flash-image` |
| `Google:ClientId` | `Google__ClientId` | OAuth **web client id** for the Drive picker |
| `Google:ApiKey` | `Google__ApiKey` | Browser **API key** for the Drive picker |
| `Storage:DatabasePath` | `Storage__DatabasePath` | SQLite file path (default `storyboarder.db`) |
| `Storage:OutputRoot` | `Storage__OutputRoot` | Folder for generated images/PDFs (default `output`) |
| `RateLimit:*` | `RateLimit__*` | Per-minute/per-day budgets, backoff, idle poll |

```bash
cd Storyboarder/Storyboarder.Api
dotnet user-secrets init
dotnet user-secrets set "Claude:ApiKey" "sk-ant-..."
dotnet user-secrets set "Gemini:ApiKey" "AIza..."
dotnet user-secrets set "Google:ClientId" "xxxx.apps.googleusercontent.com"
dotnet user-secrets set "Google:ApiKey" "AIza..."
```

### Google Cloud setup (one time)

In a Google Cloud project:

1. **Enable APIs**: Google Docs API, Google Drive API, Google Picker API, and the Generative
   Language API (for Gemini).
2. **OAuth consent screen**: configure it; add the scopes `.../auth/drive.file` and
   `.../auth/documents.readonly`.
3. **OAuth 2.0 Web client id**: add your app origin (e.g. `https://localhost:5001`) as an
   authorized JavaScript origin → use as `Google:ClientId`.
4. **Browser API key**: restrict to the Picker API → use as `Google:ApiKey`.
5. **Gemini key**: a Generative Language API key → use as `Gemini:ApiKey`.

The picker grants a short-lived access token in the browser; the backend uses it to read the
chosen doc **once** at ingest, then works entirely from the stored copy.

## Run

```bash
dotnet test Storyboarder/Storyboarder.sln      # all tests (no API keys needed)
dotnet run --project Storyboarder/Storyboarder.Api
```

Open the site root for the status page, or `/swagger` for the API. Click **Choose a novel from
Google Drive…**, pick a Google Doc, and watch the volumes build. Migrations apply automatically at
startup.

## Notes & limits

- A full novel can be **hundreds–thousands of panels** — potentially days of wall-clock time on
  free tiers and real image-generation cost. Test on a short doc first.
- Gemini character consistency is good but not perfect; the same reference sheet is supplied on
  every panel and the art-style directive is repeated to minimize drift.
- Dialogue/captions are typeset deterministically by the composer (the image model is told not to
  render text, which it does unreliably).
