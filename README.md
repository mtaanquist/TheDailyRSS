# The Daily RSS

A self-hosted RSS reader that reads like a newspaper. Feeds are organised into
folders ("sections"), and each day's articles are laid out as an **edition** with a
lead story and a column grid. Missed a day? Flip back to an **earlier edition** and
read it exactly as it printed.

Built from the [RSS Reader wireframes](#design) in a pen-on-newsprint style, with a
warm **Newsprint** theme and an **Evening Edition** dark theme.

![pen-on-newsprint, classic masthead layout]()

## Features

- 📰 **Newspaper editions** — articles grouped by the day they were published; browse
  back through the archive one edition at a time.
- 🗂 **Folders & feeds** — organise feeds into colour-coded categories; add a feed by
  pasting any site URL (the server auto-detects the RSS/Atom feed).
- ⭐ **Save / star** articles, mark read/unread, "unread only" filter, mark-all-read.
- 🌗 **Two themes** — Newsprint (light) and Evening (warm dark), plus **Auto** by sunset.
  Headline font (PT Serif / Newsreader / Lora) and reading density are configurable.
- 👤 **Multi-user accounts** — register/sign-in, per-user feeds & state, profile,
  **Sync & devices** screen with per-device sign-out.
- 🔁 **OPML import/export** to move your subscriptions in and out.
- 🛰 **Background fetcher** refreshes every feed on a schedule (polite conditional GETs).

## Architecture

A standard **hosted Blazor WebAssembly** solution on **.NET 10**, backed by **PostgreSQL**.

| Project | What it is |
| --- | --- |
| `src/TheDailyRSS.Client` | Blazor WebAssembly app — the newspaper UI |
| `src/TheDailyRSS.Server` | ASP.NET Core — REST API, EF Core + Npgsql, JWT auth, background feed fetcher; also serves the WASM client |
| `src/TheDailyRSS.Shared` | DTOs/contracts shared by client and server |

The browser can't reach Postgres or fetch cross-origin feeds directly, so the server
does both: it exposes a JSON API the WASM client calls with a bearer token, and a
`BackgroundService` polls feeds and stores articles.

## Run it (Docker — recommended for self-hosting)

```bash
cp .env.example .env        # set DB_PASSWORD, EDITION_TZ, APP_PORT…
docker compose up --build
```

Then open <http://localhost:8080> and create your account. The database schema is
created automatically on first start (EF Core migrations run at boot).

## Run it (local dev)

You need .NET 10 and a Postgres instance.

```bash
# 1. Start Postgres (example with Docker)
docker run -d --name tdr-pg -e POSTGRES_DB=thedailyrss \
  -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16

# 2. Run the server (hosts the WASM client too)
dotnet run --project src/TheDailyRSS.Server
```

The default connection string (`appsettings.json`) points at that local Postgres.
Visit the URL printed by `dotnet run` (e.g. <http://localhost:5230>).

## Configuration

Set via `appsettings.json` or environment variables (double-underscore form):

| Setting | Env var | Default | Notes |
| --- | --- | --- | --- |
| Connection string | `ConnectionStrings__Default` | local Postgres | Npgsql format |
| Edition timezone | `Feeds__EditionTimeZone` | `UTC` | IANA zone; defines the daily edition cut-off |
| Refresh interval | `Feeds__RefreshIntervalMinutes` | `20` | background fetch cadence |
| Max articles/feed | `Feeds__MaxArticlesPerFeed` | `500` | older non-saved articles are pruned |
| JWT signing key | `Jwt__Key` | auto-generated | persisted to `<DataDir>/jwt-signing.key` if blank |
| Token lifetime | `Jwt__ExpiryDays` | `30` | |
| Data directory | `DataDir` | `<contentroot>/data` | holds the JWT key |

## Database migrations

The app applies migrations automatically on startup. To create a new one after
changing the model:

```bash
dotnet ef migrations add <Name> -p src/TheDailyRSS.Server -s src/TheDailyRSS.Server -o Data/Migrations
```

## Notes & trade-offs

- **Multi-user & shared storage.** Feeds are stored once as a global `FeedSource`
  (deduplicated by URL) that owns the article rows; many users can subscribe without
  re-downloading or re-storing the same articles. Each user gets a `Subscription`
  (which files the source into a category) and per-user read/saved/position state in
  `UserArticleState`, created lazily on first interaction. Registration is open and the
  **first account to register becomes the admin**.
- **Fixed categories.** Sections are a seeded, Guardian-style taxonomy (News, World,
  Politics, Business, Technology, Science, Environment, Sport, Culture, Lifestyle,
  Opinion). Users file each subscription into one of them but can't create their own;
  admins manage the taxonomy under *Settings → Categories*. The same shared source can be
  filed under different categories by different users.
- **Front page** shows a curated slice of every category the reader has feeds in (top few
  per section, in taxonomy order); drilling into a section shows it in full.
- **Muted words.** Per-user keyword filters (*Settings → Muted words*) hide matching
  articles from editions (case-insensitive, title or title+summary). A muted article is
  still reachable by direct link.
- **Feed HTML** is rendered in the reading view with `<script>/<style>/<iframe>` stripped.
  For a personal, self-hosted reader this is a reasonable trade-off; if you expose the
  instance widely, consider a fuller HTML sanitizer.
- **Schema reset.** The multi-user/shared-storage rework replaced the original schema
  with a fresh `InitialCreate` baseline. If you ran an earlier build, reset the dev
  database first: `docker compose down -v`.
- WASM asset fingerprinting is disabled (`WasmFingerprintAssets=false`) so the hosted
  server serves a stable `blazor.webassembly.js`. After upgrading the app, do a hard
  refresh (Ctrl/Cmd+Shift+R) to pick up new client assets.

## Design

Recreated from a Claude Design handoff bundle ("RSS Reader Wireframes"). The primary
reading view is the **Classic masthead** layout (lead story + 3-column grid); the
Evening dark theme and the account/sync screens follow the same wireframes.
