/*
 * دور ۲۴ (بستهٔ B) – «تجربهٔ خرید» فروشگاه. همهٔ پنجره‌ها سفارشی‌اند (هیچ alert/confirm مرورگر)،
 * هیچ متنی با innerHTML ساخته نمی‌شود (امن در برابر XSS) و هیچ استایل درون‌خطی ندارد (CSP). ظاهر: assets/css/commerce.css.
 *
 *   [data-quick-view="id"]      نمایش سریع کالا در پنجره            ← GET  /api/products/{id}/quick   (پیشنهاد ۳۱)
 *   [data-compare-add="id"]     افزودن به مقایسه + سینی پایین صفحه   ← POST /api/compare               (پیشنهاد ۳۲)
 *   [data-search-q] … [data-product-id]  ثبت کلیک نتیجهٔ جست‌وجو     ← POST /api/search/click          (پیشنهاد ۳۰)
 *   [data-image-search]         جست‌وجو با عکس                       ← POST /api/search/image          (پیشنهاد ۲۹)
 *   [data-voice-search]         جست‌وجوی صوتی (فقط اگر مرورگر پشتیبانی کند؛ بدون سرویس بیرونی)  (پیشنهاد ۲۳)
 *   [data-social-proof="id"]    آمار واقعی خرید اخیر                 ← GET  /api/products/{id}/proof   (پیشنهاد ۱۵)
 *   [data-price-match="id"]     «قیمت کمتر دیدید؟»                    ← POST /api/price-match           (پیشنهاد ۱۱)
 *
 * توکن ضدجعل از /api/antiforgery گرفته و در هدر RequestVerificationToken فرستاده می‌شود؛ اگر منقضی شده بود (۴۰۰) یک‌بار تازه می‌شود.
 */
(function () {
  "use strict";

  if (window.FratakCommerce) return;

  // ── ابزارهای کوچک ─────────────────────────────────────────────

  /** ساخت عنصر با کلاس و متن (متن همیشه textContent است). مثال: el("p", "fc-muted", "سلام") */
  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  }

  /** پیام کوتاه پایین صفحه (از modal.js). */
  function toast(message, type) {
    if (window.FratakModal && message) window.FratakModal.toast(message, type || "info");
  }

  /** فقط آدرس‌های داخلی سایت (جلوگیری از تزریق پیوند بیرونی از پاسخ سرور). مثال: "/product/x" ← درست؛ "//evil" ← "/" */
  function safePath(url) {
    return typeof url === "string" && url.charAt(0) === "/" && url.charAt(1) !== "/" && url.charAt(1) !== "\\" ? url : "/";
  }

  /** ارقام فارسی/عربی ← انگلیسی و حذف جداکننده. مثال: "۱٬۲۵۰٬۰۰۰" ← "1250000" */
  function toLatinDigits(value) {
    return String(value || "")
      .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
      .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660))
      .replace(/[,٬،\s]/g, "");
  }

  /** عدد با جداکنندهٔ هزارگان. مثال: 1250000 ← "1,250,000" */
  function groupDigits(value) {
    return String(value).replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  }

  let tokenPromise = null;

  /** توکن ضدجعل (یک‌بار در هر بارگذاری صفحه، مگر fresh=true). */
  function token(fresh) {
    if (!tokenPromise || fresh) {
      tokenPromise = fetch("/api/antiforgery", { credentials: "same-origin", cache: "no-store" })
        .then((r) => (r.ok ? r.json() : { token: "" }))
        .then((d) => (d && d.token) || "")
        .catch(() => "");
    }
    return tokenPromise;
  }

  /**
   * ارسال امن به API. body یا شیء (JSON) است یا FormData. خروجی: { status, data }.
   * اگر توکن منقضی شده باشد (۴۰۰ با پیام نشست) یک‌بار با توکن تازه تکرار می‌شود.
   */
  async function send(url, body, retried) {
    const headers = { RequestVerificationToken: await token(retried) };
    const isForm = typeof FormData !== "undefined" && body instanceof FormData;
    if (!isForm) headers["Content-Type"] = "application/json";
    let response;
    try {
      response = await fetch(url, {
        method: "POST",
        credentials: "same-origin",
        headers,
        body: isForm ? body : JSON.stringify(body),
      });
    } catch (e) {
      return { status: 0, data: null };
    }
    let data = null;
    try {
      data = response.status === 204 ? null : await response.json();
    } catch (e) {
      data = null;
    }
    if (response.status === 400 && !retried && data && /نشست/.test(data.error || "")) return send(url, body, true);
    return { status: response.status, data };
  }

  async function getJson(url) {
    try {
      const r = await fetch(url, { credentials: "same-origin" });
      return r.ok ? await r.json() : null;
    } catch (e) {
      return null;
    }
  }

  // ── پنجرهٔ سفارشی ─────────────────────────────────────────────

  let openDialog = null;

  /**
   * باز کردن پنجره. خروجی: { body, close }. Esc، کلیک بیرون و دکمهٔ × می‌بندند؛ فوکوس برمی‌گردد.
   * مثال: const d = dialog("نمایش سریع"); d.body.appendChild(...)
   */
  function dialog(title, narrow) {
    if (openDialog) openDialog.close();
    const previous = document.activeElement;
    const backdrop = el("div", "fc-backdrop");
    const box = el("div", "fc-dialog" + (narrow ? " fc-narrow" : ""));
    box.setAttribute("role", "dialog");
    box.setAttribute("aria-modal", "true");
    box.setAttribute("tabindex", "-1");
    const head = el("div", "fc-head");
    const heading = el("h2", "fc-title", title);
    heading.id = "fc-title-" + Date.now();
    box.setAttribute("aria-labelledby", heading.id);
    const closeBtn = el("button", "fc-close", "×");
    closeBtn.type = "button";
    closeBtn.setAttribute("aria-label", "بستن");
    head.append(heading, closeBtn);
    const body = el("div", "fc-body");
    box.append(head, body);
    backdrop.appendChild(box);

    function onKey(e) {
      if (e.key === "Escape") close();
      if (e.key === "Tab") {
        const items = box.querySelectorAll("a[href],button:not([disabled]),input,select,textarea,[tabindex]:not([tabindex='-1'])");
        if (!items.length) return;
        const first = items[0];
        const last = items[items.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
      }
    }
    function close() {
      if (!backdrop.isConnected) return;
      backdrop.remove();
      document.removeEventListener("keydown", onKey);
      document.documentElement.classList.remove("fc-lock");
      if (openDialog && openDialog.backdrop === backdrop) openDialog = null;
      if (previous && previous.focus) previous.focus();
    }
    closeBtn.addEventListener("click", close);
    backdrop.addEventListener("click", (e) => { if (e.target === backdrop) close(); });
    document.addEventListener("keydown", onKey);
    document.body.appendChild(backdrop);
    document.documentElement.classList.add("fc-lock");
    box.focus();
    openDialog = { backdrop, close };
    return { body, close, box };
  }

  /**
   * دور ۲۸ (#۷۷ – «اسکلت بارگذاری»): به‌جای متن خالی «در حال بارگذاری…»، طرح خاکستری متحرک همان چیدمان (عکس + چند خط)
   * نشان داده می‌شود تا کاربر بداند چه چیزی در راه است. متن برای صفحه‌خوان‌ها پنهان می‌ماند (aria-busy).
   */
  function loading(body, text) {
    body.textContent = "";
    const skel = el("div", "fc-skel-wrap");
    skel.setAttribute("aria-busy", "true");
    skel.appendChild(el("div", "fc-skel fc-skel-img"));
    const lines = el("div", "fc-skel-lines");
    ["w-80", "w-60", "w-40", "w-70"].forEach((w) => lines.appendChild(el("div", "fc-skel fc-skel-line " + w)));
    skel.appendChild(lines);
    skel.appendChild(el("span", "fc-sr", text || "در حال بارگذاری…"));
    body.appendChild(skel);
  }

  // دور ۲۸ (#۷۷): عکس‌های کارت کالا تا بارگذاری، زمینهٔ اسکلتی متحرک دارند (کلاس fc-img-skel در قالب)؛
  // بعد از بارگذاری یا خطا زمینه برداشته می‌شود تا عکس‌های شفاف (دوربری‌شده) تمیز دیده شوند.
  function settleImage(img) {
    if (img.complete) { img.classList.add("is-loaded"); return; }
    img.addEventListener("load", () => img.classList.add("is-loaded"), { once: true });
    img.addEventListener("error", () => img.classList.add("is-loaded"), { once: true });
  }
  document.querySelectorAll("img.fc-img-skel").forEach(settleImage);

  /** فرم پنهان POST با توکن (برای افزودن به سبد که باید سمت سرور هدایت شود). */
  async function postForm(action, fields) {
    const form = el("form");
    form.method = "post";
    form.action = action;
    form.hidden = true;
    const all = Object.assign({ __RequestVerificationToken: await token(false) }, fields);
    Object.keys(all).forEach((name) => {
      const input = el("input");
      input.type = "hidden";
      input.name = name;
      input.value = all[name];
      form.appendChild(input);
    });
    document.body.appendChild(form);
    form.submit();
  }

  // ── نمایش سریع (پیشنهاد ۳۱) ───────────────────────────────────

  async function quickView(id, trigger) {
    const d = dialog("نمایش سریع");
    loading(d.body);
    const p = await getJson("/api/products/" + encodeURIComponent(id) + "/quick");
    if (!p) {
      d.body.textContent = "";
      d.body.appendChild(el("div", "fc-loading", "اطلاعات این کالا در دسترس نیست."));
      return;
    }
    d.body.textContent = "";
    const wrap = el("div", "fc-quick");

    // ستون عکس
    const media = el("div");
    const main = el("img", "fc-quick-main");
    const images = Array.isArray(p.images) && p.images.length ? p.images : [{ src: "", alt: p.name }];
    main.src = images[0].src;
    main.alt = images[0].alt || p.name;
    media.appendChild(main);
    if (images.length > 1) {
      const thumbs = el("div", "fc-thumbs");
      images.forEach((img, index) => {
        const b = el("button", "fc-thumb");
        b.type = "button";
        b.setAttribute("aria-label", "عکس " + (index + 1));
        b.setAttribute("aria-current", index === 0 ? "true" : "false");
        const t = el("img");
        t.src = img.src;
        t.alt = "";
        t.loading = "lazy";
        b.appendChild(t);
        b.addEventListener("click", () => {
          main.src = img.src;
          main.alt = img.alt || p.name;
          thumbs.querySelectorAll(".fc-thumb").forEach((x) => x.setAttribute("aria-current", x === b ? "true" : "false"));
        });
        thumbs.appendChild(b);
      });
      media.appendChild(thumbs);
    }

    // ستون اطلاعات
    const info = el("div");
    if (p.brand || p.category) info.appendChild(el("div", "fc-brand", [p.brand, p.category].filter(Boolean).join(" · ")));
    info.appendChild(el("h3", "fc-name", p.name));
    if (p.rating) info.appendChild(el("div", "fc-muted", "★ " + p.rating + (p.reviews ? " (" + p.reviews + " نظر)" : "")));
    const price = el("div", "fc-price", p.inStock ? p.price : "ناموجود");
    if (p.inStock && p.oldPrice) {
      price.appendChild(el("span", "fc-old", p.oldPrice));
      if (p.discount) price.appendChild(el("span", "fc-off", p.discount + "٪"));
    }
    info.appendChild(price);
    if (p.warranty) info.appendChild(el("div", "fc-muted", "گارانتی: " + p.warranty));
    if (Array.isArray(p.specs) && p.specs.length) {
      const dl = el("dl", "fc-specs");
      p.specs.forEach((s) => {
        const row = el("div");
        row.append(el("dt", null, s.key), el("dd", null, s.value));
        dl.appendChild(row);
      });
      info.appendChild(dl);
    }
    if (p.summary) info.appendChild(el("p", "fc-summary", p.summary));

    const actions = el("div", "fc-actions");
    const url = safePath(p.url);
    if (p.inStock) {
      const add = el("button", "fc-btn fc-btn-primary", "افزودن به سبد خرید");
      add.type = "button";
      add.addEventListener("click", () => {
        add.disabled = true;
        postForm("/cart?handler=Add", { productId: p.id, quantity: "1", returnUrl: location.pathname + location.search });
      });
      actions.appendChild(add);
    }
    const more = el("a", "fc-btn", "صفحهٔ کامل کالا");
    more.href = url;
    actions.appendChild(more);
    const cmp = el("button", "fc-btn", "مقایسه");
    cmp.type = "button";
    cmp.addEventListener("click", () => compareAdd(p.id, cmp));
    actions.appendChild(cmp);
    info.appendChild(actions);

    wrap.append(media, info);
    d.body.appendChild(wrap);
    if (trigger) trigger.blur();
  }

  // ── مقایسه و سینی (پیشنهاد ۳۲) ────────────────────────────────

  function compareCount() {
    const match = document.cookie.match(/(?:^|;\s*)fr_cmp=(\d+)/);
    return match ? parseInt(match[1], 10) : 0;
  }

  let tray = null;

  async function renderTray() {
    if (/^\/compare(\/|$)/i.test(location.pathname)) return;
    if (compareCount() <= 0) { if (tray) { tray.remove(); tray = null; } return; }
    const data = await getJson("/api/compare");
    const items = data && Array.isArray(data.items) ? data.items : [];
    if (tray) { tray.remove(); tray = null; }
    if (!items.length) return;
    tray = el("div", "fc-tray");
    tray.setAttribute("role", "region");
    tray.setAttribute("aria-label", "فهرست مقایسه");
    const list = el("div", "fc-tray-items");
    items.forEach((it) => {
      const a = el("a", "fc-tray-item");
      a.href = safePath(it.url);
      a.title = it.name;
      const img = el("img");
      img.src = it.image;
      img.alt = it.name;
      img.loading = "lazy";
      a.appendChild(img);
      list.appendChild(a);
    });
    const label = el("span", "fc-tray-label", items.length + " از " + (data.max || 4));
    const go = el("a", "fc-tray-go", items.length > 1 ? "مقایسه کنید" : "یک کالای دیگر انتخاب کنید");
    go.href = "/compare";
    const clear = el("button", "fc-tray-clear", "×");
    clear.type = "button";
    clear.setAttribute("aria-label", "خالی کردن فهرست مقایسه");
    clear.addEventListener("click", async () => {
      const r = await send("/api/compare", { clear: true });
      if (r.status === 200) { if (tray) { tray.remove(); tray = null; } toast(r.data.message, "info"); }
    });
    tray.append(list, label, go, clear);
    document.body.appendChild(tray);
  }

  async function compareAdd(id, button) {
    if (button) button.disabled = true;
    const r = await send("/api/compare", { productId: id });
    if (button) button.disabled = false;
    if (r.status === 200 && r.data) {
      toast(r.data.message, r.data.ok ? "success" : "warning");
      renderTray();
    } else {
      toast((r.data && r.data.error) || "افزودن به مقایسه انجام نشد؛ دوباره امتحان کنید.", "error");
    }
  }

  // ── ثبت کلیک نتیجهٔ جست‌وجو (پیشنهاد ۳۰) ──────────────────────

  function trackSearchClicks() {
    const grids = document.querySelectorAll("[data-search-q]");
    if (!grids.length) return;
    let any = false;
    grids.forEach((grid) => {
      const q = (grid.getAttribute("data-search-q") || "").trim();
      if (!q) return;
      any = true;
      const sent = new Set();
      grid.addEventListener("click", (e) => {
        const link = e.target.closest("a[href]");
        if (!link || !grid.contains(link)) return;
        const card = link.closest("[data-product-id]");
        if (!card) return;
        const productId = card.getAttribute("data-product-id");
        if (!productId || sent.has(productId)) return;
        sent.add(productId);
        // keepalive: درخواست حتی بعد از رفتن به صفحهٔ کالا کامل می‌شود. خطا مهم نیست (فقط آمار است).
        token(false).then((t) =>
          fetch("/api/search/click", {
            method: "POST",
            credentials: "same-origin",
            keepalive: true,
            headers: { "Content-Type": "application/json", RequestVerificationToken: t },
            body: JSON.stringify({ q: q.slice(0, 100), productId }),
          }).catch(() => null)
        );
      });
    });
    if (any) token(false); // توکن را از قبل بگیر تا کلیک معطل نشود.
  }

  // ── جست‌وجو با عکس (پیشنهاد ۲۹) ──────────────────────────────

  const MAX_IMAGE = 4 * 1024 * 1024;
  // دور ۲۸: عکس‌های دوربین گوشی معمولاً ۵ تا ۱۲ مگابایت‌اند؛ قبل از ارسال در خود مرورگر کوچک می‌شوند (ضلع بزرگ ۱۰۲۴ پیکسل،
  // JPEG با کیفیت ۰٫۸۸). هم سریع‌تر است و هم دیگر خطای «حجم زیاد» نمی‌دهد. اگر مرورگر نتوانست، همان فایل اصلی فرستاده می‌شود.
  const MAX_SIDE = 1024;

  /** کوچک کردن عکس با canvas. خروجی: Blob تازه یا null (اگر لازم نبود یا مرورگر پشتیبانی نکرد). */
  async function shrink(file) {
    try {
      if (typeof createImageBitmap !== "function") return null;
      const bitmap = await createImageBitmap(file);
      const scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
      if (scale >= 1 && file.size <= MAX_IMAGE && /jpe?g|png|webp/i.test(file.type || "")) {
        bitmap.close && bitmap.close();
        return null;
      }
      const canvas = document.createElement("canvas");
      canvas.width = Math.max(1, Math.round(bitmap.width * scale));
      canvas.height = Math.max(1, Math.round(bitmap.height * scale));
      const ctx = canvas.getContext("2d");
      // پس‌زمینهٔ سفید برای PNG شفاف (امضای کالاها هم روی زمینهٔ سفید ساخته شده).
      ctx.fillStyle = "#fff";
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
      bitmap.close && bitmap.close();
      return await new Promise((resolve) => canvas.toBlob((b) => resolve(b), "image/jpeg", 0.88));
    } catch (e) {
      return null;
    }
  }

  /** دور ۲۹ (#۱): متن کادر جست‌وجوی همان فرم (اگر مشتری چیزی نوشته) برای دقیق‌تر کردن نتیجه؛ حداکثر ۶۰ نویسه. */
  function searchHint(button) {
    const box = button && button.closest("form") ? button.closest("form").querySelector("input[name=q]") : null;
    return box ? String(box.value || "").trim().slice(0, 60) : "";
  }

  function pickImage(hint) {
    const input = el("input");
    input.type = "file";
    input.accept = "image/*";
    input.hidden = true;
    input.addEventListener("change", () => {
      const file = input.files && input.files[0];
      input.remove();
      if (file) imageSearch(file, hint || "");
    });
    document.body.appendChild(input);
    input.click();
  }

  async function imageSearch(file, hint) {
    const d = dialog("جست‌وجو با عکس");
    const small = await shrink(file);
    const upload = small || file;
    if (upload.size > MAX_IMAGE) {
      d.body.appendChild(el("p", "fc-error", "حجم عکس حداکثر ۴ مگابایت است."));
      return;
    }
    const head = el("div", "fc-image-head");
    const preview = el("img", "fc-preview");
    preview.alt = "عکس شما";
    const objectUrl = URL.createObjectURL(file);
    preview.src = objectUrl;
    preview.addEventListener("load", () => URL.revokeObjectURL(objectUrl), { once: true });
    const status = el("p", "fc-muted", "در حال پیدا کردن کالاهای شبیه…");
    head.append(preview, status);
    d.body.appendChild(head);

    const form = new FormData();
    form.append("image", upload, small ? "photo.jpg" : file.name || "image");
    if (hint) form.append("q", hint);
    const r = await send("/api/search/image", form);
    if (r.status !== 200 || !r.data) {
      status.className = "fc-error";
      status.textContent =
        (r.data && r.data.error) ||
        (r.status === 404 ? "جست‌وجو با عکس الان خاموش است." : r.status === 429 ? "چند لحظه صبر کنید و دوباره امتحان کنید." : "جست‌وجو انجام نشد؛ دوباره امتحان کنید.");
      return;
    }
    const products = Array.isArray(r.data.products) ? r.data.products : [];
    if (!products.length) {
      status.textContent = "کالای شبیهی پیدا نشد. می‌توانید نام کالا را جست‌وجو کنید یا از دستیار فروشگاه بپرسید.";
      return;
    }
    status.textContent = (r.data.approximate
      ? "کالای دقیقاً مشابهی پیدا نشد؛ شاید یکی از این‌ها همان باشد که می‌خواهید:"
      : "این کالاها بیشترین شباهت را به عکس شما دارند:") + (hint ? " (با توجه به «" + hint + "»)" : "");
    const grid = el("div", "fc-grid");
    products.forEach((p) => {
      const a = el("a", "fc-card");
      a.href = safePath(p.url);
      const img = el("img");
      img.src = p.image;
      img.alt = p.name;
      img.loading = "lazy";
      a.append(img, el("div", "fc-card-name", p.name), el("div", "fc-card-price", p.price));
      grid.appendChild(a);
    });
    d.body.appendChild(grid);
  }

  // ── جست‌وجوی صوتی (پیشنهاد ۲۳) – موتور تشخیص گفتار خود مرورگر ──

  // دور ۲۶: جست‌وجوی صوتی درست شد. علت خرابی: سرآیند Permissions-Policy میکروفون را برای خود سایت هم بسته بود («microphone=()»)
  // و مرورگر بدون هیچ پیامی رد می‌کرد. حالا: پیام «در حال شنیدن…»، پیام روشن برای هر خطا (اجازه، اینترنت، سکوت) و توقف خودکار بعد از ۱۰ ثانیه.
  function voiceError(code) {
    switch (code) {
      case "not-allowed":
      case "service-not-allowed":
        return window.isSecureContext === false
          ? "جست‌وجوی صوتی فقط روی نشانی امن (https) کار می‌کند."
          : "اجازهٔ میکروفون داده نشده است؛ از نماد قفل کنار نشانی سایت اجازه دهید.";
      case "network":
        return "سرویس تشخیص گفتار مرورگر در دسترس نیست؛ اتصال اینترنت را بررسی کنید یا تایپ کنید.";
      case "audio-capture":
        return "میکروفونی پیدا نشد.";
      case "no-speech":
        return "صدایی شنیده نشد؛ دوباره امتحان کنید.";
      case "language-not-supported":
        return "مرورگر شما گفتار فارسی را پشتیبانی نمی‌کند.";
      default:
        return "صدا تشخیص داده نشد؛ دوباره امتحان کنید.";
    }
  }

  function setupVoice() {
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    const buttons = document.querySelectorAll("[data-voice-search]");
    if (!Recognition || !buttons.length) return;
    buttons.forEach((button) => {
      button.classList.remove("hidden");
      button.classList.add("flex");
      let active = null;
      let timer = 0;
      button.addEventListener("click", (event) => {
        event.preventDefault(); // دکمه داخل فرم است؛ نباید فرم خالی ارسال شود
        if (active) { active.stop(); return; }
        const form = button.closest("form");
        const input = form && form.querySelector("input[name='q']");
        if (!input) return;
        let rec;
        try { rec = new Recognition(); } catch (e) { toast(voiceError(""), "warning"); return; }
        rec.lang = "fa-IR";
        rec.interimResults = true; // متن در حال گفتن داخل کادر دیده می‌شود
        rec.continuous = false;
        rec.maxAlternatives = 1;
        active = rec;
        let finalText = "";
        button.classList.add("fc-listening");
        button.setAttribute("aria-pressed", "true");
        const placeholder = input.getAttribute("placeholder") || "";
        input.setAttribute("placeholder", "در حال شنیدن… بگویید چه می‌خواهید");
        rec.onresult = (e) => {
          let interim = "";
          for (let i = e.resultIndex; i < e.results.length; i++) {
            const part = e.results[i][0] ? e.results[i][0].transcript : "";
            if (e.results[i].isFinal) finalText += part; else interim += part;
          }
          input.value = (finalText + interim).trim().slice(0, 100);
        };
        rec.onerror = (e) => {
          if (e.error !== "aborted") toast(voiceError(e.error), "warning");
        };
        rec.onend = () => {
          clearTimeout(timer);
          active = null;
          button.classList.remove("fc-listening");
          button.setAttribute("aria-pressed", "false");
          input.setAttribute("placeholder", placeholder);
          const text = (finalText || "").trim();
          if (text) {
            input.value = text.slice(0, 100);
            if (form.requestSubmit) form.requestSubmit(); else form.submit();
          }
        };
        try {
          rec.start();
          timer = setTimeout(() => { try { rec.stop(); } catch (e) { /* قبلاً تمام شده */ } }, 10000);
        } catch (e) {
          toast(voiceError(""), "warning");
          rec.onend();
        }
      });
    });
  }

  // ── آمار واقعی خرید (پیشنهاد ۱۵) ─────────────────────────────

  function setupSocialProof() {
    document.querySelectorAll("[data-social-proof]").forEach(async (node) => {
      const id = node.getAttribute("data-social-proof");
      if (!id) return;
      const d = await getJson("/api/products/" + encodeURIComponent(id) + "/proof");
      if (d && d.text) {
        node.textContent = d.text;
        node.classList.remove("hidden");
        node.classList.add("flex");
      }
    });
  }

  // ── تضمین قیمت (پیشنهاد ۱۱) ──────────────────────────────────

  function priceMatch(button) {
    const id = button.getAttribute("data-price-match");
    const ourPrice = parseInt(button.getAttribute("data-price") || "0", 10);
    const name = button.getAttribute("data-name") || "";
    const d = dialog("قیمت کمتر دیدید؟", true);
    d.body.appendChild(el("p", "fc-muted", "اگر «" + name + "» را با همین شرایط (نو، اصل و با گارانتی) در فروشگاه دیگری ارزان‌تر دیدید، پیوندش را بفرستید. بعد از بررسی، کد تخفیف ویژهٔ شما ارسال می‌شود."));

    const form = el("form");
    form.noValidate = true;
    const urlLabel = el("label", "fc-field", "پیوند صفحهٔ کالا در فروشگاه دیگر");
    const url = el("input");
    url.type = "url";
    url.dir = "ltr";
    url.required = true;
    url.maxLength = 500;
    url.placeholder = "https://";
    urlLabel.appendChild(url);
    const priceLabel = el("label", "fc-field", "قیمت آن فروشگاه (تومان)");
    const price = el("input");
    price.type = "text";
    price.inputMode = "numeric";
    price.dir = "ltr";
    price.required = true;
    price.maxLength = 15;
    price.placeholder = ourPrice ? groupDigits(Math.max(ourPrice - 1000, 0)) : "";
    priceLabel.appendChild(price);
    price.addEventListener("input", () => {
      const digits = toLatinDigits(price.value).replace(/\D/g, "");
      price.value = digits ? groupDigits(digits) : "";
    });
    const message = el("p", "fc-error");
    message.setAttribute("aria-live", "polite");
    const actions = el("div", "fc-actions");
    const submit = el("button", "fc-btn fc-btn-primary", "ثبت درخواست");
    submit.type = "submit";
    actions.appendChild(submit);
    form.append(urlLabel, priceLabel, message, actions);
    d.body.appendChild(form);
    url.focus();

    form.addEventListener("submit", async (e) => {
      e.preventDefault();
      message.className = "fc-error";
      message.textContent = "";
      const link = url.value.trim();
      const amount = parseInt(toLatinDigits(price.value), 10);
      if (!/^https?:\/\/[^\s/]+\.[^\s]+$/i.test(link)) { message.textContent = "پیوند کامل صفحهٔ کالا را وارد کنید (با https://)."; url.focus(); return; }
      if (!amount || amount <= 0) { message.textContent = "قیمت را به تومان وارد کنید."; price.focus(); return; }
      if (ourPrice && amount >= ourPrice) { message.textContent = "این قیمت از قیمت ما کمتر نیست."; price.focus(); return; }
      submit.disabled = true;
      const r = await send("/api/price-match", { productId: id, url: link, price: amount });
      submit.disabled = false;
      if (r.status === 401) {
        message.textContent = "";
        const login = el("a", "fc-btn fc-btn-primary", "ورود به حساب");
        login.href = "/login?returnUrl=" + encodeURIComponent(location.pathname + location.search);
        message.className = "fc-muted";
        message.append(el("span", null, "برای ثبت درخواست ابتدا وارد حساب شوید. "), login);
        return;
      }
      if (r.status === 200 && r.data) {
        if (r.data.ok) {
          form.remove();
          d.body.appendChild(el("p", "fc-ok", r.data.message));
          toast(r.data.message, "success");
        } else {
          message.textContent = r.data.message;
        }
        return;
      }
      message.textContent = (r.data && r.data.error) || (r.status === 429 ? "تعداد درخواست‌ها زیاد است؛ کمی بعد امتحان کنید." : "ثبت انجام نشد؛ دوباره امتحان کنید.");
    });
  }

  // ── اتصال رویدادها ───────────────────────────────────────────

  document.addEventListener("click", (e) => {
    const target = e.target.closest("[data-quick-view],[data-compare-add],[data-image-search],[data-price-match]");
    if (!target) return;
    if (target.hasAttribute("data-quick-view")) { e.preventDefault(); quickView(target.getAttribute("data-quick-view"), target); }
    else if (target.hasAttribute("data-compare-add")) { e.preventDefault(); compareAdd(target.getAttribute("data-compare-add"), target); }
    else if (target.hasAttribute("data-image-search")) { e.preventDefault(); pickImage(searchHint(target)); }
    else if (target.hasAttribute("data-price-match")) { e.preventDefault(); priceMatch(target); }
  });

  // دور ۲۹ (#۳): دکمهٔ «جست‌وجو» نوار پایین موبایل ← رفتن به بالای صفحه و فوکوس روی کادر جست‌وجوی بالا (بدون باز شدن صفحهٔ دیگر).
  document.addEventListener("click", (e) => {
    const link = e.target.closest("[data-focus-search]");
    if (!link) return;
    const input = document.querySelector("[data-msearch-input]");
    if (!input || input.offsetParent === null) return; // دسکتاپ یا کادر پنهان ← همان صفحهٔ /search
    e.preventDefault();
    window.scrollTo({ top: 0, behavior: "smooth" });
    input.focus({ preventScroll: true });
  });

  function init() {
    trackSearchClicks();
    setupVoice();
    setupSocialProof();
    renderTray();
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();

  // برای آزمون و استفادهٔ اسکریپت‌های دیگر (مثلاً دستیار).
  window.FratakCommerce = { quickView, compareAdd, renderTray, toLatinDigits, groupDigits, safePath };
})();
