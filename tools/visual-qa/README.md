# Visual QA harness

A tiny headless-browser harness for eyeballing the Blazor UI — it registers a throwaway
account against a **running** server, injects the JWT so the app boots signed-in, and
screenshots a set of routes to `shots/*.png`.

It's deliberately not wired into CI; it's a manual "let me see the page" tool.

## One-time setup

Chromium needs a few system libs (these need `sudo`):

```bash
sudo apt-get update && sudo apt-get install -y unzip libnss3 libnspr4 libasound2t64
```

Then install the npm deps and a browser. If `npx playwright install chromium` works on your
OS, use it. On very new distros where Playwright's registry has no matching build, fetch an
OS-agnostic Chrome instead:

```bash
cd tools/visual-qa
npm install
npx @puppeteer/browsers install chrome@stable   # prints the chrome path
```

## Run

Start the app (any Postgres will do), then:

```bash
# CHROME only needed if you used the @puppeteer/browsers fallback above
CHROME="$(find chrome -name chrome -type f | head -1)" \
BASE=http://localhost:5230 \
PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS=1 \
npm run shoot
```

Screenshots land in `shots/`. Limit the set with `ROUTES=front,profile,keywords`.

Routes: `front, profile, ai, keywords, devices, categories, feeds, login`.
