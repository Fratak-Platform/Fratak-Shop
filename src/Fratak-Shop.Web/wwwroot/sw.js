/*
 * Fratak Shop service worker – deliberately conservative:
 *  - only same-origin GET requests for static assets (css / js / fonts) are cached;
 *  - pages (HTML) are never cached, they may contain personal data (cart, profile, orders);
 *  - POST and other non-GET requests, APIs, payment and auth URLs are never touched;
 *  - when a page can't be loaded because the device is offline, /offline.html is shown.
 * Bump VERSION to drop the old caches after changing this file.
 */
const VERSION = "v1";
const CACHE = `fratak-static-${VERSION}`;
const OFFLINE_URL = "/offline.html";
const PRECACHE = [
  OFFLINE_URL,
  "/assets/css/output.css",
  "/assets/css/font.css",
  "/assets/font/YekanBakhFaNum-Regular.woff2",
  "/assets/image/logo.png",
  "/assets/image/pwa/icon-192.png",
];
const CACHEABLE = /\.(?:css|js|woff2?|ttf)$/i;
const MAX_ENTRIES = 60;

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE)
      .then((cache) => cache.addAll(PRECACHE.map((url) => new Request(url, { cache: "reload" }))))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((key) => key.startsWith("fratak-") && key !== CACHE).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  );
});

async function trim(cache) {
  const keys = await cache.keys();
  for (let i = 0; i < keys.length - MAX_ENTRIES; i++) {
    if (!PRECACHE.includes(new URL(keys[i].url).pathname)) await cache.delete(keys[i]);
  }
}

self.addEventListener("fetch", (event) => {
  const request = event.request;
  if (request.method !== "GET") return;

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;

  // Pages: always from the network; offline fallback only. Nothing is stored.
  if (request.mode === "navigate") {
    event.respondWith(fetch(request).catch(() => caches.match(OFFLINE_URL)));
    return;
  }

  if (!url.pathname.startsWith("/assets/") || !CACHEABLE.test(url.pathname)) return;

  // Versioned assets (?v=hash) never change → cache first. Others → network first,
  // the cached copy is only used offline (so an edited file is never served stale).
  const versioned = url.searchParams.has("v");
  event.respondWith(
    caches.open(CACHE).then(async (cache) => {
      if (versioned) {
        const cached = await cache.match(request);
        if (cached) return cached;
      }

      try {
        const response = await fetch(request);
        if (response.ok && response.type === "basic") {
          await cache.put(request, response.clone());
          trim(cache);
        }
        return response;
      } catch (error) {
        const cached = await cache.match(request, { ignoreSearch: !versioned });
        if (cached) return cached;
        throw error;
      }
    }),
  );
});
