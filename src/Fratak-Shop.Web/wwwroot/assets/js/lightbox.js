/*
 * ═══════════════════════════════════════════════════════════════════════════
 *  لایت‌باکس سراسری فراتک (مرحلهٔ دهم)
 * ═══════════════════════════════════════════════════════════════════════════
 *  هدف: کلیک روی هر تصویر در سایت و پنل مدیریت، تصویر را در یک پنجرهٔ بزرگ (مُدال) همین صفحه نشان دهد؛
 *  هیچ تصویری دیگر در تب جدید باز نمی‌شود.
 *
 *  چه چیزهایی باز می‌شوند؟
 *   ۱. هر عنصر با data-lightbox (لینک یا خود تصویر). مثال:
 *        <a href="/uploads/reviews/x.webp" data-lightbox="review-12"><img src="…"></a>
 *      مقدار data-lightbox «نام گروه» است: تصویرهای هم‌گروه با دکمهٔ قبلی/بعدی و کلیدهای جهت ورق می‌خورند.
 *   ۲. هر لینکی که به یک فایل تصویری می‌رود (پسوند jpg/png/webp/gif/avif/svg) و داخلش تصویر است
 *      (مثلاً تصویرهای لینک‌دار داخل متن بلاگ که ویرایشگر ساخته).
 *   ۳. تصویرهای داخل متن‌های محتوایی (.prose-content و [data-lightbox-scope]) که لینک نیستند.
 *
 *  چه چیزهایی باز نمی‌شوند؟ عنصرهای data-no-lightbox، تصویرهای خیلی کوچک (آیکون)، تصویرهای داخل لینک به صفحهٔ دیگر،
 *  و کلیک با Ctrl/Cmd/دکمهٔ وسط (رفتار عادی مرورگر برای کسی که عمداً می‌خواهد).
 *
 *  امکانات: ورق زدن (دکمه، کلیدهای ←/→، کشیدن انگشت روی موبایل)، بزرگ‌نمایی (کلیک/ضربه روی تصویر = ۲٫۵ برابر، چرخ ماوس و
 *  دکمه‌های +/− تا ۵ برابر، دو انگشت روی موبایل) و جابه‌جایی با حرکت ماوس یا انگشت، کلیدهای + و − و 0،
 *  Esc برای بستن، قفل اسکرول صفحه، بازگرداندن فوکوس، پیش‌بارگذاری تصویر بعدی، شمارنده و عنوان (alt).
 *
 *  دکمه‌های عملیات (دور بیستم): open(list, start, { actions: [...] }) دکمه‌هایی مثل «حذف این تصویر» یا «انتخاب» را بالای تصویر
 *  می‌گذارد. هر action: { label: "متن" یا تابع(item, index), danger: true/false, onClick(item, index, api) }.
 *  api: { remove(i) حذف از فهرست فعلی (آخری ← بسته شدن)، refresh() بازسازی برچسب‌ها، close() }.
 *  هر item می‌تواند data دلخواه داشته باشد (مثلاً عنصر کارت در صفحه). محل استفاده: admin-gallery.js (ویرایش کالا و پیشنهادهای هوشمند).
 *
 *  امنیت: هیچ متنی با innerHTML ساخته نمی‌شود (فقط textContent)، اسکریپت درون‌خطی ندارد (سازگار با CSP)
 *  و فقط آدرس‌های http/https/نسبی را به‌عنوان تصویر می‌پذیرد (javascript: و data: غیرتصویری رد می‌شوند).
 *  ظاهر با style خود عنصرها ساخته می‌شود تا به کلاس‌های Tailwind (که این پوشه را اسکن نمی‌کند) وابسته نباشد.
 *
 *  محل استفاده: در _Layout، _LayoutMinimal و _AdminLayout بارگذاری می‌شود.
 *  API عمومی: window.FratakLightbox.open([urls یا {url, caption, data}], index, { actions }) و close().
 */
(function () {
  "use strict";

  if (window.FratakLightbox) return;

  const IMAGE_EXT = /\.(jpe?g|png|webp|gif|avif|svg|bmp)$/i;
  const CONTENT_SCOPE = ".prose-content, [data-lightbox-scope]";
  const MIN_SIZE = 48; // تصویرهای کوچک‌تر از این (آیکون‌ها) باز نمی‌شوند

  const MAX_SCALE = 5;
  const CLICK_SCALE = 2.5;

  let root, imgEl, captionEl, counterEl, prevBtn, nextBtn, closeBtn, spinner, toolbar, zoomInBtn, zoomOutBtn;
  let items = [];
  let index = 0;
  let scale = 1;
  let actions = [];
  let pinchStart = null;
  let lastFocus = null;
  let touchStartX = null;
  let touchStartY = null;
  let previousOverflow = "";

  // ── تشخیص آدرس امن تصویر ───────────────────────────────────────────
  /** فقط http(s)، آدرس نسبی و data:image پذیرفته می‌شود. مثال: "/uploads/a.webp" ✔، "javascript:…" ✘ */
  function safeUrl(url) {
    if (!url) return null;
    const value = String(url).trim();
    if (/^data:image\//i.test(value)) return value;
    try {
      const parsed = new URL(value, window.location.href);
      return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.href : null;
    } catch (e) {
      return null;
    }
  }

  /** آیا این لینک مستقیم به یک فایل تصویری می‌رود؟ (پرس‌وجو و # نادیده گرفته می‌شوند) */
  function isImageHref(href) {
    try {
      const parsed = new URL(href, window.location.href);
      return IMAGE_EXT.test(parsed.pathname);
    } catch (e) {
      return false;
    }
  }

  /** تصویر بزرگ هر عنصر: href لینک، data-full، یا بزرگ‌ترین منبع تصویر. */
  function fullUrl(el) {
    if (el.tagName === "A") return safeUrl(el.getAttribute("data-full") || el.getAttribute("href"));
    if (el.tagName === "IMG") return safeUrl(el.getAttribute("data-full") || el.currentSrc || el.src);
    const inner = el.querySelector("img");
    return inner ? fullUrl(inner) : null;
  }

  function captionOf(el) {
    const img = el.tagName === "IMG" ? el : el.querySelector("img");
    return (el.getAttribute("data-caption") || (img && img.getAttribute("alt")) || el.getAttribute("title") || "").trim();
  }

  function isTiny(img) {
    if (!img) return false;
    const w = img.naturalWidth || img.width;
    const h = img.naturalHeight || img.height;
    return w > 0 && h > 0 && w < MIN_SIZE && h < MIN_SIZE;
  }

  /**
   * عنصر قابل‌بازشدن از روی هدف کلیک (یا null).
   * ترتیب: data-lightbox ← لینک مستقیم به تصویر ← تصویر بدون لینک داخل متن محتوایی.
   */
  function triggerFrom(target) {
    if (!(target instanceof Element)) return null;
    if (target.closest("[data-no-lightbox]")) return null;

    const marked = target.closest("[data-lightbox]");
    if (marked) return marked;

    const link = target.closest("a[href]");
    if (link) {
      if (link.querySelector("img") && isImageHref(link.getAttribute("href"))) return link;
      return null; // لینک به صفحهٔ دیگر: رفتار عادی
    }

    if (target.tagName === "IMG" && target.closest(CONTENT_SCOPE) && !target.closest("button, [data-ui], label") && !isTiny(target))
      return target;
    return null;
  }

  /** همهٔ عنصرهای هم‌گروه (برای قبلی/بعدی). */
  function groupOf(trigger) {
    const name = trigger.getAttribute("data-lightbox");
    let members;
    if (name) {
      members = Array.from(document.querySelectorAll("[data-lightbox]")).filter((el) => el.getAttribute("data-lightbox") === name);
    } else {
      const scope = trigger.closest(CONTENT_SCOPE) || trigger.parentElement?.parentElement || document.body;
      members = Array.from(scope.querySelectorAll("a[href], img, [data-lightbox]")).filter((el) => {
        if (el.tagName === "IMG" && el.closest("a[href], [data-lightbox]")) return false;
        return triggerFrom(el) === el && !el.getAttribute("data-lightbox");
      });
    }
    if (!members.includes(trigger)) members = [trigger];
    const list = [];
    let start = 0;
    members.forEach((el) => {
      const url = fullUrl(el);
      if (!url) return;
      if (el === trigger) start = list.length;
      list.push({ url, caption: captionOf(el) });
    });
    return { list, start };
  }

  // ── ساخت پنجره (فقط یک بار) ────────────────────────────────────────
  function button(label, path, css) {
    const b = document.createElement("button");
    b.type = "button";
    b.setAttribute("aria-label", label);
    b.title = label;
    Object.assign(b.style, {
      position: "absolute", width: "44px", height: "44px", borderRadius: "9999px", border: "0",
      background: "rgba(255,255,255,.14)", color: "#fff", cursor: "pointer", display: "flex",
      alignItems: "center", justifyContent: "center", backdropFilter: "blur(4px)", zIndex: "2",
    }, css);
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 256 256");
    svg.setAttribute("width", "22");
    svg.setAttribute("height", "22");
    svg.setAttribute("aria-hidden", "true");
    const p = document.createElementNS("http://www.w3.org/2000/svg", "path");
    p.setAttribute("d", path);
    p.setAttribute("fill", "currentColor");
    svg.appendChild(p);
    b.appendChild(svg);
    b.addEventListener("mouseenter", () => { b.style.background = "rgba(255,255,255,.28)"; });
    b.addEventListener("mouseleave", () => { b.style.background = "rgba(255,255,255,.14)"; });
    return b;
  }

  const ICON_CLOSE = "M205.66,194.34a8,8,0,0,1-11.32,11.32L128,139.31,61.66,205.66a8,8,0,0,1-11.32-11.32L116.69,128,50.34,61.66A8,8,0,0,1,61.66,50.34L128,116.69l66.34-66.35a8,8,0,0,1,11.32,11.32L139.31,128Z";
  const ICON_RIGHT = "M181.66,133.66l-80,80a8,8,0,0,1-11.32-11.32L164.69,128,90.34,53.66a8,8,0,0,1,11.32-11.32l80,80A8,8,0,0,1,181.66,133.66Z";
  const ICON_LEFT = "M165.66,202.34a8,8,0,0,1-11.32,11.32l-80-80a8,8,0,0,1,0-11.32l80-80a8,8,0,0,1,11.32,11.32L91.31,128Z";

  function build() {
    root = document.createElement("div");
    root.setAttribute("role", "dialog");
    root.setAttribute("aria-modal", "true");
    root.setAttribute("aria-label", "نمایش تصویر");
    root.hidden = true;
    Object.assign(root.style, {
      position: "fixed", inset: "0", zIndex: "2147483000", background: "rgba(10,14,25,.92)",
      // نمایش با display کنترل می‌شود (style درون‌خطی بر ویژگی hidden غلبه می‌کند).
      display: "none", alignItems: "center", justifyContent: "center", touchAction: "none",
      opacity: "0", transition: "opacity .18s ease", overscrollBehavior: "contain", overflow: "hidden",
    });

    imgEl = document.createElement("img");
    imgEl.alt = "";
    imgEl.decoding = "async";
    Object.assign(imgEl.style, {
      maxWidth: "92vw", maxHeight: "84vh", objectFit: "contain", borderRadius: "10px",
      boxShadow: "0 10px 40px rgba(0,0,0,.45)", cursor: "zoom-in", userSelect: "none",
      transition: "transform .2s ease", background: "rgba(255,255,255,.04)",
    });
    imgEl.draggable = false;

    spinner = document.createElement("div");
    Object.assign(spinner.style, {
      position: "absolute", width: "38px", height: "38px", borderRadius: "9999px",
      border: "3px solid rgba(255,255,255,.25)", borderTopColor: "#fff", animation: "fratak-lb-spin .8s linear infinite",
    });
    const keyframes = document.createElement("style");
    keyframes.textContent = "@keyframes fratak-lb-spin{to{transform:rotate(360deg)}}";
    document.head.appendChild(keyframes);

    captionEl = document.createElement("div");
    Object.assign(captionEl.style, {
      position: "absolute", bottom: "14px", left: "50%", transform: "translateX(-50%)", maxWidth: "86vw",
      color: "#e5e7eb", fontSize: "13px", textAlign: "center", lineHeight: "1.8", padding: "4px 12px",
      background: "rgba(0,0,0,.35)", borderRadius: "9999px", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis",
    });
    counterEl = document.createElement("div");
    Object.assign(counterEl.style, {
      position: "absolute", top: "18px", left: "50%", transform: "translateX(-50%)",
      color: "#e5e7eb", fontSize: "13px", direction: "ltr", fontVariantNumeric: "tabular-nums",
    });

    closeBtn = button("بستن (Esc)", ICON_CLOSE, { top: "12px", right: "12px" });
    // راست‌به‌چپ: «قبلی» سمت راست و «بعدی» سمت چپ است.
    prevBtn = button("تصویر قبلی", ICON_RIGHT, { right: "12px", top: "50%", marginTop: "-22px" });
    nextBtn = button("تصویر بعدی", ICON_LEFT, { left: "12px", top: "50%", marginTop: "-22px" });

    // نوار بالا-چپ: بزرگ‌نمایی/کوچک‌نمایی + دکمه‌های عملیات صفحه (مثلاً «حذف این تصویر»).
    toolbar = document.createElement("div");
    Object.assign(toolbar.style, {
      position: "absolute", top: "12px", left: "12px", display: "flex", gap: "8px", alignItems: "center",
      flexWrap: "wrap", maxWidth: "calc(100vw - 90px)", zIndex: "3", direction: "rtl",
    });
    zoomInBtn = button("بزرگ‌نمایی (+)", ICON_PLUS, { position: "static" });
    zoomOutBtn = button("کوچک‌نمایی (−)", ICON_MINUS, { position: "static" });
    toolbar.append(zoomInBtn, zoomOutBtn);

    root.append(spinner, imgEl, counterEl, captionEl, closeBtn, prevBtn, nextBtn, toolbar);
    document.body.appendChild(root);
    zoomInBtn.addEventListener("click", (e) => { e.stopPropagation(); setScale(scale * 1.5); });
    zoomOutBtn.addEventListener("click", (e) => { e.stopPropagation(); setScale(scale / 1.5); });
    root.addEventListener("wheel", (e) => {
      e.preventDefault();
      if (e.target === imgEl) pan(e);
      setScale(scale * (e.deltaY < 0 ? 1.2 : 1 / 1.2));
    }, { passive: false });

    closeBtn.addEventListener("click", close);
    prevBtn.addEventListener("click", (e) => { e.stopPropagation(); go(-1); });
    nextBtn.addEventListener("click", (e) => { e.stopPropagation(); go(1); });
    root.addEventListener("click", (e) => { if (e.target === root) close(); });
    imgEl.addEventListener("click", toggleZoom);
    imgEl.addEventListener("mousemove", pan);
    imgEl.addEventListener("load", () => { spinner.style.display = "none"; });
    imgEl.addEventListener("error", () => {
      spinner.style.display = "none";
      captionEl.textContent = "این تصویر بارگذاری نشد.";
      captionEl.style.display = "";
    });
    root.addEventListener("touchstart", (e) => {
      if (e.touches.length === 2) {
        // دو انگشت: شروع بزرگ‌نمایی.
        pinchStart = { dist: distance(e.touches), scale };
        touchStartX = null;
        return;
      }
      if (e.touches.length !== 1) return;
      touchStartX = e.touches[0].clientX;
      touchStartY = e.touches[0].clientY;
    }, { passive: true });
    root.addEventListener("touchmove", (e) => {
      if (pinchStart && e.touches.length === 2) {
        setScale(pinchStart.scale * distance(e.touches) / Math.max(1, pinchStart.dist));
        return;
      }
      if (scale > 1 && e.touches.length === 1) pan(e.touches[0]); // جابه‌جایی تصویر بزرگ‌شده با انگشت
    }, { passive: true });
    root.addEventListener("touchend", (e) => {
      if (pinchStart) { if (e.touches.length < 2) pinchStart = null; touchStartX = null; return; }
      if (touchStartX === null || scale > 1) { touchStartX = null; return; }
      const dx = e.changedTouches[0].clientX - touchStartX;
      const dy = e.changedTouches[0].clientY - touchStartY;
      touchStartX = null;
      if (Math.abs(dx) > 50 && Math.abs(dx) > Math.abs(dy)) go(dx > 0 ? 1 : -1); // RTL: کشیدن به راست = بعدی
      else if (dy > 90 && Math.abs(dy) > Math.abs(dx)) close(); // کشیدن به پایین = بستن
    });
    root.addEventListener("keydown", trapFocus);
  }

  const ICON_PLUS = "M224,128a8,8,0,0,1-8,8H136v80a8,8,0,0,1-16,0V136H40a8,8,0,0,1,0-16h80V40a8,8,0,0,1,16,0v80h80A8,8,0,0,1,224,128Z";
  const ICON_MINUS = "M224,128a8,8,0,0,1-8,8H40a8,8,0,0,1,0-16H216A8,8,0,0,1,224,128Z";

  function distance(touches) {
    const dx = touches[0].clientX - touches[1].clientX;
    const dy = touches[0].clientY - touches[1].clientY;
    return Math.sqrt(dx * dx + dy * dy);
  }

  // ── دکمه‌های عملیات صفحه ──────────────────────────────────────────────
  const api = {
    /** حذف تصویر i از فهرست فعلی؛ اگر چیزی نماند پنجره بسته می‌شود. */
    remove(i) {
      const at = typeof i === "number" ? i : index;
      if (at < 0 || at >= items.length) return;
      items.splice(at, 1);
      if (items.length === 0) { close(); return; }
      if (index >= items.length) index = items.length - 1;
      render();
    },
    /** بازسازی برچسب دکمه‌ها (مثلاً بعد از «انتخاب»/«لغو انتخاب»). */
    refresh() { renderActions(); },
    close() { close(); },
  };

  function renderActions() {
    toolbar.querySelectorAll("[data-lb-action]").forEach((b) => b.remove());
    const item = items[index];
    actions.forEach((action) => {
      const label = typeof action.label === "function" ? action.label(item, index) : action.label;
      if (!label) return;
      const b = document.createElement("button");
      b.type = "button";
      b.setAttribute("data-lb-action", "");
      b.textContent = String(label);
      Object.assign(b.style, {
        height: "40px", padding: "0 16px", borderRadius: "9999px", border: "0", cursor: "pointer", fontSize: "13px",
        fontFamily: "inherit", fontWeight: "700", color: "#fff",
        background: action.danger ? "rgba(220,38,38,.92)" : "rgba(0,105,253,.92)", boxShadow: "0 4px 14px rgba(0,0,0,.3)",
      });
      b.addEventListener("click", (e) => {
        e.stopPropagation();
        try { action.onClick && action.onClick(items[index], index, api); } catch (err) { /* خطای صفحه پنجره را خراب نکند */ }
        if (root && !root.hidden) renderActions();
      });
      toolbar.appendChild(b);
    });
  }

  // ── رفتار ──────────────────────────────────────────────────────────
  function render() {
    const item = items[index];
    resetZoom();
    renderActions();
    spinner.style.display = "";
    imgEl.removeAttribute("src");
    imgEl.src = item.url;
    imgEl.alt = item.caption || "تصویر";
    captionEl.textContent = item.caption;
    captionEl.style.display = item.caption ? "" : "none";
    const many = items.length > 1;
    counterEl.textContent = many ? (index + 1) + " / " + items.length : "";
    prevBtn.style.display = many ? "flex" : "none";
    nextBtn.style.display = many ? "flex" : "none";
    if (many) new Image().src = items[(index + 1) % items.length].url; // پیش‌بارگذاری بعدی
  }

  function go(step) {
    if (items.length < 2) return;
    index = (index + step + items.length) % items.length;
    render();
  }

  /** بزرگ‌نمایی بین ۱ و ۵ برابر. مثال: setScale(2.5). */
  function setScale(next) {
    scale = Math.min(MAX_SCALE, Math.max(1, next));
    if (scale < 1.05) scale = 1;
    imgEl.style.transform = scale > 1 ? "scale(" + scale.toFixed(3) + ")" : "";
    imgEl.style.cursor = scale > 1 ? "zoom-out" : "zoom-in";
    if (scale === 1) imgEl.style.transformOrigin = "50% 50%";
    zoomOutBtn.style.opacity = scale > 1 ? "1" : ".45";
    zoomInBtn.style.opacity = scale < MAX_SCALE ? "1" : ".45";
  }

  function toggleZoom(e) {
    if (e && typeof e.stopPropagation === "function") e.stopPropagation();
    const zoomIn = scale === 1;
    if (zoomIn && e) { scale = CLICK_SCALE; pan(e); }
    setScale(zoomIn ? CLICK_SCALE : 1);
  }

  function pan(e) {
    if (scale <= 1 || !e) return;
    const rect = imgEl.getBoundingClientRect();
    const x = ((e.clientX - rect.left) / rect.width) * 100;
    const y = ((e.clientY - rect.top) / rect.height) * 100;
    imgEl.style.transformOrigin = Math.min(100, Math.max(0, x)) + "% " + Math.min(100, Math.max(0, y)) + "%";
  }

  function resetZoom() {
    setScale(1);
  }

  function onKey(e) {
    if (root.hidden) return;
    if (e.key === "Escape") { e.preventDefault(); close(); }
    else if (e.key === "ArrowLeft") { e.preventDefault(); go(1); }   // RTL
    else if (e.key === "ArrowRight") { e.preventDefault(); go(-1); }
    else if (e.key === "+" || e.key === "=") { e.preventDefault(); setScale(scale * 1.5); }
    else if (e.key === "-") { e.preventDefault(); setScale(scale / 1.5); }
    else if (e.key === "0") { e.preventDefault(); setScale(1); }
  }

  function trapFocus(e) {
    if (e.key !== "Tab") return;
    const focusable = [closeBtn, prevBtn, nextBtn, zoomInBtn, zoomOutBtn].concat(Array.from(toolbar.querySelectorAll("[data-lb-action]")))
      .filter((b) => b.style.display !== "none");
    const i = focusable.indexOf(document.activeElement);
    e.preventDefault();
    const next = e.shiftKey ? (i <= 0 ? focusable.length - 1 : i - 1) : (i + 1) % focusable.length;
    focusable[next].focus();
  }

  /**
   * باز کردن پنجره. list: آدرس‌ها یا {url, caption, data}؛ start: شمارهٔ تصویر اول؛ options.actions: دکمه‌های عملیات.
   * مثال: open([{url:"/uploads/products/a.webp", data: card}], 0, { actions: [{ label: "حذف", danger: true, onClick: (it, i, api) => api.remove(i) }] })
   */
  function open(list, start, options) {
    const clean = (list || []).map((it) => (typeof it === "string"
      ? { url: safeUrl(it), caption: "" }
      : { url: safeUrl(it.url), caption: it.caption || "", data: it.data }))
      .filter((it) => it.url);
    if (clean.length === 0) return;
    if (!root) build();
    actions = options && Array.isArray(options.actions) ? options.actions.filter((a) => a && typeof a.onClick === "function") : [];
    items = clean;
    index = Math.min(Math.max(0, start || 0), items.length - 1);
    lastFocus = document.activeElement;
    previousOverflow = document.documentElement.style.overflow;
    document.documentElement.style.overflow = "hidden";
    root.hidden = false;
    root.style.display = "flex";
    render();
    requestAnimationFrame(() => { root.style.opacity = "1"; });
    document.addEventListener("keydown", onKey);
    closeBtn.focus({ preventScroll: true });
  }

  function close() {
    if (!root || root.hidden) return;
    root.style.opacity = "0";
    root.hidden = true;
    root.style.display = "none";
    imgEl.removeAttribute("src");
    actions = [];
    document.documentElement.style.overflow = previousOverflow;
    document.removeEventListener("keydown", onKey);
    if (lastFocus && typeof lastFocus.focus === "function") lastFocus.focus({ preventScroll: true });
  }

  // ── اتصال سراسری (یک شنوندهٔ کلیک برای همهٔ صفحه، حتی محتوای بعداً اضافه‌شده) ──
  document.addEventListener("click", (e) => {
    if (e.defaultPrevented || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
    const trigger = triggerFrom(e.target);
    if (!trigger) return;
    const { list, start } = groupOf(trigger);
    if (list.length === 0) return;
    e.preventDefault();
    open(list, start);
  });

  // صفحه‌کلید: Enter/Space روی تصویری که data-lightbox دارد و لینک نیست (با tabindex).
  document.addEventListener("keydown", (e) => {
    if (e.key !== "Enter" && e.key !== " ") return;
    const el = e.target;
    if (!(el instanceof Element) || el.tagName !== "IMG" || !el.hasAttribute("data-lightbox")) return;
    e.preventDefault();
    const { list, start } = groupOf(el);
    open(list, start);
  });

  // نشانگر «بزرگ‌نمایی» روی تصویرهای قابل‌بازشدن داخل متن‌ها.
  function markContentImages() {
    document.querySelectorAll(CONTENT_SCOPE).forEach((scope) => {
      scope.querySelectorAll("img").forEach((img) => {
        if (!img.closest("a[href], [data-no-lightbox], button")) img.style.cursor = "zoom-in";
      });
    });
    document.querySelectorAll("[data-lightbox]").forEach((el) => { el.style.cursor = "zoom-in"; });
  }
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", markContentImages);
  else markContentImages();

  window.FratakLightbox = { open, close };
})();
