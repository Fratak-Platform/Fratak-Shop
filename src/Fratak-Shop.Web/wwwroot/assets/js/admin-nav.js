// ═══════════════════════════════════════════════════════════════════════════
//  منوی کناری پنل: گروه‌های تاشو + جست‌وجوی منو
//  دور ۳۰ (#۱۱): «همهٔ گروه‌ها پیش‌فرض بسته‌اند» (مثل «فنی» یا «محتوا و سئو») و فقط با کلیک باز می‌شوند — جز گروه صفحهٔ فعلی (دور ۳۱).
//  • حالت باز/بسته دیگر در مرورگر ذخیره نمی‌شود (قبلاً گروه‌هایی که یک بار باز شده بودند همیشه باز می‌ماندند و منو شلوغ می‌شد).
//    کلید قدیمی «fs.nav.v1» هم پاک می‌شود.
//  • «آکاردئون»: با باز کردن یک گروه، گروه باز دیگر بسته می‌شود تا فهرست کوتاه بماند؛ ولی گروه صفحهٔ فعلی (پس از دور ۳۱)
//    هیچ‌وقت خودکار بسته نمی‌شود و ردیف آبی‌اش همیشه دیده می‌شود.
//  • اگر سرور گروه فعال را علامت نزده باشد، اسکریپت از روی آدرس صفحه دقیق‌ترین لینک را پیدا و فعال می‌کند (پشتیبان).
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

  // پشتیبان سمت مرورگر: اگر سرور گروه/ردیف فعال را علامت نزده بود (مثلاً HTML قدیمی از کش، یا آدرسی که سرور نشناخت)،
  // همین‌جا از روی آدرس صفحه دقیق‌ترین لینک منو پیدا می‌شود: «/admin/products/edit/12» ← «/admin/products».
  // همان لینک آبی (is-active) و گروهش باز و علامت‌دار (is-current) می‌شود. داشبورد («/admin») گروهی باز نمی‌کند.
  function pathOf(href) {
    try { return new URL(href, window.location.origin).pathname.replace(/\/+$/, "").toLowerCase() || "/"; }
    catch (e) { return ""; }
  }
  if (!current) {
    var here = pathOf(window.location.href);
    var best = null, bestLength = -1;
    Array.prototype.forEach.call(nav.querySelectorAll("details[data-nav-group] .a-nav-link"), function (link) {
      var target = pathOf(link.getAttribute("href"));
      if (!target || target === "/admin") return;
      var match = here === target || here.indexOf(target + "/") === 0;
      if (match && target.length > bestLength) { best = link; bestLength = target.length; }
    });
    if (best) {
      Array.prototype.forEach.call(nav.querySelectorAll("details[data-nav-group] .a-nav-link.is-active"), function (link) {
        link.classList.remove("is-active");
        link.removeAttribute("aria-current");
      });
      best.classList.add("is-active");
      best.setAttribute("aria-current", "page");
      current = best.closest("details[data-nav-group]");
      if (current) {
        current.setAttribute("data-nav-active", "1");
        var summary = current.querySelector("summary");
        if (summary) summary.classList.add("is-current");
      }
    }
  }

  // همه بسته به‌جز گروه داده‌شده و گروه صفحهٔ فعلی (حتی اگر مرورگر حالت قبلی را از کش صفحه برگرداند).
  // گروه صفحهٔ فعلی هیچ‌وقت خودکار بسته نمی‌شود: با باز کردن گروه دیگر (مثلاً «فنی» وقتی در «فهرست محصولات» هستید)
  // «محصولات و انبار» باز و ردیفش آبی می‌ماند؛ فقط اگر خود کاربر روی عنوانش بزند بسته می‌شود.
  function closeAll(except) {
    groups.forEach(function (group) {
      var keep = group === except || group === current;
      if (group.open !== keep) group.open = keep;
    });
  }
  function restore() { closeAll(current); }
  restore();
  // بازگشت با دکمهٔ «عقب» مرورگر (صفحه از کش برمی‌گردد): دوباره فقط گروه صفحهٔ همان صفحه باز می‌شود.
  window.addEventListener("pageshow", function (event) { if (event.persisted && !searching) { restore(); revealActive(); } });

  // لینک صفحهٔ فعلی در منوی بلند دیده شود: فقط خود منو اسکرول می‌شود (scrollIntoView صفحهٔ اصلی را هم جابه‌جا می‌کرد).
  // مثال: در «ویدیوهای پیشنهادی کالا» گروه «هوشمند (خودکار)» باز است و ردیف آبی وسط منو دیده می‌شود.
  function revealActive() {
    var link = (current && current.querySelector(".a-nav-link.is-active")) || nav.querySelector(".a-nav-link.is-active");
    if (!link) return;
    var navBox = nav.getBoundingClientRect();
    var linkBox = link.getBoundingClientRect();
    if (linkBox.top < navBox.top || linkBox.bottom > navBox.bottom) {
      nav.scrollTop += linkBox.top - navBox.top - (nav.clientHeight - link.offsetHeight) / 2;
    }
  }
  revealActive();

  // آکاردئون: باز شدن یک گروه ← بسته شدن بقیهٔ گروه‌ها جز گروه صفحهٔ فعلی (هنگام جست‌وجو نه، چون چند گروه نتیجه دارند).
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
