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

/*
 * دور ۲۶ – «بنر زنده» (assets/js/banners.js)
 * بنرهای زنده (.fsb-wrap) وقتی دست‌کم ۳۰٪ در صفحه دیده شوند کلاس is-live می‌گیرند تا حرکت ورود عنصرها از همان لحظه شروع شود؛
 * بیرون رفتن از صفحه ← حرکت چرخشی متوقف (باتری و پردازنده). بنر بالای صفحه از سرور is-live دارد.
 * بدون IntersectionObserver ← همه زنده می‌شوند. «کاهش حرکت» سیستم‌عامل ← CSS حرکت را خاموش می‌کند.
 */
(function () {
  'use strict';
  var nodes = document.querySelectorAll('.fsb-wrap');
  if (!nodes.length) return;
  // دور ۲۷: JS فعال است ← عنصرهای ورودی تا لحظهٔ دیده شدن پنهان بمانند (بدون «چشمک» پیش از حرکت ورود).
  Array.prototype.forEach.call(nodes, function (node) { node.classList.add('fsb-armed'); });
  setupParallax(nodes);
  if (!('IntersectionObserver' in window)) {
    Array.prototype.forEach.call(nodes, function (node) { node.classList.add('is-live'); });
    return;
  }
  var observer = new IntersectionObserver(function (entries) {
    entries.forEach(function (entry) {
      var node = entry.target;
      if (entry.isIntersecting) {
        node.classList.add('is-live');
        node.classList.remove('is-paused');
      } else if (node.classList.contains('is-live')) {
        node.classList.add('is-paused');
      }
    });
  }, { threshold: 0.3 });
  Array.prototype.forEach.call(nodes, function (node) { observer.observe(node); });

  /*
   * دور ۲۷ – پارالاکس: لایه‌های fsb-z1..3 (پس‌زمینه، میانه، کالا) با سرعت متفاوت جابه‌جا می‌شوند.
   * • موس (دستگاه دارای hover): موقعیت موس روی بنر ← --px/--py بین ‎-1 و 1.
   * • لمسی: موقعیت بنر در صفحه هنگام اسکرول ← --py (کالا آرام بالا/پایین می‌رود).
   * با requestAnimationFrame (حداکثر یک‌بار در هر فریم) و فقط برای بنرهای دیده‌شده؛ «کاهش حرکت» ← هیچ کاری نمی‌کند.
   */
  function setupParallax(list) {
    if (!window.matchMedia || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    var items = Array.prototype.filter.call(list, function (node) { return node.querySelector('.fsb-z1, .fsb-z2, .fsb-z3'); });
    if (!items.length) return;
    var hover = window.matchMedia('(hover: hover)').matches;
    var pending = false;
    function set(node, x, y) {
      node.style.setProperty('--px', Math.max(-1, Math.min(1, x)).toFixed(3));
      node.style.setProperty('--py', Math.max(-1, Math.min(1, y)).toFixed(3));
    }
    if (hover) {
      items.forEach(function (node) {
        node.addEventListener('pointermove', function (event) {
          if (pending) return;
          pending = true;
          requestAnimationFrame(function () {
            pending = false;
            var box = node.getBoundingClientRect();
            if (!box.width || !box.height) return;
            set(node, ((event.clientX - box.left) / box.width - 0.5) * 2, ((event.clientY - box.top) / box.height - 0.5) * 2);
          });
        }, { passive: true });
        node.addEventListener('pointerleave', function () { set(node, 0, 0); }, { passive: true });
      });
      return;
    }
    function onScroll() {
      if (pending) return;
      pending = true;
      requestAnimationFrame(function () {
        pending = false;
        var vh = window.innerHeight || 1;
        items.forEach(function (node) {
          if (!node.classList.contains('is-live') || node.classList.contains('is-paused')) return;
          var box = node.getBoundingClientRect();
          set(node, 0, ((box.top + box.height / 2) / vh - 0.5) * 2);
        });
      });
    }
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
  }
})();
