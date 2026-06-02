// Extracted from index.html so the page can ship a strict script-src CSP (no 'unsafe-inline').

// Engine facts read once by the .NET host at startup (Program.cs -> BrowserEnv).
window.tdrEnv = {
    // Safari/WebKit, which includes every iOS browser (all WebKit under the hood). Chromium and
    // Gecko UAs also carry "AppleWebKit", so the Chromium family (incl. Android) is excluded.
    isWebKit: () => {
        const ua = navigator.userAgent;
        return /AppleWebKit/.test(ua) && !/Chrome|Chromium|Android|Edg\//.test(ua);
    }
};

// Feed images load straight from arbitrary external hosts and some fail (404, hotlink block, etc.).
// Degrade a failed photo to the striped placeholder by dropping .has-photo and removing the <img>.
// Resource 'error' events don't bubble, so listen in the capture phase; an inline onerror= would be
// blocked by the strict CSP (script-src 'self', no 'unsafe-inline').
document.addEventListener('error', (e) => {
    const img = e.target;
    if (!(img instanceof HTMLImageElement)) return;
    const box = img.closest('.tdr-img.has-photo');
    if (!box) return;
    box.classList.remove('has-photo');
    img.remove();
}, true);

window.tdrDownload = (filename, text, mime) => {
    const blob = new Blob([text], { type: mime || 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = filename;
    document.body.appendChild(a); a.click();
    document.body.removeChild(a); URL.revokeObjectURL(url);
};

// Share a link via the native share sheet (mobile) or the clipboard (desktop). Returns
// 'shared' | 'copied' | 'cancelled' so the caller can show the right confirmation.
window.tdrShare = async (url, title) => {
    try {
        if (navigator.share) { await navigator.share({ title, url }); return 'shared'; }
        await navigator.clipboard.writeText(url);
        return 'copied';
    } catch (_) {
        return 'cancelled';
    }
};

// Forward wheel scrolls over the empty side gutters into the centre column,
// so a scroll started outside the 1100px shell still moves the news. The left
// menu does the same — unless it's tall enough to have its own scrollbar, in
// which case we leave it to scroll itself.
window.addEventListener('wheel', (e) => {
    if (e.ctrlKey) return; // leave pinch-zoom alone
    const main = document.querySelector('.tdr-main');
    if (!main) return;
    if (e.target.closest('.tdr-main')) return; // real scroll region handles itself
    const sidebar = e.target.closest('.tdr-sidebar');
    if (sidebar && sidebar.scrollHeight > sidebar.clientHeight) return; // sidebar has its own scrollbar
    main.scrollTop += e.deltaY;
    e.preventDefault();
}, { passive: false });

// Global F5 / "r" → in-place refresh (handled by Blazor, not a full page reload).
window.tdrHotkeys = {
    ref: null,
    handler: null,
    register: (dotnetRef) => {
        window.tdrHotkeys.ref = dotnetRef;
        window.tdrHotkeys.handler = (e) => {
            const t = e.target;
            const typing = t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable);
            const isRefresh = e.key === 'F5' || (e.key === 'r' && !typing && !e.ctrlKey && !e.metaKey && !e.altKey);
            if (!isRefresh) return;
            e.preventDefault();
            dotnetRef.invokeMethodAsync('OnRefreshHotkey');
        };
        window.addEventListener('keydown', window.tdrHotkeys.handler, true);
    },
    unregister: () => {
        if (window.tdrHotkeys.handler)
            window.removeEventListener('keydown', window.tdrHotkeys.handler, true);
        window.tdrHotkeys.handler = null;
        window.tdrHotkeys.ref = null;
    }
};

// Per-route scroll memory for the .tdr-main news column. We persist its scrollTop (keyed by the
// full path) as the reader scrolls, then a page restores it once its content has rendered — so
// "back" from an article lands where you left off, while a freshly-opened article starts at the top.
window.tdrScroll = {
    _key: () => 'tdr.scroll:' + location.pathname + location.search,
    _bound: false,
    init: () => {
        if (window.tdrScroll._bound) return;
        window.tdrScroll._bound = true;
        let raf = 0;
        // scroll events don't bubble; the capture phase still catches them on .tdr-main.
        document.addEventListener('scroll', (e) => {
            const m = e.target;
            if (!m || !m.classList || !m.classList.contains('tdr-main')) return;
            if (raf) return;
            raf = requestAnimationFrame(() => {
                raf = 0;
                try { sessionStorage.setItem(window.tdrScroll._key(), String(m.scrollTop)); } catch (_) { /* storage off */ }
            });
        }, true);
    },
    // Restore the saved offset for the current route (0 if none — e.g. a fresh article opens at the top).
    restore: () => {
        const m = document.querySelector('.tdr-main');
        if (!m) return;
        let v = 0;
        try { const s = sessionStorage.getItem(window.tdrScroll._key()); if (s !== null) v = parseInt(s, 10) || 0; } catch (_) { /* storage off */ }
        m.scrollTop = v;
    }
};

// Pull-to-refresh for the .tdr-main news column. The app shell is pinned to the viewport
// (the document itself never scrolls), so the browser's own pull-to-refresh never fires — we
// re-create the gesture on the scroll region and reuse the app's in-place reload (the same
// path as the F5/"r" hotkey, via MainLayout.OnPullRefresh -> AppState.RequestReloadAsync).
window.tdrPullRefresh = {
    THRESHOLD: 72,   // px of pull needed to trigger a refresh
    MAX: 110,        // px cap for the rubber-band travel
    ref: null, main: null, ind: null,
    startY: 0, dist: 0, armed: false, pulling: false, busy: false,

    register(dotnetRef) {
        this.ref = dotnetRef;
        this.main = document.querySelector('.tdr-main');
        this.ind = document.querySelector('.tdr-ptr');
        if (!this.main || !this.ind) return;
        this._ts = (e) => this._onStart(e);
        this._tm = (e) => this._onMove(e);
        this._te = () => this._onEnd();
        // touchmove must be non-passive so we can suppress native scroll while pulling.
        this.main.addEventListener('touchstart', this._ts, { passive: true });
        this.main.addEventListener('touchmove', this._tm, { passive: false });
        this.main.addEventListener('touchend', this._te, { passive: true });
        this.main.addEventListener('touchcancel', this._te, { passive: true });
    },

    unregister() {
        if (this.main) {
            this.main.removeEventListener('touchstart', this._ts);
            this.main.removeEventListener('touchmove', this._tm);
            this.main.removeEventListener('touchend', this._te);
            this.main.removeEventListener('touchcancel', this._te);
        }
        this.ref = null; this.main = null; this.ind = null;
    },

    _onStart(e) {
        if (this.busy || e.touches.length !== 1) { this.armed = false; return; }
        this.armed = this.main.scrollTop <= 0; // only a pull that begins at the top counts
        this.startY = e.touches[0].clientY;
        this.dist = 0; this.pulling = false;
    },

    _onMove(e) {
        if (!this.armed || this.busy) return;
        const dy = e.touches[0].clientY - this.startY;
        if (dy <= 0) { // moved back up — hand control to normal scrolling
            if (this.pulling) this._reset();
            this.armed = this.main.scrollTop <= 0;
            return;
        }
        e.preventDefault(); // we own this gesture now; stop native scroll/overscroll
        this.pulling = true;
        this.dist = Math.min(this.MAX, dy * 0.5); // damped for a rubber-band feel
        this.ind.style.transform = `translateX(-50%) translateY(${this.dist}px)`;
        this.ind.style.opacity = String(Math.min(1, this.dist / this.THRESHOLD));
        this.ind.classList.toggle('ready', this.dist >= this.THRESHOLD);
    },

    async _onEnd() {
        if (!this.pulling || this.busy) { this.armed = false; return; }
        const trigger = this.dist >= this.THRESHOLD;
        this.armed = false;
        if (!trigger) { this._reset(); return; }
        // Snap to the resting position and spin while the in-place reload runs.
        this.busy = true;
        this.ind.classList.remove('ready');
        this.ind.classList.add('busy');
        this.ind.style.transform = `translateX(-50%) translateY(${this.THRESHOLD}px)`;
        this.ind.style.opacity = '1';
        try { if (this.ref) await this.ref.invokeMethodAsync('OnPullRefresh'); }
        catch (_) { /* a failed reload still needs the indicator cleared */ }
        finally {
            this.busy = false;
            this.ind.classList.remove('busy');
            this._reset();
        }
    },

    _reset() {
        this.pulling = false; this.dist = 0;
        this.ind.classList.remove('ready');
        this.ind.classList.add('settling');
        this.ind.style.transform = 'translateX(-50%) translateY(0)';
        this.ind.style.opacity = '0';
        setTimeout(() => { if (this.ind) this.ind.classList.remove('settling'); }, 220);
    }
};
