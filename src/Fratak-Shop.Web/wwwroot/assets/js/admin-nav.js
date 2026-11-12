// ═══════════════════════════════════════════════════════════════════════════
//  منوی کناری پنل: گروه‌های تاشو + جست‌وجوی منو
//  دور ۳۰ (#۱۱): «همهٔ گروه‌ها پیش‌فرض بسته‌اند» (مثل «فنی» یا «محتوا و سئو») و فقط با کلیک باز می‌شوند — جز گروه صفحهٔ فعلی (دور ۳۱).
//  • حالت باز/بسته دیگر در مرورگر ذخیره نمی‌شود (قبلاً گروه‌هایی که یک بار باز شده بودند همیشه باز می‌ماندند و منو شلوغ می‌شد).
//    کلید قدیمی «fs.nav.v1» هم پاک می‌شود.
//  • «آکاردئون»: با باز کردن یک گروه، گروه باز دیگر بسته می‌شود تا همیشه فقط یک فهرست کوتاه دیده شود.
//  • دور ۳۱ (#۷): گروهِ صفحهٔ فعلی (data-nav-active="1"، مثلاً «فنی» وقتی در «امنیت» هستید) باز است و ردیف صفحه پررنگ (is-active)؛
//    بقیهٔ گروه‌ها بسته‌اند. با پاک شدن جست‌وجو هم همین حالت برمی‌گردد. لینک فعال در دید منو اسکرول می‌شود.
//  • کادر جست‌وجو (input[data-nav-search]) لینک‌ها را با متن فیلتر می‌کند و گروه‌های دارای نتیجه را موقتاً باز می‌کند؛
//    با پاک شدن جست‌وجو همه دوباره بسته می‌شوند (جز گروه صفحهٔ فعلی). «ي/ك» عربی، نیم‌فاصله و فاصلهٔ اضافه یکسان می‌شوند
//    (مثال: «سفارشها» ← «سفارش‌ها»). Enter ← اولین نتیجه؛ Escape ← پاک کردن.
//  بدون اسکریپت درون‌خطی (سازگار با CSP) و بدون وابستگی.
// ═══════════════════════════════════════════════════════════════════════════
(function () {
  "use strict";
  var nav = document.querySelector(".a-nav");
  if (!nav) return;

  // پاک کردن حافظهٔ قدیمی باز/بسته (دور ۲۳) تا هیچ گروهی خودبه‌خود باز نشود.
  try { localStorage.removeItem("fs.nav.v1"); } catch (e) { /* حالت خصوصی مرورگر */ }

  function norm(text) {
    return (text || "").replace(/ي/g, "ی").replace(/ك/g, "ک").replace(/[\u200c\u200f\u200e]/g, "").replace(/\s+/g, "").toLowerCase();
  }

  var groups = Array.prototype.slice.call(nav.querySelectorAll("details[data-nav-group]"));
  var searching = false;

  // گروه صفحهٔ فعلی (یا null در داشبورد/صفحه‌های بیرون از گروه).
  var current = nav.querySelector("details[data-nav-group][data-nav-active]");

  // همه بسته به‌جز گروه داده‌شده (حتی اگر مرورگر حالت قبلی را از کش صفحه برگرداند).
  function closeAll(except) {
    groups.forEach(function (group) { group.open = group === except; });
  }
  function restore() { closeAll(current); }
  restore();
  window.addEventListener("pageshow", function (event) { if (event.persisted && !searching) restore(); });

  // لینک صفحهٔ فعلی در منوی بلند دیده شود (بدون پرش صفحهٔ اصلی).
  var activeLink = current && current.querySelector(".a-nav-link.is-active");
  if (activeLink && typeof activeLink.scrollIntoView === "function") {
    try { activeLink.scrollIntoView({ block: "nearest" }); } catch (e) { /* مرورگر قدیمی */ }
  }

  // آکاردئون: باز شدن یک گروه ← بسته شدن بقیه (هنگام جست‌وجو نه، چون چند گروه نتیجه دارند).
  groups.forEach(function (group) {
    group.addEventListener("toggle", function () {
      if (!searching && group.open) closeAll(group);
    });
  });

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
      if (section.tagName === "DETAILS") section.open = searching ? shown > 0 : section === current;
      visible += shown;
    });
    if (empty) empty.hidden = !(searching && visible === 0);
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
