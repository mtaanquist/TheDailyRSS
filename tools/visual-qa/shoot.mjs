// Visual-QA harness: registers a throwaway account against a running TheDailyRSS server, injects
// the JWT into localStorage so the WASM app boots authenticated, then screenshots routes to PNGs.
//
//   CHROME=/path/to/chrome BASE=http://localhost:5230 OUT=./shots [ROUTES=front,profile] node shoot.mjs
//
// CHROME  path to a launchable Chrome/Chromium. If unset, Playwright's bundled browser is used.
//         (On unsupported hosts, fetch one with: npx @puppeteer/browsers install chrome@stable)
// BASE    base URL of the running app (default http://localhost:5230)
// OUT     output directory for screenshots (default ./shots)
// ROUTES  comma-separated subset of route names to shoot (default: all below)
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const BASE = process.env.BASE ?? 'http://localhost:5230';
const OUT = process.env.OUT ?? './shots';
const CHROME = process.env.CHROME;

const ALL = {
  front: '/',
  briefing: '/briefing',
  weekly: '/weekly',
  profile: '/settings/profile',
  ai: '/settings/ai',
  keywords: '/settings/keywords',
  devices: '/settings/devices',
  categories: '/settings/categories',
  feeds: '/feeds',
  login: '/login',
};
const wanted = process.env.ROUTES
  ? process.env.ROUTES.split(',').map((s) => s.trim()).filter((n) => ALL[n])
  : Object.keys(ALL);

mkdirSync(OUT, { recursive: true });

// 1) Register a throwaway account to obtain a JWT.
const email = `vqa_${Date.now()}@example.com`;
const reg = await fetch(`${BASE}/api/auth/register`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email, displayName: 'Visual QA', password: 'visualqa-pass-1' }),
});
if (!reg.ok) throw new Error(`register failed: ${reg.status} ${await reg.text()}`);
const { token } = await reg.json();
console.log('registered', email);

const browser = await chromium.launch({ executablePath: CHROME || undefined, args: ['--no-sandbox'] });
const page = await browser.newPage({ viewport: { width: 1280, height: 1500 } });
await page.addInitScript((t) => localStorage.setItem('tdr.token', t), token);

for (const name of wanted) {
  const path = ALL[name];
  try {
    await page.goto(`${BASE}${path}`, { waitUntil: 'networkidle', timeout: 30000 });
    await page.waitForFunction(() => !document.querySelector('.tdr-splash'), { timeout: 15000 }).catch(() => {});
    await page.waitForTimeout(700);
    await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: true });
    console.log('shot', name, path);
  } catch (e) {
    console.log('FAIL', name, path, e.message);
  }
}

await browser.close();
console.log('done →', OUT);
