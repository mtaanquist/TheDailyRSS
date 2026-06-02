// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change). The published build uses
// service-worker.published.js, which provides the offline app-shell caching.
self.addEventListener('fetch', () => { });
