/*
 * دیالوگ «پیشنهاد قیمت فروش» در فرم محصول پنل مدیریت (دور ۱۰).
 * محل بارگذاری: Areas/Admin/Pages/Products/Edit.cshtml (section Scripts).
 *
 * کار: قیمت خرید (از فیلد «قیمت خرید» فرم اگر هست)، هزینهٔ اضافهٔ همین کالا و نام کالا را می‌گیرد، به
 * POST ?handler=SuggestPrice می‌فرستد و نتیجه را نشان می‌دهد: قیمت پیشنهادی، سود خالص، کل هزینه، سربه‌سر و
 * فهرست قیمت فروشگاه‌های دیگر (مثلاً دیجی‌کالا ۳۰۰٬۰۰۰، فروشگاه X ۲۸۰٬۰۰۰). دکمهٔ «استفاده از این قیمت»
 * عدد را در «قیمت فروش» فرم می‌گذارد؛ ذخیره با خود کارمند است.
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
  let lastSuggested = null;
  let opener = null;

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
    setTimeout(() => (readMoney($("#pa-cost")) ? $("[data-advisor-run]") : $("#pa-cost")).focus(), 0);
  }

  function close() {
    dialog.hidden = true;
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

  function offerRow(offer, css) {
    const tr = document.createElement("tr");
    if (css) tr.className = css;
    const shop = document.createElement("td");
    const url = safeUrl(offer.url);
    if (url) {
      const a = document.createElement("a");
      a.className = "a-link";
      a.href = url;
      a.target = "_blank";
      a.rel = "noopener noreferrer";
      a.textContent = offer.shop;
      shop.appendChild(a);
    } else {
      shop.textContent = offer.shop;
    }
    if (offer.title) shop.title = offer.title;
    const price = document.createElement("td");
    price.className = "num";
    price.textContent = fa(offer.price);
    const source = document.createElement("td");
    source.textContent = css === "is-ignored" ? `${offer.source} (پرت؛ حساب نشد)` : offer.source;
    tr.append(shop, price, source);
    return tr;
  }

  function render(data) {
    lastSuggested = data.suggested;
    setField("suggested", toman(data.suggested));
    setField("profit", `${toman(data.profit)} (${fa(data.profitPercent)}٪)`);
    setField("totalCost", toman(data.totalCost));
    setField("breakEven", toman(data.breakEven));
    setField("minPrice", `${toman(data.minPrice)} (سود ${fa(data.costs.min)}٪)`);
    setField("targetPrice", `${toman(data.targetPrice)} (سود ${fa(data.costs.target)}٪)`);
    setField("reason", data.reason);
    setField("matched", data.matched ? `کالای انتخاب‌شده در ترب: ${data.matched}` : "");

    const body = $("[data-advisor-offers]");
    body.replaceChildren();
    data.offers.forEach((offer, index) => body.appendChild(offerRow(offer, index === 0 ? "is-lowest" : "")));
    data.ignored.forEach((offer) => body.appendChild(offerRow(offer, "is-ignored")));
    if (!data.offers.length && !data.ignored.length) {
      const tr = document.createElement("tr");
      const td = document.createElement("td");
      td.colSpan = 3;
      td.className = "a-muted";
      td.textContent = "قیمتی از سایت‌های دیگر نیامد.";
      tr.appendChild(td);
      body.appendChild(tr);
    }

    const problems = $("[data-advisor-problems]");
    problems.replaceChildren();
    data.problems.forEach((text) => {
      const li = document.createElement("li");
      li.textContent = text;
      problems.appendChild(li);
    });
    $("[data-advisor-result]").hidden = false;
  }

  function run(costsOnly) {
    const purchase = readMoney($("#pa-cost"));
    if (!purchase || purchase === "0") {
      status("قیمت خرید را وارد کنید.");
      $("#pa-cost").focus();
      return;
    }
    const body = new FormData();
    body.append("purchase", purchase);
    body.append("extra", readMoney($("#pa-extra")) || "0");
    body.append("query", $("#pa-query").value.trim());
    body.append("lookup", costsOnly ? "false" : "true");
    status(costsOnly ? "در حال محاسبه…" : "در حال جست‌وجوی قیمت در سایت‌های دیگر (تا حدود ۱۵ ثانیه)…");
    dialog.querySelectorAll("[data-advisor-run]").forEach((b) => (b.disabled = true));
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
      .finally(() => dialog.querySelectorAll("[data-advisor-run]").forEach((b) => (b.disabled = false)));
  }

  openButtons.forEach((button) => button.addEventListener("click", () => open(button)));
  dialog.addEventListener("click", (e) => {
    if (e.target.closest("[data-advisor-close]")) return close();
    const runButton = e.target.closest("[data-advisor-run]");
    if (runButton) return run(runButton.dataset.advisorRun === "costs");
    if (e.target.closest("[data-advisor-apply]") && lastSuggested) {
      writeMoney(document.getElementById("p-price"), Math.round(lastSuggested));
      const discount = document.getElementById("p-dprice");
      if (discount && Number(readMoney(discount)) >= lastSuggested) {
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
      run(false);
    }
  });
})();
