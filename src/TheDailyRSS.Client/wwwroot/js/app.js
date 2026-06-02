// Extracted from index.html so the page can ship a strict script-src CSP (no 'unsafe-inline').
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
