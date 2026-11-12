/*
 * ورودی‌های فارسی (بدون وابستگی، سازگار با CSP – بدون اسکریپت درون‌خطی)
 * محل استفاده: هم پنل مدیریت (_AdminLayout) و هم سایت (_Layout).
 *
 *  ۱) تقویم شمسی:   <input data-jalali>          → «1404/08/03»
 *                   <input data-jalali="datetime"> → «1404/08/03 14:30»
 *     ورودی‌هایی که placeholder آن‌ها شبیه «1404/01/01» (یا «1404/08/01 14:30») است هم خودکار تقویم می‌گیرند،
 *     پس فرم‌های قدیمی بدون تغییر کار می‌کنند. مقدار با ارقام لاتین نوشته می‌شود و سرور
 *     (PersianDateInput.Parse) هر دو نوع رقم را می‌پذیرد.
 *
 *  ۲) مبلغ:         <input data-money>  یا  <input data-money="ریال">
 *     هنگام تایپ جداکنندهٔ سه‌رقمی می‌گیرد (1,250,000) و زیرش «یک میلیون و دویست و پنجاه هزار تومان» نوشته می‌شود.
 *     عدد خام در یک ورودی مخفی هم‌نام ارسال می‌شود تا سرور همیشه «1250000» بگیرد.
 *
 *  ۳) مبلغ به حروف روی متن‌ها: هر کانتینر [data-amount-words] (مثلاً محتوای پنل) جست‌وجو می‌شود و
 *     هر عدد کنار «تومان» یک راهنمای شناور (title) با مبلغ به حروف می‌گیرد؛ روی موبایل با لمس نمایش داده می‌شود.
 *
 *  API عمومی: window.FratakFa = { toWords, toJalali, toGregorian, formatNumber, attach(root) }
 */
(function () {
  "use strict";

  // ── عدد به حروف (همان منطق PersianNumberWords در سرور) ─────────────────
  const ONES = ["", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه"];
  const TEENS = ["ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده"];
  const TENS = ["", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"];
  const HUNDREDS = ["", "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد"];
  const SCALES = ["", "هزار", "میلیون", "میلیارد", "تریلیون", "کوادریلیون"];

  function groupToWords(n) {
    const parts = [];
    const h = Math.floor(n / 100);
    const rest = n % 100;
    if (h) parts.push(HUNDREDS[h]);
    if (rest >= 10 && rest < 20) parts.push(TEENS[rest - 10]);
    else {
      if (rest >= 20) parts.push(TENS[Math.floor(rest / 10)]);
      if (rest % 10) parts.push(ONES[rest % 10]);
    }
    return parts.join(" و ");
  }

  /** مثال: toWords(1250000) → «یک میلیون و دویست و پنجاه هزار» */
  function toWords(value) {
    let n = Math.floor(Math.abs(Number(value) || 0));
    if (n === 0) return "صفر";
    const groups = [];
    let scale = 0;
    while (n > 0 && scale < SCALES.length) {
      const g = n % 1000;
      if (g) groups.unshift(scale ? `${groupToWords(g)} ${SCALES[scale]}` : groupToWords(g));
      n = Math.floor(n / 1000);
      scale++;
    }
    return (Number(value) < 0 ? "منفی " : "") + groups.join(" و ");
  }

  const toLatinDigits = (text) => String(text || "")
    .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 1776))
    .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 1632));

  /** مثال: formatNumber("1250000") → «1,250,000» */
  const formatNumber = (digits) => String(digits).replace(/\B(?=(\d{3})+(?!\d))/g, ",");

  // ── تبدیل تاریخ شمسی ↔ میلادی (الگوریتم jalaali، دقیق برای سال‌های ۱۲۰۰ تا ۱۶۰۰) ──
  const BREAKS = [-61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178];
  const div = (a, b) => ~~(a / b);
  const mod = (a, b) => a - ~~(a / b) * b;

  function jalCal(jy) {
    let jp = BREAKS[0], jm, jump = 0, leapJ = -14, n, i;
    const gy = jy + 621;
    for (i = 1; i < BREAKS.length; i++) {
      jm = BREAKS[i];
      jump = jm - jp;
      if (jy < jm) break;
      leapJ = leapJ + div(jump, 33) * 8 + div(mod(jump, 33), 4);
      jp = jm;
    }
    n = jy - jp;
    leapJ = leapJ + div(n, 33) * 8 + div(mod(n, 33) + 3, 4);
    if (mod(jump, 33) === 4 && jump - n === 4) leapJ += 1;
    const leapG = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150;
    const march = 20 + leapJ - leapG;
    if (jump - n < 6) n = n - jump + div(jump + 4, 33) * 33;
    let leap = mod(mod(n + 1, 33) - 1, 4);
    if (leap === -1) leap = 4;
    return { leap, gy, march };
  }

  function g2d(gy, gm, gd) {
    let d = div((gy + div(gm - 8, 6) + 100100) * 1461, 4) + div(153 * mod(gm + 9, 12) + 2, 5) + gd - 34840408;
    d = d - div(div(gy + 100100 + div(gm - 8, 6), 100) * 3, 4) + 752;
    return d;
  }

  function d2g(jdn) {
    let j = 4 * jdn + 139361631;
    j = j + div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
    const i = div(mod(j, 1461), 4) * 5 + 308;
    const gd = div(mod(i, 153), 5) + 1;
    const gm = mod(div(i, 153), 12) + 1;
    const gy = div(j, 1461) - 100100 + div(8 - gm, 6);
    return { gy, gm, gd };
  }

  function j2d(jy, jm, jd) {
    const r = jalCal(jy);
    return g2d(r.gy, 3, r.march) + (jm - 1) * 31 - div(jm, 7) * (jm - 7) + jd - 1;
  }

  function d2j(jdn) {
    const gy = d2g(jdn).gy;
    let jy = gy - 621;
    const r = jalCal(jy);
    const jdn1f = g2d(gy, 3, r.march);
    let k = jdn - jdn1f;
    if (k >= 0) {
      if (k <= 185) return { jy, jm: 1 + div(k, 31), jd: mod(k, 31) + 1 };
      k -= 186;
    } else {
      jy -= 1;
      k += 179;
      if (r.leap === 1) k += 1;
    }
    return { jy, jm: 7 + div(k, 30), jd: mod(k, 30) + 1 };
  }

  const toJalali = (date) => d2j(g2d(date.getFullYear(), date.getMonth() + 1, date.getDate()));
  const toGregorian = (jy, jm, jd) => { const g = d2g(j2d(jy, jm, jd)); return new Date(g.gy, g.gm - 1, g.gd); };
  const monthLength = (jy, jm) => (jm <= 6 ? 31 : jm <= 11 ? 30 : jalCal(jy).leap === 0 ? 30 : 29);

  const MONTHS = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
  const WEEK = ["ش", "ی", "د", "س", "چ", "پ", "ج"];
  const pad = (n) => String(n).padStart(2, "0");
  const DATE_PLACEHOLDER = /^1[34]\d\d\/\d\d\/\d\d( \d\d:\d\d)?$/;

  // ── تقویم شمسی ─────────────────────────────────────────────────────────
  let popup = null;
  let current = null; // { input, withTime, jy, jm }

  function parseValue(text) {
    const m = toLatinDigits(text).trim().match(/^(\d{4})[/\-.](\d{1,2})[/\-.](\d{1,2})(?:\s+(\d{1,2}):(\d{2}))?$/);
    if (!m) return null;
    const v = { jy: +m[1], jm: +m[2], jd: +m[3], hh: m[4] ? +m[4] : null, mi: m[5] ? +m[5] : null };
    return v.jm >= 1 && v.jm <= 12 && v.jd >= 1 && v.jd <= monthLength(v.jy, v.jm) ? v : null;
  }

  /**
   * ساعت پیش‌فرض وقتی هنوز ساعتی انتخاب نشده: data-jalali-time="23:59" (مثلاً «پایان تخفیف» ← آخر روز)، وگرنه ۰۰:۰۰
   * (قبلاً ۱۲:۰۰ بود و پایان حراج ۳ روزه وسط روز سوم تمام می‌شد).
   */
  function defaultTime(input) {
    const m = String(input.dataset.jalaliTime || "").match(/^(\d{1,2}):(\d{2})$/);
    return m ? { hh: Math.min(23, +m[1]), mi: Math.min(59, +m[2]) } : { hh: 0, mi: 0 };
  }

  function closePicker() {
    if (popup) popup.hidden = true;
    current = null;
  }

  function writeValue(jy, jm, jd) {
    const input = current.input;
    let text = `${jy}/${pad(jm)}/${pad(jd)}`;
    if (current.withTime) {
      const hh = popup.querySelector("[data-jp-hh]").value || "00";
      const mi = popup.querySelector("[data-jp-mi]").value || "00";
      text += ` ${pad(Math.min(23, +hh || 0))}:${pad(Math.min(59, +mi || 0))}`;
    }
    input.value = text;
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
  }

  function render() {
    const { jy, jm, input } = current;
    const selected = parseValue(input.value);
    const today = toJalali(new Date());
    // روز هفتهٔ اول ماه: شنبه = 0
    const first = toGregorian(jy, jm, 1).getDay();
    const offset = (first + 1) % 7;
    const days = monthLength(jy, jm);

    let cells = WEEK.map((w) => `<span class="fa-jp-week">${w}</span>`).join("");
    for (let i = 0; i < offset; i++) cells += "<span></span>";
    for (let d = 1; d <= days; d++) {
      const cls = ["fa-jp-day"];
      if (selected && selected.jy === jy && selected.jm === jm && selected.jd === d) cls.push("is-selected");
      if (today.jy === jy && today.jm === jm && today.jd === d) cls.push("is-today");
      if ((offset + d) % 7 === 0) cls.push("is-friday");
      cells += `<button type="button" class="${cls.join(" ")}" data-jp-day="${d}">${d.toLocaleString("fa-IR")}</button>`;
    }

    const time = current.withTime
      ? `<div class="fa-jp-time"><label>ساعت <input data-jp-hh inputmode="numeric" maxlength="2" value="${pad(selected?.hh ?? defaultTime(input).hh)}"></label>:<input data-jp-mi inputmode="numeric" maxlength="2" value="${pad(selected?.mi ?? defaultTime(input).mi)}"></div>`
      : "";

    popup.innerHTML =
      `<div class="fa-jp-head">
         <button type="button" data-jp-nav="1" aria-label="ماه بعد">‹</button>
         <strong>${MONTHS[jm - 1]} ${jy.toLocaleString("fa-IR", { useGrouping: false })}</strong>
         <button type="button" data-jp-nav="-1" aria-label="ماه قبل">›</button>
       </div>
       <div class="fa-jp-grid">${cells}</div>${time}
       <div class="fa-jp-foot">
         <button type="button" data-jp-today>امروز</button>
         <button type="button" data-jp-clear>پاک کردن</button>
         <button type="button" data-jp-close>بستن</button>
       </div>`;
  }

  function position(input) {
    const rect = input.getBoundingClientRect();
    const width = 272;
    let left = rect.right - width + window.scrollX; // راست‌چین با ورودی
    left = Math.max(8 + window.scrollX, Math.min(left, window.scrollX + document.documentElement.clientWidth - width - 8));
    let top = rect.bottom + window.scrollY + 4;
    // اگر پایین صفحه جا نیست، بالای ورودی باز شود.
    if (rect.bottom + 340 > window.innerHeight && rect.top > 340) top = rect.top + window.scrollY - 344;
    popup.style.left = `${left}px`;
    popup.style.top = `${top}px`;
  }

  function openPicker(input) {
    if (!popup) {
      popup = document.createElement("div");
      popup.className = "fa-jp";
      popup.setAttribute("role", "dialog");
      popup.setAttribute("aria-label", "انتخاب تاریخ شمسی");
      popup.hidden = true;
      document.body.appendChild(popup);
      popup.addEventListener("mousedown", (e) => { if (!e.target.closest("input")) e.preventDefault(); });
      popup.addEventListener("click", onPopupClick);
      // دور ۳۱ (#۱۰): تغییر ساعت/دقیقه بعد از انتخاب روز هم در ورودی نوشته می‌شود (قبلاً فقط کلیک روی روز ساعت را ثبت می‌کرد و
      // ساعتی که بعداً عوض می‌شد گم می‌شد؛ حراج چندروزه با ساعت دلخواه درست ذخیره نمی‌شد).
      popup.addEventListener("input", (e) => {
        if (!current || !current.withTime || !e.target.matches("[data-jp-hh], [data-jp-mi]")) return;
        const selected = parseValue(current.input.value);
        if (selected) writeValue(selected.jy, selected.jm, selected.jd);
      });
    }
    const value = parseValue(input.value) || toJalali(new Date());
    current = { input, withTime: input.dataset.jalali === "datetime" || / \d\d:\d\d$/.test(input.placeholder || ""), jy: value.jy, jm: value.jm };
    render();
    popup.hidden = false;
    position(input);
  }

  function onPopupClick(e) {
    if (!current) return;
    const t = e.target.closest("button");
    if (!t) return;
    if (t.dataset.jpNav) {
      let m = current.jm + Number(t.dataset.jpNav);
      let y = current.jy;
      if (m > 12) { m = 1; y++; }
      if (m < 1) { m = 12; y--; }
      current.jm = m;
      current.jy = y;
      render();
    } else if (t.dataset.jpDay) {
      writeValue(current.jy, current.jm, Number(t.dataset.jpDay));
      if (!current.withTime) closePicker(); else render();
    } else if (t.hasAttribute("data-jp-today")) {
      const today = toJalali(new Date());
      writeValue(today.jy, today.jm, today.jd);
      closePicker();
    } else if (t.hasAttribute("data-jp-clear")) {
      current.input.value = "";
      current.input.dispatchEvent(new Event("input", { bubbles: true }));
      closePicker();
    } else if (t.hasAttribute("data-jp-close")) {
      closePicker();
    }
  }

  function attachDate(input) {
    if (input.dataset.jpReady) return;
    input.dataset.jpReady = "1";
    input.setAttribute("autocomplete", "off");
    input.addEventListener("focus", () => openPicker(input));
    input.addEventListener("click", () => { if (!current || current.input !== input) openPicker(input); });
    input.addEventListener("keydown", (e) => { if (e.key === "Escape" || e.key === "Tab") closePicker(); });
    // تایپ دستی: ارقام فارسی به لاتین تبدیل می‌شود تا قالب یکدست بماند.
    input.addEventListener("blur", () => { input.value = toLatinDigits(input.value).trim(); });
  }

  document.addEventListener("mousedown", (e) => {
    if (popup && !popup.hidden && !popup.contains(e.target) && (!current || e.target !== current.input)) closePicker();
  });
  window.addEventListener("resize", () => { if (current) position(current.input); });

  // ── مبلغ با جداکننده و حروف ──────────────────────────────────────────────
  function attachMoney(input) {
    if (input.dataset.moneyReady) return;
    input.dataset.moneyReady = "1";
    const unit = input.dataset.money && input.dataset.money !== "true" ? input.dataset.money : "تومان";
    // حداقل/حداکثر قبل از تبدیل نوع خوانده می‌شود تا اعتبارسنجی مرورگر از دست نرود (با setCustomValidity).
    const min = input.hasAttribute("min") ? Number(input.getAttribute("min")) : null;
    const max = input.hasAttribute("max") ? Number(input.getAttribute("max")) : null;
    // ورودی عددی جداکننده نمی‌پذیرد؛ به متنی با صفحه‌کلید عددی تبدیل می‌شود.
    if (input.type === "number") {
      input.type = "text";
      input.inputMode = "numeric";
    }
    input.classList.add("fa-money");
    // مقدار خام (بدون جداکننده) در یک ورودی مخفی هم‌نام نگه داشته می‌شود و ورودی قابل‌مشاهده نام ندارد؛
    // پس فرم در هر حالتی (submit معمولی، form.submit() بعد از پنجرهٔ تأیید، FormData در fetch) عدد خام می‌فرستد.
    const hidden = document.createElement("input");
    hidden.type = "hidden";
    if (input.name) {
      hidden.name = input.name;
      input.dataset.moneyName = input.name;
      input.removeAttribute("name");
    }
    const words = document.createElement("div");
    words.className = "fa-money-words";
    words.setAttribute("aria-live", "polite");
    input.insertAdjacentElement("afterend", words);
    input.insertAdjacentElement("afterend", hidden);

    const update = () => {
      // بخش اعشاری حذف می‌شود (مقدار سرور ممکن است «150000.00» باشد؛ تومان اعشار ندارد).
      const raw = toLatinDigits(input.value).split(/[.٫]/)[0].replace(/[^\d]/g, "").replace(/^0+(?=\d)/, "");
      const caretFromEnd = input.value.length - (input.selectionStart ?? input.value.length);
      input.value = raw ? formatNumber(raw) : "";
      hidden.value = raw;
      if (document.activeElement === input) {
        const pos = Math.max(0, input.value.length - caretFromEnd);
        input.setSelectionRange(pos, pos);
      }
      words.textContent = raw && raw !== "0" ? `${toWords(raw)} ${unit}` : "";
      const n = raw ? Number(raw) : null;
      input.setCustomValidity(
        n !== null && min !== null && n < min ? `حداقل مبلغ ${formatNumber(String(min))} ${unit} است.`
          : n !== null && max !== null && n > max ? `حداکثر مبلغ ${formatNumber(String(max))} ${unit} است.` : "");
    };
    input.addEventListener("input", update);
    input.addEventListener("change", update);
    update();
  }

  // ── راهنمای مبلغ به حروف روی متن‌ها ─────────────────────────────────────
  const AMOUNT = /(\d{1,3}(?:,\d{3})+|\d{4,})(\s*)(تومان|ریال)/g;
  const HAS_AMOUNT = /(\d{1,3}(?:,\d{3})+|\d{4,})\s*(تومان|ریال)/; // بدون g تا test حالت نگه ندارد

  function annotateAmounts(root) {
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
      acceptNode: (node) => {
        const parent = node.parentElement;
        if (!parent || parent.closest("script,style,textarea,input,select,option,code,pre,.fa-amount,[contenteditable]")) return NodeFilter.FILTER_REJECT;
        return HAS_AMOUNT.test(node.nodeValue) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_SKIP;
      },
    });
    const nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);
    nodes.forEach((node) => {
      const fragment = document.createDocumentFragment();
      let last = 0;
      const text = node.nodeValue;
      AMOUNT.lastIndex = 0;
      let m;
      while ((m = AMOUNT.exec(text))) {
        fragment.append(text.slice(last, m.index));
        const span = document.createElement("span");
        span.className = "fa-amount";
        span.title = `${toWords(m[1].replace(/,/g, ""))} ${m[3]}`;
        span.tabIndex = 0;
        span.textContent = m[0];
        fragment.append(span);
        last = m.index + m[0].length;
      }
      fragment.append(text.slice(last));
      node.replaceWith(fragment);
    });
  }

  // لمس روی موبایل: نمایش حروف در یک حباب کوچک (title روی لمس نمایش داده نمی‌شود).
  let bubble = null;
  document.addEventListener("click", (e) => {
    const amount = e.target.closest(".fa-amount");
    if (!amount) { if (bubble) bubble.hidden = true; return; }
    if (!window.matchMedia("(hover: none)").matches) return;
    if (!bubble) { bubble = document.createElement("div"); bubble.className = "fa-amount-bubble"; document.body.appendChild(bubble); }
    const rect = amount.getBoundingClientRect();
    bubble.textContent = amount.title;
    bubble.hidden = false;
    bubble.style.top = `${rect.bottom + window.scrollY + 6}px`;
    bubble.style.left = `${Math.max(8, rect.left + window.scrollX - 40)}px`;
  });

  // ── اتصال ────────────────────────────────────────────────────────────────
  function attach(root) {
    const scope = root || document;
    scope.querySelectorAll("input[data-jalali], input[type=text][placeholder], input:not([type])[placeholder]").forEach((input) => {
      if (input.hasAttribute("data-jalali") || DATE_PLACEHOLDER.test(toLatinDigits(input.placeholder))) attachDate(input);
    });
    scope.querySelectorAll("input[data-money]").forEach(attachMoney);
    scope.querySelectorAll("[data-amount-words]").forEach(annotateAmounts);
  }

  window.FratakFa = { toWords, toJalali, toGregorian, formatNumber, monthLength, attach };
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", () => attach());
  else attach();
})();
