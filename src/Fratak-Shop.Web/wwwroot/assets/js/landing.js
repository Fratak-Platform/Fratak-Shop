/*
  صفحه‌های کمپین (دور ۲۱): شمارش معکوس واقعی تا «پایان کمپین».
  نشانه‌گذاری: <div class="fs-lp-countdown" data-fs-countdown="2026-11-30T23:59:00+03:30"> با خانه‌های <b data-u="d|h|m|s">.
  - زمان از ساعت دستگاه کاربر و تاریخ ثابت پایان حساب می‌شود (با هر بار باز کردن صفحه از نو شروع نمی‌شود = بدون فوریت جعلی).
  - بعد از پایان، شمارنده با پیام «این کمپین به پایان رسید» جایگزین می‌شود.
  - ارقام فارسی؛ تب پنهان = به‌روزرسانی متوقف (مصرف باتری).
*/
(function () {
  "use strict";
  var FA = "۰۱۲۳۴۵۶۷۸۹";

  function fa(n) {
    return String(n).replace(/[0-9]/g, function (d) { return FA[+d]; });
  }

  function tick(el, end) {
    var left = Math.max(0, Math.floor((end - Date.now()) / 1000));
    if (left <= 0) {
      el.className = "fs-lp-countdown fs-lp-countdown--ended";
      el.textContent = "این کمپین به پایان رسید";
      return false;
    }
    var parts = { d: Math.floor(left / 86400), h: Math.floor((left % 86400) / 3600), m: Math.floor((left % 3600) / 60), s: left % 60 };
    Object.keys(parts).forEach(function (k) {
      var cell = el.querySelector('[data-u="' + k + '"]');
      if (cell) cell.textContent = fa(parts[k]);
    });
    return true;
  }

  function init() {
    var items = [];
    document.querySelectorAll("[data-fs-countdown]").forEach(function (el) {
      var end = Date.parse(el.getAttribute("data-fs-countdown") || "");
      if (isNaN(end)) {
        el.remove();
        return;
      }
      if (tick(el, end)) items.push({ el: el, end: end });
    });
    if (!items.length) return;
    var timer = setInterval(function () {
      if (document.hidden) return;
      items = items.filter(function (item) { return tick(item.el, item.end); });
      if (!items.length) clearInterval(timer);
    }, 1000);
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();
})();
