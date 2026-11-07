/*
  صفحه‌ساز کمپین (دور ۲۱) – GrapesJS 0.23 با رابط فارسی.
  صفحه: /admin/campaigns/edit/{id}  (Areas/Admin/Pages/Campaigns/Edit.cshtml)

  - بلوک‌ها را از ستون راست روی صفحه بکشید؛ روی هر متن دوبار کلیک = ویرایش؛ «ظاهر» = رنگ، اندازه، فاصله، گوشهٔ گرد، سایه.
  - پیش‌نمایش دسکتاپ / تبلت / موبایل؛ تغییر ظاهر در حالت تبلت/موبایل فقط همان اندازه را عوض می‌کند (media query) = واکنش‌گرای واقعی.
  - کالاها با کدهای کوتاه ([[deals:8]] …) می‌آیند و روی سایت با قیمت و موجودی امروز پر می‌شوند.
  - ذخیره: پروژه (برای ادامهٔ ویرایش) + HTML/CSS؛ سرور همه را پاک‌سازی می‌کند (بدون اسکریپت، فرم، iframe).
  - هیچ فایل یا آیکونی از اینترنت بار نمی‌شود (cssIcons خاموش؛ آیکون‌ها SVG داخلی).
*/
(function () {
  "use strict";
  const root = document.querySelector("[data-page-builder]");
  if (!root || !window.grapesjs) return;

  const $ = (s, el) => (el || root).querySelector(s);
  const $$ = (s, el) => Array.from((el || root).querySelectorAll(s));
  const csrf = () => (document.querySelector('meta[name="csrf-token"]') || {}).content || "";
  const base = root.dataset.base;
  const toast = (msg, type) => (window.FratakModal && window.FratakModal.toast ? window.FratakModal.toast(msg, type || "success") : console.log(msg));
  const confirmBox = (message, title) =>
    window.FratakModal && window.FratakModal.confirm ? window.FratakModal.confirm({ title: title || "تأیید", message, okText: "بله", cancelText: "انصراف" }) : Promise.resolve(true);

  // آیکون‌های ساده (SVG داخلی؛ بدون فونت‌آیکون بیرونی).
  const icon = (path) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">${path}</svg>`;
  const I = {
    section: icon('<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M7 10h10M7 14h6"/>'),
    hero: icon('<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M7 10h10M9 14h6"/>'),
    cols: icon('<rect x="3" y="5" width="8" height="14" rx="1.5"/><rect x="13" y="5" width="8" height="14" rx="1.5"/>'),
    heading: icon('<path d="M6 5v14M18 5v14M6 12h12"/>'),
    text: icon('<path d="M4 7h16M4 12h16M4 17h10"/>'),
    button: icon('<rect x="3" y="8" width="18" height="8" rx="4"/>'),
    image: icon('<rect x="3" y="5" width="18" height="14" rx="2"/><circle cx="9" cy="10" r="2"/><path d="M21 17l-5-5-8 7"/>'),
    timer: icon('<circle cx="12" cy="13" r="7"/><path d="M12 9v4l2 2M10 3h4"/>'),
    grid: icon('<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>'),
    star: icon('<path d="M12 3l2.6 5.6 6.1.7-4.5 4.2 1.2 6L12 16.6 6.6 19.5l1.2-6L3.3 9.3l6.1-.7z"/>'),
    faq: icon('<circle cx="12" cy="12" r="9"/><path d="M9.5 9a2.5 2.5 0 015 .5c0 1.5-2.5 2-2.5 3.5M12 17h.01"/>'),
    quote: icon('<path d="M7 7h4v4H8a3 3 0 003 3M14 7h4v4h-3a3 3 0 003 3"/>'),
    line: icon('<path d="M3 12h18"/>'),
    space: icon('<path d="M12 4v16M8 8l4-4 4 4M8 16l4 4 4-4"/>'),
    tag: icon('<path d="M3 12V4h8l10 10-8 8z"/><circle cx="7.5" cy="7.5" r="1.5"/>'),
  };

  const products = (code, title) =>
    `<section class="fs-lp-section"><div class="fs-lp-wrap"><h2 class="fs-lp-h2">${title}</h2><div class="fs-lp-products" data-fs-code="${code}">[[${code}]]</div></div></section>`;

  const blocks = [
    { id: "hero-sale", label: "سربرگ حراج", category: "بخش‌ها", media: I.hero, content: '<section class="fs-lp-hero fs-lp-hero--sale"><div class="fs-lp-wrap"><span class="fs-lp-kicker">کمپین ویژه</span><h1 class="fs-lp-title">حراج بزرگ</h1><p class="fs-lp-lead">تخفیف واقعی روی کالاهای منتخب.</p><a class="fs-lp-btn" href="/products?Discounted=true&amp;Sort=MostDiscounted">دیدن تخفیف‌ها</a></div></section>' },
    { id: "hero-launch", label: "سربرگ معرفی", category: "بخش‌ها", media: I.hero, content: '<section class="fs-lp-hero fs-lp-hero--launch"><div class="fs-lp-wrap"><span class="fs-lp-kicker">تازه رسید</span><h1 class="fs-lp-title">مجموعهٔ جدید</h1><p class="fs-lp-lead">یک جمله دربارهٔ مزیت اصلی برای خریدار.</p><a class="fs-lp-btn" href="/products?Sort=Newest">مشاهده</a></div></section>' },
    { id: "section", label: "بخش ساده", category: "بخش‌ها", media: I.section, content: '<section class="fs-lp-section"><div class="fs-lp-wrap"><h2 class="fs-lp-h2">عنوان بخش</h2><p class="fs-lp-text">متن این بخش را این‌جا بنویسید.</p></div></section>' },
    { id: "section-soft", label: "بخش رنگی", category: "بخش‌ها", media: I.section, content: '<section class="fs-lp-section fs-lp-section--soft"><div class="fs-lp-wrap"><h2 class="fs-lp-h2">عنوان بخش</h2><p class="fs-lp-text">متن این بخش.</p></div></section>' },
    { id: "cols", label: "متن + عکس", category: "بخش‌ها", media: I.cols, content: { tagName: "section", classes: ["fs-lp-section"], components: [{ tagName: "div", classes: ["fs-lp-wrap", "fs-lp-cols"], components: [
      { tagName: "div", components: '<h2 class="fs-lp-h2">چرا این کالا؟</h2><p class="fs-lp-text">مزیت‌ها را کوتاه و روشن بنویسید.</p><a class="fs-lp-btn fs-lp-btn--accent" href="/products">مشاهده</a>' },
      { type: "image", classes: ["fs-lp-image"], attributes: { alt: "" } },
    ] }] } },
    { id: "features", label: "سه مزیت", category: "بخش‌ها", media: I.star, content: '<section class="fs-lp-section"><div class="fs-lp-wrap fs-lp-features"><div class="fs-lp-feature"><b>ضمانت اصالت</b><p>همهٔ کالاها اصل.</p></div><div class="fs-lp-feature"><b>ارسال سریع</b><p>بسته‌بندی امن.</p></div><div class="fs-lp-feature"><b>پشتیبانی</b><p>پاسخگوی شما هستیم.</p></div></div></section>' },
    { id: "heading", label: "تیتر", category: "متن و دکمه", media: I.heading, content: '<h2 class="fs-lp-h2">تیتر تازه</h2>' },
    { id: "text", label: "متن", category: "متن و دکمه", media: I.text, content: '<p class="fs-lp-text">متن تازه را این‌جا بنویسید.</p>' },
    { id: "button", label: "دکمه", category: "متن و دکمه", media: I.button, content: '<a class="fs-lp-btn fs-lp-btn--accent" href="/products">خرید کنید</a>' },
    { id: "countdown", label: "شمارش معکوس", category: "متن و دکمه", media: I.timer, content: '<div class="fs-lp-countdown" data-fs-countdown="end"><div class="fs-lp-countdown__cell"><b data-u="d">۰</b><span>روز</span></div><div class="fs-lp-countdown__cell"><b data-u="h">۰</b><span>ساعت</span></div><div class="fs-lp-countdown__cell"><b data-u="m">۰</b><span>دقیقه</span></div><div class="fs-lp-countdown__cell"><b data-u="s">۰</b><span>ثانیه</span></div></div>' },
    { id: "faq", label: "پرسش و پاسخ", category: "متن و دکمه", media: I.faq, content: '<details class="fs-lp-faq"><summary>پرسش را بنویسید</summary><p>پاسخ کوتاه و روشن.</p></details>' },
    { id: "quote", label: "نظر مشتری", category: "متن و دکمه", media: I.quote, content: '<blockquote class="fs-lp-quote">«کیفیت عالی و ارسال سریع بود.»<cite>— نام مشتری</cite></blockquote>' },
    { id: "divider", label: "خط جداکننده", category: "متن و دکمه", media: I.line, content: '<hr class="fs-lp-divider">' },
    { id: "spacer", label: "فاصله", category: "متن و دکمه", media: I.space, content: '<div class="fs-lp-spacer"></div>' },
    { id: "image", label: "عکس", category: "رسانه", media: I.image, content: { type: "image", classes: ["fs-lp-image"], attributes: { alt: "" }, activate: true } },
    { id: "banner", label: "بنر لینک‌دار", category: "رسانه", media: I.image, content: { tagName: "a", classes: ["fs-lp-banner"], attributes: { href: "/products" }, components: [{ type: "image", attributes: { alt: "" } }] } },
    { id: "deals", label: "پرتخفیف‌ها", category: "کالاها", media: I.grid, content: products("deals:8", "پرتخفیف‌ترین‌ها") },
    { id: "newest", label: "تازه‌ترین‌ها", category: "کالاها", media: I.grid, content: products("newest:8", "تازه‌ترین کالاها") },
    { id: "best", label: "پرفروش‌ها", category: "کالاها", media: I.grid, content: products("bestsellers:8", "پرفروش‌ها") },
    { id: "category", label: "کالاهای یک دسته", category: "کالاها", media: I.grid, content: products("category:slug:8", "کالاهای دسته") },
    { id: "product", label: "یک کالا", category: "کالاها", media: I.tag, content: '<div class="fs-lp-products" data-fs-code="product">[[product:slug]]</div>' },
  ];

  const canvasCss = (root.dataset.canvasCss || "").split(",").filter(Boolean);
  const fa = window.FratakGrapesFa;

  const editor = window.grapesjs.init({
    container: $("[data-pb-editor]"),
    height: "100%",
    width: "auto",
    fromElement: false,
    storageManager: false,
    cssIcons: "",
    showOffsets: true,
    noticeOnUnload: false,
    i18n: fa ? { locale: "fa", detectLocale: false, localeFallback: "en", messagesAdd: { fa } } : undefined,
    panels: { defaults: [] },
    deviceManager: {
      devices: [
        { id: "desktop", name: "دسکتاپ", width: "" },
        { id: "tablet", name: "تبلت", width: "768px", widthMedia: "992px" },
        { id: "mobile", name: "موبایل", width: "375px", widthMedia: "480px" },
      ],
    },
    blockManager: { appendTo: $("[data-pb-blocks]"), blocks },
    selectorManager: { appendTo: $("[data-pb-selectors]"), componentFirst: true },
    traitManager: { appendTo: $("[data-pb-traits]") },
    layerManager: { appendTo: $("[data-pb-layers]") },
    styleManager: {
      appendTo: $("[data-pb-styles]"),
      sectors: [
        { name: "متن", open: true, buildProps: ["font-size", "font-weight", "color", "text-align", "line-height", "letter-spacing", "text-decoration", "text-shadow"] },
        { name: "پس‌زمینه و حاشیه", open: false, buildProps: ["background-color", "background", "border-radius", "border", "box-shadow", "opacity"] },
        { name: "اندازه و فاصله", open: false, buildProps: ["width", "max-width", "height", "min-height", "margin", "padding"] },
        { name: "چیدمان", open: false, buildProps: ["display", "flex-direction", "flex-wrap", "justify-content", "align-items", "position", "top", "right", "bottom", "left"] },
      ],
    },
    assetManager: {
      upload: `${base}?handler=Upload`,
      uploadName: "files",
      multiUpload: true,
      autoAdd: true,
      headers: { RequestVerificationToken: csrf() },
      embedAsBase64: false,
    },
    parser: { optionsHtml: { allowScripts: false, allowUnsafeAttr: false, allowUnsafeAttrValue: false } },
    canvas: { styles: canvasCss },
    protectedCss:
      "*{box-sizing:border-box}body{margin:0;direction:rtl;font-family:Vazirmatn,system-ui,sans-serif;color:#0f172a;background:#fff;--lp-accent:#0ea5e9;--lp-ink:#0f172a;--lp-soft:#f1f5f9}" +
      "img{max-width:100%;height:auto}" +
      ".fs-lp-products{display:flex;align-items:center;justify-content:center;min-height:120px;border:2px dashed #94a3b8;border-radius:16px;color:#475569;background:#f8fafc;font-weight:700;direction:ltr}",
  });

  // ───────── بار کردن صفحه ─────────
  // CSS ذخیره‌شده روی سرور محدود به «.fs-lp» شده («.fs-lp .x{}»)؛ در بوم ویرایشگر آن کلاس نیست، پس پیشوند برداشته می‌شود
  // (موقع ذخیره دوباره اضافه می‌شود). مثال: «.fs-lp .fs-lp-title{color:red}» ← «.fs-lp-title{color:red}»؛ «.fs-lp{…}» ← «body{…}».
  const unscope = (css) => css.replace(/\.fs-lp\s+(?=[^\s{,])/g, "").replace(/\.fs-lp(?=\s*[{,])/g, "body");

  let dirty = false;
  let ready = false;
  editor.on("load", async () => {
    try {
      const res = await fetch(`${base}?handler=Project`, { headers: { Accept: "application/json" } });
      const data = await res.json();
      let loaded = false;
      if (data.project) {
        try {
          editor.loadProjectData(JSON.parse(data.project));
          loaded = true;
        } catch (e) { /* پروژهٔ خراب ← از HTML */ }
      }
      if (!loaded) {
        editor.setComponents(data.html || "");
        editor.setStyle(unscope(data.css || ""));
      }
      editor.UndoManager.clear();
    } catch (e) {
      toast("صفحه بار نشد؛ دوباره باز کنید.", "error");
    }
    ready = true;
    editor.runCommand("core:component-outline");
    $('[data-pb-outline]').classList.add("is-active");
  });
  editor.on("update", () => { if (ready) dirty = true; });

  // ───────── نوار بالا ─────────
  $$("[data-pb-device]").forEach((b) => b.addEventListener("click", () => {
    editor.setDevice(b.dataset.pbDevice);
    $$("[data-pb-device]").forEach((x) => x.classList.toggle("is-active", x === b));
  }));
  $("[data-pb-undo]").addEventListener("click", () => editor.UndoManager.undo());
  $("[data-pb-redo]").addEventListener("click", () => editor.UndoManager.redo());
  $("[data-pb-outline]").addEventListener("click", (e) => {
    const on = editor.Commands.isActive("core:component-outline");
    on ? editor.stopCommand("core:component-outline") : editor.runCommand("core:component-outline");
    e.currentTarget.classList.toggle("is-active", !on);
  });
  $("[data-pb-template]").addEventListener("change", async (e) => {
    const key = e.target.value;
    e.target.value = "";
    if (!key || !(await confirmBox("محتوای فعلی صفحه با این قالب جایگزین شود؟ (با «برگرداندن» قابل بازگشت است)", "قالب"))) return;
    const res = await fetch(`${base}?handler=Template&key=${encodeURIComponent(key)}`, { headers: { Accept: "application/json" } });
    if (!res.ok) { toast("قالب دریافت نشد.", "error"); return; }
    const data = await res.json();
    editor.setComponents(data.html || "");
    editor.setStyle(data.css || "");
  });

  // ───────── زبانه‌های ستون چپ ─────────
  $$("[data-pb-tab]").forEach((b) => b.addEventListener("click", () => {
    $$("[data-pb-tab]").forEach((x) => x.classList.toggle("is-active", x === b));
    $$("[data-pb-pane]").forEach((p) => (p.hidden = p.dataset.pbPane !== b.dataset.pbTab));
  }));

  // ───────── ذخیره ─────────
  async function save() {
    const btn = $("[data-pb-save]");
    btn.disabled = true;
    try {
      const form = new FormData();
      $$("[data-pb-settings] input").forEach((input) => {
        form.append(input.name, input.type === "checkbox" ? (input.checked ? "true" : "false") : input.value.trim());
      });
      form.append("project", JSON.stringify(editor.getProjectData()));
      form.append("html", editor.getHtml());
      form.append("css", editor.getCss());
      const r = await fetch(`${base}?handler=Save`, { method: "POST", body: form, headers: { RequestVerificationToken: csrf(), Accept: "application/json" } });
      const data = await r.json().catch(() => ({}));
      if (!r.ok) { toast(data.error || "ذخیره نشد.", "error"); return; }
      dirty = false;
      if (data.slug) {
        $("#pb-slug").value = data.slug;
        $("[data-pb-view]").href = data.url;
      }
      toast(data.message || "ذخیره شد.");
    } catch (e) {
      toast("ذخیره نشد؛ اتصال را بررسی کنید.", "error");
    } finally {
      btn.disabled = false;
    }
  }
  $("[data-pb-save]").addEventListener("click", save);
  document.addEventListener("keydown", (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") { e.preventDefault(); save(); }
  });
  $$("[data-pb-settings] input").forEach((input) => input.addEventListener("change", () => (dirty = true)));
  window.addEventListener("beforeunload", (e) => {
    if (!dirty) return;
    e.preventDefault();
    e.returnValue = "";
  });
})();
