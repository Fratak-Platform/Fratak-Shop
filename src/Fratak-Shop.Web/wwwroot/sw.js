/*
 * Fratak Shop service worker – deliberately conservative:
 *  - only same-origin GET requests for static assets (css / js / fonts) are cached;
 *  - pages (HTML) are never cached, they may contain personal data (cart, profile, orders);
 *  - POST and other non-GET requests, APIs, payment and auth URLs are never touched;
 *  - when a page can't be loaded because the device is offline, /offline.html is shown;
 *  - web push: shows the notification sent by the shop and opens its (same-origin) link.
 * Bump VERSION to drop the old caches after changing this file.
 */
const VERSION = "v6"; // v6: آیکون اعلان از تنظیمات «استودیوی لوگو» (مرحلهٔ دوازدهم). v5: آیکون پیش‌فرض PWA مشکی با نشان فراتک (مرحلهٔ دهم). v4: مانیفست بدون کش و آیکون‌های نسخه‌دار. v3: هویت بصری جدید (فونت Vazirmatn، لوگو و آیکون‌های برند)
const CACHE = `fratak-static-${VERSION}`;
const OFFLINE_URL = "/offline.html";
const PRECACHE = [
  OFFLINE_URL,
  "/assets/css/output.css",
  "/assets/css/font.css",
  "/assets/font/vazirmatn-fd-arabic-wght.woff2",
  "/assets/brand/logo.svg",
  "/assets/brand/app-icon-192.png",
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

  // Precached brand files (logo / icon of the offline page) are handled too, so they show up offline.
  if (!url.pathname.startsWith("/assets/") || !(CACHEABLE.test(url.pathname) || PRECACHE.includes(url.pathname))) return;

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

// ── Web push ───────────────────────────────────────────────────────────
// Payload (JSON, encrypted by the server): { title, body, url, tag, icon }.
// icon فقط اگر مسیر همین سایت باشد («/…» و نه «//…») پذیرفته می‌شود؛ وگرنه آیکون برند.
self.addEventListener("push", (event) => {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch (_) {
    data = { body: event.data ? event.data.text() : "" };
  }
  const title = data.title || "فراتک";
  const icon = typeof data.icon === "string" && data.icon.startsWith("/") && !data.icon.startsWith("//") ? data.icon : "/assets/brand/app-icon-192.png";
  event.waitUntil(
    self.registration.showNotification(title, {
      body: data.body || "",
      icon,
      badge: "/assets/brand/notification-badge-96.png",
      tag: data.tag || undefined,
      renotify: !!data.tag,
      dir: "rtl",
      lang: "fa",
      data: { url: data.url || "/" },
    }),
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  let target = "/";
  try {
    const url = new URL((event.notification.data && event.notification.data.url) || "/", self.location.origin);
    if (url.origin === self.location.origin) target = url.pathname + url.search + url.hash;
  } catch (_) { /* keep "/" */ }

  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((windows) => {
      for (const client of windows) {
        if (new URL(client.url).pathname === target.split(/[?#]/)[0] && "focus" in client) return client.focus();
      }
      return self.clients.openWindow(target);
    }),
  );
});
