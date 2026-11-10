// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۳ – منوی کناری پنل: گروه‌های تاشو با حافظه + جست‌وجوی منو
//  • هر گروه (details[data-nav-group]) باز/بسته بودنش را در localStorage مرورگر همان کارمند نگه می‌دارد
//    (کلید «fs.nav.v1»). گروهی که صفحهٔ فعلی در آن است (data-nav-active) همیشه باز می‌ماند.
//  • کادر جست‌وجو (input[data-nav-search]) لینک‌ها را با متن فیلتر می‌کند؛ «ي/ك» عربی، نیم‌فاصله و فاصلهٔ اضافه
//    یکسان می‌شوند (مثال: «سفارشها» ← «سفارش‌ها»). Enter ← اولین نتیجه؛ Escape ← پاک کردن.
//  بدون اسکریپت درون‌خطی (سازگار با CSP) و بدون وابستگی.
// ═══════════════════════════════════════════════════════════════════════════
(function () {
  "use strict";
  var KEY = "fs.nav.v1";
  var nav = document.querySelector(".a-nav");
  if (!nav) return;

  function load() {
    try { return JSON.parse(localStorage.getItem(KEY) || "{}") || {}; } catch (e) { return {}; }
  }
  function save(state) {
    try { localStorage.setItem(KEY, JSON.stringify(state)); } catch (e) { /* حالت خصوصی مرورگر: فقط ذخیره نمی‌شود */ }
  }
  function norm(text) {
    return (text || "").replace(/ي/g, "ی").replace(/ك/g, "ک").replace(/[\u200c\u200f\u200e]/g, "").replace(/\s+/g, "").toLowerCase();
  }

  var state = load();
  var groups = Array.prototype.slice.call(nav.querySelectorAll("details[data-nav-group]"));
  var searching = false;

  // بازگرداندن وضعیت قبلی (گروه صفحهٔ فعلی همیشه باز).
  groups.forEach(function (group) {
    var name = group.getAttribute("data-nav-group");
    if (group.hasAttribute("data-nav-active")) { group.open = true; return; }
    if (Object.prototype.hasOwnProperty.call(state, name)) group.open = !!state[name];
  });
  groups.forEach(function (group) {
    group.addEventListener("toggle", function () {
      if (searching) return; // باز شدن خودکار هنگام جست‌وجو ذخیره نمی‌شود
      state[group.getAttribute("data-nav-group")] = group.open;
      save(state);
    });
  });

  // لینک فعال در دید باشد (منوی بلند).
  var active = nav.querySelector(".a-nav-link.is-active");
  if (active && active.scrollIntoView) {
    try { active.scrollIntoView({ block: "nearest" }); } catch (e) { /* مرورگر قدیمی */ }
  }

  var input = document.querySelector("[data-nav-search]");
  var empty = nav.querySelector("[data-nav-empty]");
  if (!input) return;

  function filter() {
    var query = norm(input.value);
    searching = query.length > 0;
    var visible = 0;
    Array.prototype.forEach.call(nav.querySelectorAll("[data-nav-sec]"), function (section) {
      var links = section.querySelectorAll(".a-nav-link");
      var shown = 0;
      Array.prototype.forEach.call(links, function (link) {
        var match = !searching || norm(link.textContent).indexOf(query) >= 0;
        link.hidden = !match;
        if (match) shown++;
      });
      section.hidden = searching && shown === 0;
      if (section.tagName === "DETAILS") {
        if (searching) { if (shown > 0) section.open = true; }
        else {
          var name = section.getAttribute("data-nav-group");
          section.open = section.hasAttribute("data-nav-active") || !!state[name];
        }
      }
      visible += shown;
    });
    if (empty) empty.hidden = !(searching && visible === 0);
    // پایان جست‌وجو: رویدادهای toggle همگام‌شده ذخیره نشوند.
    if (!searching) setTimeout(function () { searching = false; }, 0);
  }

  input.addEventListener("input", filter);
  input.addEventListener("keydown", function (event) {
    if (event.key === "Escape") { input.value = ""; filter(); }
    if (event.key === "Enter") {
      var first = nav.querySelector(".a-nav-link:not([hidden])");
      if (first && input.value) { event.preventDefault(); window.location.href = first.getAttribute("href"); }
    }
  });
})();
