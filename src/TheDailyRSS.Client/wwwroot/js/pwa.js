// Service-worker registration lives in this external file (not an inline <script>) so the
// app's strict CSP — script-src 'self' 'wasm-unsafe-eval', no 'unsafe-inline' — still applies.
// The worker itself runs under worker-src, which falls back to default-src 'self' (same origin).
if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('service-worker.js').catch(err =>
            console.error('Service worker registration failed:', err));
    });
}

// iOS home-screen (standalone) launch can come up with a stale/zoomed viewport scale, so the
// layout renders ~1.15x too large and clips on the right — even though the same page is fine in
// Safari. Re-asserting the viewport meta once after launch nudges WebKit to recompute the layout
// viewport at the true device width. We briefly clamp the scale, then restore the original content
// (which keeps initial-scale=1 + viewport-fit=cover + pinch-zoom) so accessibility isn't lost.
(function fixStandaloneViewportZoom() {
    if (!('standalone' in navigator) || !navigator.standalone) return; // iOS standalone only
    const vp = document.querySelector('meta[name="viewport"]');
    if (!vp) return;
    const original = vp.getAttribute('content');
    const reassert = () => {
        vp.setAttribute('content', original + ', maximum-scale=1, minimum-scale=1');
        requestAnimationFrame(() => vp.setAttribute('content', original));
    };
    reassert();
    // Orientation changes hit the same bug; re-assert after the rotation settles.
    window.addEventListener('orientationchange', () => setTimeout(reassert, 300));
})();
