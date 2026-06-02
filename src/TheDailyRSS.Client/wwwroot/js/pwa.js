// Service-worker registration lives in this external file (not an inline <script>) so the
// app's strict CSP — script-src 'self' 'wasm-unsafe-eval', no 'unsafe-inline' — still applies.
// The worker itself runs under worker-src, which falls back to default-src 'self' (same origin).
if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('service-worker.js').catch(err =>
            console.error('Service worker registration failed:', err));
    });
}
