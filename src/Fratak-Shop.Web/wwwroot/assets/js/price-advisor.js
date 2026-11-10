/*
 * دیالوگ «پیشنهاد قیمت فروش» در فرم محصول پنل مدیریت (دور ۱۰؛ بازنویسی مرحلهٔ پانزدهم).
 * محل بارگذاری: Areas/Admin/Pages/Products/Edit.cshtml (section Scripts).
 *
 * کار: قیمت خرید (از فیلد «قیمت خرید» فرم اگر هست)، هزینهٔ اضافهٔ همین کالا و نام کالا را می‌گیرد، به
 * POST ?handler=SuggestPrice می‌فرستد و نتیجه را نشان می‌دهد:
 *   • کارت‌ها: قیمت پیشنهادی، سود خالص، کل هزینه، کمترین قیمت مجاز، قیمت با سود هدف و جایگاه ما در ترب با این قیمت؛
 *   • نوار بازهٔ قیمت رقبا (کمترین ← بیشترین) با نشانهٔ میانه و قیمت پیشنهادی؛
 *   • جدول همهٔ قیمت‌ها (ترب، دیجی‌کالا، باسلام، سایت‌های دیگر) با داوری سیستم و دلیل؛ زبانه‌های «همه/حساب‌شده/کنارگذاشته» و فیلتر منبع؛
 *   • دکمهٔ «حذف» برای هر قیمت (لینک اشتباه، کالای دیگر، کالای غیرهم‌ارز) و «حساب کن» برای قیمت کنارگذاشته.
 *     این انتخاب‌ها فقط در حافظهٔ همین دیالوگ‌اند و با هر کلیک دوباره محاسبه می‌شود؛ دفعهٔ بعد (جست‌وجوی تازه) پاک می‌شوند.
 * دکمهٔ «استفاده از این قیمت» عدد را در «قیمت فروش» فرم می‌گذارد؛ ذخیره با خود کارمند است.
 *
 * امنیت: همهٔ متن‌های دریافتی با textContent نوشته می‌شوند (نه innerHTML) و لینک‌ها فقط http/https هستند.
 */
(function () {
  "use strict";

  const dialog = document.getElementById("price-advisor");
  const openButtons = document.querySelectorAll("[data-price-advisor-open]");
  if (!dialog || !openButtons.length) return;

  const $ = (selector) => dialog.querySelector(selector);
  const csrf = () => (document.querySelector('meta[name="csrf-token"]') || {}).content || "";
  const fa = (n) => (n === null || n === undefined ? "-" : Number(n).toLocaleString("fa-IR"));
  const toman = (n) => (n === null || n === undefined ? "-" : `${fa(n)} تومان`);
  const digits = (text) => String(text || "")
    .replace(/[۰-۹]/g, (d) => "۰۱۲۳۴۵۶۷۸۹".indexOf(d))
    .replace(/[٠-٩]/g, (d) => "٠١٢٣٤٥٦٧٨٩".indexOf(d))
    .split(/[.٫]/)[0]
    .replace(/[^\d]/g, "");

  /** عنوان و رنگ داوری (همان CompetitorDisplay.cs سمت سرور). */
  const VERDICTS = {
    ok: ["حساب شد", "a-badge-green"],
    forced: ["حساب شد (دستی)", "a-badge-green"],
    own: ["فروشگاه خودمان", "a-badge-gray"],
    excluded: ["حذف دستی", "a-badge-gray"],
    mismatch: ["کالای دیگر", "a-badge-gray"],
    model: ["مدل دیگر", "a-badge-yellow"],
    accessory: ["لوازم جانبی", "a-badge-gray"],
    duplicate: ["تکراری", "a-badge-gray"],
    low: ["خیلی ارزان (پرت)", "a-badge-yellow"],
    high: ["خیلی گران (پرت)", "a-badge-yellow"],
  };

  let last = null; // آخرین پاسخ سرور
  let opener = null;
  let tab = "all";
  let source = "";
  // انتخاب‌های کارمند فقط برای همین محاسبه (با جست‌وجوی تازه پاک می‌شوند).
  const excluded = new Set();
  const included = new Set();
  let lastQuery = null;

  /** مقدار ورودی مبلغ (با جداکننده یا بدون آن). */
  function readMoney(input) {
    return input ? digits(input.value) : "";
  }

  /** نوشتن در ورودی مبلغ و خبر دادن به fa-inputs.js تا جداکننده و ورودی مخفی به‌روز شوند. */
  function writeMoney(input, value) {
    if (!input) return;
    input.value = String(value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
  }

  function open(button) {
    opener = button;
    const cost = readMoney(document.getElementById("p-cost"));
    if (cost) writeMoney($("#pa-cost"), cost);
    const query = $("#pa-query");
    if (!query.value) {
      const name = document.getElementById("p-name");
      const english = document.getElementById("p-en");
      query.value = ((name && name.value) || (english && english.value) || "").trim().slice(0, 120);
    }
    dialog.hidden = false;
    document.documentElement.classList.add("a-no-scroll");
    setTimeout(() => (readMoney($("#pa-cost")) ? $("[data-advisor-run]") : $("#pa-cost")).focus(), 0);
  }

  function close() {
    dialog.hidden = true;
    document.documentElement.classList.remove("a-no-scroll");
    if (opener) opener.focus();
  }

  function status(text) {
    $("[data-advisor-status]").textContent = text || "";
  }

  function setField(name, text) {
    const el = $(`[data-advisor-field="${name}"]`);
    if (el) {
      el.textContent = text || "";
      el.hidden = !text;
    }
  }

  function safeUrl(url) {
    try {
      const parsed = new URL(url);
      return parsed.protocol === "https:" || parsed.protocol === "http:" ? parsed.href : null;
    } catch {
      return null;
    }
  }

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
  }

  /** یک ردیف جدول: فروشگاه (+ عنوان کالا)، منبع، قیمت و فاصله با پیشنهاد، داوری با دلیل، دکمهٔ حذف/حساب کن. */
  function offerRow(offer) {
    const tr = el("tr", offer.accepted ? "" : "is-rejected");
    if (offer.accepted && last && last.reference !== null && Number(offer.price) === Number(last.reference)) tr.classList.add("is-reference");

    const shop = el("td");
    const url = safeUrl(offer.url);
    if (url) {
      const a = el("a", "a-link", offer.shop);
      a.href = url;
      a.target = "_blank";
      a.rel = "noopener noreferrer nofollow";
      shop.appendChild(a);
    } else {
      shop.appendChild(el("span", "", offer.shop));
    }
    if (offer.title) {
      const title = el("span", "block text-xs a-muted a-competitor-title", offer.title);
      title.title = offer.title;
      shop.appendChild(title);
    }

    const src = el("td", "text-xs", offer.source);

    const price = el("td", "num");
    price.appendChild(el("span", "", fa(offer.price)));
    if (offer.diff !== null && offer.diff !== undefined && offer.diff !== 0) {
      const diff = el("small", `block a-ltr ${offer.diff < 0 ? "a-down" : "a-up"}`, `${offer.diff > 0 ? "+" : ""}${offer.diff}٪`);
      diff.title = "نسبت به قیمت پیشنهادی";
      price.appendChild(diff);
    }

    const verdict = el("td");
    const meta = VERDICTS[offer.code] || [offer.code, "a-badge-gray"];
    const badge = el("span", `a-badge ${meta[1]}`, meta[0]);
    verdict.appendChild(badge);
    if (offer.reason) verdict.appendChild(el("span", "block text-xs a-muted", offer.reason));
    if (offer.match !== null && offer.match !== undefined) {
      verdict.appendChild(el("span", "block text-xs a-muted", `شباهت عنوان: ${fa(offer.match)}٪`));
    }

    const actions = el("td", "whitespace-nowrap");
    const button = el("button", "a-btn a-btn-sm " + (offer.accepted ? "a-btn-danger-soft" : "a-btn-light"));
    button.type = "button";
    button.dataset.offerToggle = offer.id;
    if (offer.accepted) {
      button.textContent = "حذف";
      button.title = "این قیمت فقط در همین محاسبه حساب نشود (لینک اشتباه، کالای دیگر یا غیرهم‌ارز)";
    } else if (offer.code === "own") {
      button.hidden = true;
    } else {
      button.textContent = offer.code === "excluded" ? "برگرداندن" : "حساب کن";
      button.title = "این قیمت در همین محاسبه حساب شود";
    }
    actions.appendChild(button);

    tr.append(shop, src, price, verdict, actions);
    return tr;
  }

  function visible(offer) {
    if (source && offer.source !== source) return false;
    if (tab === "accepted") return offer.accepted;
    if (tab === "rejected") return !offer.accepted;
    return true;
  }

  function renderTable() {
    const body = $("[data-advisor-offers]");
    body.replaceChildren();
    const offers = (last && last.offers) || [];
    const rows = offers.filter(visible);
    rows.forEach((offer) => body.appendChild(offerRow(offer)));
    if (!rows.length) {
      const tr = el("tr");
      const td = el("td", "a-muted", offers.length ? "در این فیلتر قیمتی نیست." : "قیمتی از سایت‌های دیگر نیامد.");
      td.colSpan = 5;
      tr.appendChild(td);
      body.appendChild(tr);
    }

    const count = (fn) => fa(offers.filter((o) => (!source || o.source === source) && fn(o)).length);
    $('[data-tab-count="all"]').textContent = count(() => true);
    $('[data-tab-count="accepted"]').textContent = count((o) => o.accepted);
    $('[data-tab-count="rejected"]').textContent = count((o) => !o.accepted);
    dialog.querySelectorAll("[data-advisor-tab]").forEach((b) => {
      const active = b.dataset.advisorTab === tab;
      b.classList.toggle("is-active", active);
      b.setAttribute("aria-selected", active ? "true" : "false");
    });
    dialog.querySelectorAll("[data-advisor-source]").forEach((b) => b.classList.toggle("is-active", b.dataset.advisorSource === source));
  }

  /** فیلتر منبع: «همه» + هر منبعی که در نتیجه هست (ترب، دیجی‌کالا، باسلام، سایت‌های دیگر). */
  function renderSources(offers) {
    const box = $("[data-advisor-sources]");
    box.replaceChildren();
    const names = [...new Set(offers.map((o) => o.source))];
    if (names.length < 2) {
      source = "";
      return;
    }
    if (source && !names.includes(source)) source = "";
    const chip = (value, label) => {
      const b = el("button", "a-chip", label);
      b.type = "button";
      b.dataset.advisorSource = value;
      box.appendChild(b);
    };
    chip("", "همهٔ منابع");
    names.forEach((name) => chip(name, `${name} (${fa(offers.filter((o) => o.source === name).length)})`));
  }

  /** نوار بازه: کمترین تا بیشترین قیمت حساب‌شده؛ نشانهٔ میانه و قیمت پیشنهادی روی آن. */
  function renderRange(data) {
    const box = $("[data-advisor-range]");
    const min = data.lowest;
    const max = data.highest;
    if (min === null || max === null || max <= min) {
      box.hidden = true;
      return;
    }
    const lo = Math.min(min, data.suggested);
    const hi = Math.max(max, data.suggested);
    // در RTL «راست» شروع است؛ با right درصد را از شروع نوار حساب می‌کنیم.
    const at = (value) => `${(((value - lo) / (hi - lo)) * 100).toFixed(1)}%`;
    const median = box.querySelector("[data-range-median]");
    median.style.right = at(data.median ?? min);
    median.title = `میانه: ${toman(data.median)}`;
    const ours = box.querySelector("[data-range-ours]");
    ours.style.right = at(data.suggested);
    ours.title = `قیمت پیشنهادی: ${toman(data.suggested)}`;
    box.querySelector("[data-range-min]").textContent = `کمترین ${fa(min)}`;
    box.querySelector("[data-range-mid]").textContent = `میانه ${fa(data.median)}`;
    box.querySelector("[data-range-max]").textContent = `بیشترین ${fa(max)}`;
    box.hidden = false;
  }

  function render(data) {
    last = data;
    setField("suggested", toman(data.suggested));
    setField("profit", `${toman(data.profit)} (${fa(data.profitPercent)}٪)`);
    setField("totalCost", toman(data.totalCost));
    setField("minPrice", `${toman(data.minPrice)} (سود ${fa(data.costs.min)}٪)`);
    setField("targetPrice", `${toman(data.targetPrice)} (سود ${fa(data.costs.target)}٪)`);
    if (data.torob) {
      const t = data.torob;
      setField("torob", t.rank === 1
        ? `ارزان‌ترین از ${fa(t.sellers + 1)}`
        : `${fa(t.rank)} از ${fa(t.sellers + 1)} (ارزان‌ترین ترب: ${fa(t.lowest)})`);
    } else {
      setField("torob", "فروشنده‌ای در ترب نبود");
    }
    let reason = data.reason;
    if (data.torob && data.torob.cheaperElsewhere > 0) {
      reason += ` ${fa(data.torob.cheaperElsewhere)} فروشگاه بیرون از ترب ارزان‌تر از قیمت پیشنهادی است (در جدول ببینید).`;
    }
    setField("reason", reason);
    setField("matched", data.matched ? `کالای انتخاب‌شده در ترب: ${data.matched}` : "");
    renderRange(data);
    renderSources(data.offers || []);
    renderTable();

    const problems = $("[data-advisor-problems]");
    problems.replaceChildren();
    (data.problems || []).forEach((text) => problems.appendChild(el("li", "", text)));
    $("[data-advisor-result]").hidden = false;
  }

  function run(costsOnly, keepChoices) {
    const purchase = readMoney($("#pa-cost"));
    if (!purchase || purchase === "0") {
      status("قیمت خرید را وارد کنید.");
      $("#pa-cost").focus();
      return;
    }
    const query = $("#pa-query").value.trim();
    // جست‌وجوی تازه (یا عبارت دیگر) ← حذف/حساب‌کردن‌های قبلی پاک می‌شوند.
    if (!keepChoices || query !== lastQuery) {
      excluded.clear();
      included.clear();
      tab = "all";
    }
    lastQuery = query;
    const body = new FormData();
    body.append("purchase", purchase);
    body.append("extra", readMoney($("#pa-extra")) || "0");
    body.append("query", query);
    body.append("lookup", costsOnly ? "false" : "true");
    // دور ۲۳ (مورد ۶۹): دستهٔ انتخاب‌شده در فرم ← کف سود همان دسته (اگر تعیین شده باشد) اعمال می‌شود.
    const category = document.getElementById("p-cat");
    if (category && category.value) body.append("categoryId", category.value);
    excluded.forEach((id) => body.append("exclude", id));
    included.forEach((id) => body.append("include", id));
    status(costsOnly ? "در حال محاسبه…"
      : keepChoices ? "در حال محاسبهٔ دوباره…"
      : "در حال جست‌وجوی قیمت در ترب، دیجی‌کالا، باسلام و سایت‌های دیگر (تا حدود ۳۰ ثانیه)…");
    dialog.querySelectorAll("[data-advisor-run],[data-offer-toggle]").forEach((b) => (b.disabled = true));
    fetch(dialog.dataset.url, {
      method: "POST",
      body,
      credentials: "same-origin",
      headers: { "X-Requested-With": "fetch", RequestVerificationToken: csrf(), Accept: "application/json" },
    })
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))))
      .then((data) => {
        if (!data.ok) {
          status(data.message || "محاسبه انجام نشد.");
          return;
        }
        status("");
        render(data);
      })
      .catch((error) => status(error.message === "403" || error.message === "404" ? "اجازهٔ ویرایش محصول ندارید." : "ارتباط برقرار نشد؛ دوباره امتحان کنید."))
      .finally(() => dialog.querySelectorAll("[data-advisor-run],[data-offer-toggle]").forEach((b) => (b.disabled = false)));
  }

  /** حذف/حساب کن یک قیمت (فقط همین محاسبه) و محاسبهٔ دوباره با همان نتایج کش‌شده. */
  function toggle(id) {
    const offer = ((last && last.offers) || []).find((o) => o.id === id);
    if (!offer) return;
    if (offer.accepted) {
      included.delete(id);
      excluded.add(id);
    } else if (offer.code === "excluded") {
      excluded.delete(id);
    } else {
      excluded.delete(id);
      included.add(id);
    }
    run(false, true);
  }

  openButtons.forEach((button) => button.addEventListener("click", () => open(button)));
  dialog.addEventListener("click", (e) => {
    if (e.target.closest("[data-advisor-close]")) return close();
    const runButton = e.target.closest("[data-advisor-run]");
    if (runButton) return run(runButton.dataset.advisorRun === "costs", false);
    const toggleButton = e.target.closest("[data-offer-toggle]");
    if (toggleButton) return toggle(toggleButton.dataset.offerToggle);
    const tabButton = e.target.closest("[data-advisor-tab]");
    if (tabButton) {
      tab = tabButton.dataset.advisorTab;
      return renderTable();
    }
    const sourceButton = e.target.closest("[data-advisor-source]");
    if (sourceButton) {
      source = sourceButton.dataset.advisorSource;
      return renderTable();
    }
    if (e.target.closest("[data-advisor-apply]") && last && last.suggested) {
      writeMoney(document.getElementById("p-price"), Math.round(last.suggested));
      const discount = document.getElementById("p-dprice");
      if (discount && Number(readMoney(discount)) >= last.suggested) {
        writeMoney(discount, "");
      }
      close();
      if (window.FratakModal) window.FratakModal.toast("قیمت فروش در فرم گذاشته شد؛ برای ثبت، محصول را ذخیره کنید.", "success");
    }
  });
  dialog.addEventListener("keydown", (e) => {
    if (e.key === "Escape") close();
    if (e.key === "Enter" && e.target.matches("input")) {
      e.preventDefault();
      run(false, false);
    }
  });
})();
