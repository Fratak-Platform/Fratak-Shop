/*
 * سنجش سرعت واقعی کاربران (Core Web Vitals) – دور سئوی خودکار، موارد ۱۳۸ تا ۱۴۹.
 * چه می‌کند؟ LCP، CLS، INP، FCP و TTFB همین بازدید را با API استاندارد مرورگر (PerformanceObserver) اندازه می‌گیرد و
 * هنگام ترک صفحه با sendBeacon به /api/rum می‌فرستد. بدون کوکی، بدون شناسهٔ کاربر، بدون کتابخانهٔ بیرونی.
 * نمونه‌گیری: نیمی از بازدیدها (بار سرور کم، آمار همچنان معتبر). خروجی در «پنل ← سئو ← ربات‌ها و سرعت».
 * مثال بدنه: {"t":"product","d":"mobile","m":{"LCP":2100,"CLS":0.02,"INP":120,"FCP":900,"TTFB":300}}
 */
(function () {
  'use strict';
  if (!('PerformanceObserver' in window) || !navigator.sendBeacon || Math.random() >= 0.5) return;

  var path = location.pathname;
  var type = path === '/' ? 'home'
    : path.indexOf('/product/') === 0 ? 'product'
    : (path.indexOf('/category/') === 0 || path.indexOf('/brand/') === 0) ? 'category'
    : path.indexOf('/blog/') === 0 ? 'post'
    : path === '/blog' ? 'blog'
    : (path === '/search' || path === '/products') ? 'search'
    : path.indexOf('/page/') === 0 ? 'page' : 'other';
  var device = window.matchMedia && window.matchMedia('(max-width: 768px)').matches ? 'mobile' : 'desktop';
  var metrics = {};
  var cls = 0, inp = 0, sent = false;

  function observe(kind, callback, extra) {
    try {
      var options = { type: kind, buffered: true };
      if (extra) for (var k in extra) options[k] = extra[k];
      new PerformanceObserver(function (list) { list.getEntries().forEach(callback); }).observe(options);
    } catch (e) { /* مرورگر قدیمی: این شاخص سنجیده نمی‌شود */ }
  }

  observe('largest-contentful-paint', function (entry) { metrics.LCP = Math.round(entry.startTime); });
  observe('paint', function (entry) { if (entry.name === 'first-contentful-paint') metrics.FCP = Math.round(entry.startTime); });
  observe('layout-shift', function (entry) { if (!entry.hadRecentInput) { cls += entry.value; metrics.CLS = Math.round(cls * 1000) / 1000; } });
  observe('event', function (entry) {
    if (entry.interactionId && entry.duration > inp) { inp = entry.duration; metrics.INP = Math.round(inp); }
  }, { durationThreshold: 40 });

  try {
    var nav = performance.getEntriesByType('navigation')[0];
    if (nav && nav.responseStart > 0) metrics.TTFB = Math.round(nav.responseStart);
  } catch (e) { /* بدون TTFB */ }

  function send() {
    if (sent || Object.keys(metrics).length === 0) return;
    sent = true;
    var body = JSON.stringify({ t: type, d: device, m: metrics });
    navigator.sendBeacon('/api/rum', new Blob([body], { type: 'application/json' }));
  }

  document.addEventListener('visibilitychange', function () { if (document.visibilityState === 'hidden') send(); });
  window.addEventListener('pagehide', send);
})();
