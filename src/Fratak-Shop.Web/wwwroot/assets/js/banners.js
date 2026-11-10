/*
 * دور ۲۳ – آمار بنرها برای آزمون A/B (assets/js/banners.js)
 * • نمایش: وقتی دست‌کم نیمی از بنر [data-banner] برای اولین بار در صفحه دیده شود.
 * • کلیک: کلیک روی همان بنر (لینک مستقیم می‌ماند تا سئو و سرعت آسیب نبیند).
 * • ارسال: گروهی با navigator.sendBeacon به POST /api/banners/track هنگام ترک صفحه یا هر ۵ ثانیه (حداکثر ۱۲ شناسه).
 * بدون کوکی و بدون اطلاعات شخصی؛ مرورگر قدیمی (بدون IntersectionObserver/sendBeacon) ← هیچ کاری نمی‌کند.
 * مثال بدنه: {"v":["5f0c…"],"c":[]}
 */
(function () {
  'use strict';
  var nodes = document.querySelectorAll('[data-banner]');
  if (!nodes.length || !navigator.sendBeacon) return;

  var views = [], clicks = [], seen = {};
  var GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

  function flush() {
    if (!views.length && !clicks.length) return;
    var body = JSON.stringify({ v: views.splice(0, 12), c: clicks.splice(0, 12) });
    try { navigator.sendBeacon('/api/banners/track', new Blob([body], { type: 'application/json' })); } catch (e) { /* بی‌اهمیت */ }
  }

  function id(node) {
    var value = node.getAttribute('data-banner');
    return value && GUID.test(value) ? value : null;
  }

  if ('IntersectionObserver' in window) {
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        var key = id(entry.target);
        observer.unobserve(entry.target);
        if (key && !seen[key]) { seen[key] = 1; views.push(key); }
      });
    }, { threshold: 0.5 });
    Array.prototype.forEach.call(nodes, function (node) { observer.observe(node); });
  }

  document.addEventListener('click', function (event) {
    var node = event.target && event.target.closest ? event.target.closest('[data-banner]') : null;
    var key = node ? id(node) : null;
    if (!key) return;
    clicks.push(key);
    flush();
  }, true);

  setInterval(flush, 5000);
  document.addEventListener('visibilitychange', function () { if (document.visibilityState === 'hidden') flush(); });
  window.addEventListener('pagehide', flush);
})();
