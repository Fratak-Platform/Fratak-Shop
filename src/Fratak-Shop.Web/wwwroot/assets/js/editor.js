/*
 * ویرایشگر دیداری متن (WYSIWYG) + انتخابگر کتابخانهٔ رسانه برای پنل مدیریت.
 * محل بارگذاری: _AdminLayout.cshtml. فقط وقتی صفحه textarea[data-html-editor] دارد،
 * فایل‌های Jodit (حدود ۶۶۰ کیلوبایت، MIT، بدون eval و سازگار با CSP) بارگذاری می‌شوند.
 *
 * کاربرد:
 *   <textarea name="Input.Content" data-html-editor rows="16"></textarea>
 *     → ویرایشگر با اندازه و رنگ قلم، تیتر، فهرست، جدول، لینک، تصویر (بارگذاری یا از کتابخانه)
 *       و دکمهٔ «کد» برای ویرایش مستقیم HTML. مقدار همیشه در همان textarea می‌ماند و با فرم ارسال می‌شود.
 *   <button type="button" data-media-pick="#banner-image">انتخاب از کتابخانه</button>
 *     → انتخابگر کتابخانه باز می‌شود و آدرس تصویر انتخاب‌شده در ورودی #banner-image نوشته می‌شود.
 *
 * امنیت: هرچه در ویرایشگر نوشته شود در سرور با HtmlSanitizer پاک‌سازی می‌شود (اسکریپت، on* و javascript: حذف می‌شود).
 * هیچ فایلی از CDN بارگذاری نمی‌شود (sourceEditor = area، beautifyHTML = false).
 */
(function () {
  "use strict";

  const $ = (selector, root) => (root || document).querySelector(selector);
  const $$ = (selector, root) => Array.from((root || document).querySelectorAll(selector));
  const csrf = () => ($('meta[name="csrf-token"]') || {}).content || "";
  const toast = (message, type) => (window.FratakModal && window.FratakModal.toast ? window.FratakModal.toast(message, type) : null);
  const VERSION = "4.18.10";
  const BASE = "/assets/js/dependencies/jodit/";

  const areas = $$("textarea[data-html-editor]");
  const pickButtons = $$("[data-media-pick]");
  if (!areas.length && !pickButtons.length) return;

  // ── بارگذاری تنبل فایل‌ها ───────────────────────────────────────────────
  function loadScript(src) {
    return new Promise((resolve, reject) => {
      const script = document.createElement("script");
      script.src = src;
      script.onload = resolve;
      script.onerror = () => reject(new Error(src));
      document.head.appendChild(script);
    });
  }

  function loadJodit() {
    if (window.Jodit) return Promise.resolve();
    const css = document.createElement("link");
    css.rel = "stylesheet";
    css.href = `${BASE}jodit.min.css?v=${VERSION}`;
    document.head.appendChild(css);
    return loadScript(`${BASE}jodit.min.js?v=${VERSION}`).then(() => loadScript(`${BASE}jodit.fa.js?v=${VERSION}`));
  }

  // ── انتخابگر کتابخانهٔ رسانه ─────────────────────────────────────────────
  let picker = null;
  let pickerCallback = null;
  let pickerPage = 1;
  let pickerQuery = "";

  function uploadFiles(files) {
    const body = new FormData();
    Array.from(files).forEach((file) => body.append("files", file));
    return fetch("/admin/media?handler=Upload", {
      method: "POST",
      body,
      credentials: "same-origin",
      headers: { "X-Requested-With": "fetch", RequestVerificationToken: csrf() },
    }).then((r) => r.json());
  }

  function buildPicker() {
    picker = document.createElement("div");
    picker.className = "a-palette a-media-picker";
    picker.hidden = true;
    picker.innerHTML =
      `<div class="a-palette-backdrop" data-picker-close></div>
       <div class="a-palette-box a-media-picker-box" role="dialog" aria-modal="true" aria-label="کتابخانهٔ رسانه">
         <div class="a-palette-head">
           <input type="search" class="a-palette-input" placeholder="جست‌وجو در کتابخانهٔ رسانه…" aria-label="جست‌وجو" maxlength="100">
           <label class="a-btn a-btn-sm">بارگذاری<input type="file" accept="image/jpeg,image/png,image/webp" multiple class="sr-only"></label>
           <button type="button" class="a-btn a-btn-sm a-btn-light" data-picker-close>بستن</button>
         </div>
         <div class="a-media-picker-grid" role="listbox"></div>
         <div class="a-palette-foot"><span data-picker-status></span><button type="button" class="a-btn a-btn-sm a-btn-light" data-picker-more hidden>بیشتر</button></div>
       </div>`;
    document.body.appendChild(picker);

    const input = $(".a-palette-input", picker);
    let timer = 0;
    input.addEventListener("input", () => {
      clearTimeout(timer);
      timer = setTimeout(() => { pickerQuery = input.value.trim(); loadPage(1); }, 250);
    });
    $("input[type=file]", picker).addEventListener("change", (e) => {
      const files = e.target.files;
      if (!files || !files.length) return;
      setStatus("در حال بارگذاری…");
      uploadFiles(files).then((result) => {
        e.target.value = "";
        if (!result.ok) { setStatus(""); return toast(result.message, "error"); }
        toast(result.message, "success");
        pickerQuery = "";
        input.value = "";
        loadPage(1);
      }).catch(() => { setStatus(""); toast("بارگذاری انجام نشد.", "error"); });
    });
    $("[data-picker-more]", picker).addEventListener("click", () => loadPage(pickerPage + 1, true));
    picker.addEventListener("click", (e) => {
      if (e.target.closest("[data-picker-close]")) return closePicker();
      const item = e.target.closest("[data-media-url]");
      if (item) {
        const callback = pickerCallback;
        closePicker();
        if (callback) callback({ url: item.dataset.mediaUrl, alt: item.dataset.mediaAlt || "" });
      }
    });
    picker.addEventListener("keydown", (e) => { if (e.key === "Escape") closePicker(); });
  }

  function setStatus(text) {
    const status = $("[data-picker-status]", picker);
    if (status) status.textContent = text;
  }

  function loadPage(page, append) {
    const grid = $(".a-media-picker-grid", picker);
    setStatus("در حال دریافت…");
    fetch(`/admin/media?handler=List&page=${page}&q=${encodeURIComponent(pickerQuery)}`, { credentials: "same-origin", headers: { Accept: "application/json" } })
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))))
      .then((data) => {
        pickerPage = data.page;
        if (!append) grid.replaceChildren();
        data.items.forEach((m) => {
          const button = document.createElement("button");
          button.type = "button";
          button.className = "a-media-picker-item";
          button.dataset.mediaUrl = m.url;
          button.dataset.mediaAlt = m.altText || "";
          button.title = m.altText || m.originalName || "";
          const img = document.createElement("img");
          img.src = m.url;
          img.alt = m.altText || "";
          img.loading = "lazy";
          button.appendChild(img);
          grid.appendChild(button);
        });
        if (!grid.children.length) {
          const empty = document.createElement("p");
          empty.className = "a-palette-empty";
          empty.textContent = pickerQuery ? "تصویری پیدا نشد." : "کتابخانه خالی است؛ با دکمهٔ «بارگذاری» تصویر اضافه کنید.";
          grid.appendChild(empty);
        }
        $("[data-picker-more]", picker).hidden = data.page >= data.pageCount;
        setStatus(`${data.total.toLocaleString("fa-IR")} تصویر`);
      })
      .catch((error) => setStatus(error.message === "302" || error.message === "403" ? "دسترسی به کتابخانهٔ رسانه ندارید." : "دریافت فهرست انجام نشد."));
  }

  /** باز کردن انتخابگر؛ callback با { url, alt } صدا زده می‌شود. */
  function openPicker(callback) {
    if (!picker) buildPicker();
    pickerCallback = callback;
    picker.hidden = false;
    pickerQuery = "";
    $(".a-palette-input", picker).value = "";
    loadPage(1);
    setTimeout(() => $(".a-palette-input", picker).focus(), 0);
  }

  function closePicker() {
    if (picker) picker.hidden = true;
    pickerCallback = null;
  }

  window.FratakMedia = { open: openPicker };

  pickButtons.forEach((button) => button.addEventListener("click", () => {
    const target = $(button.dataset.mediaPick);
    if (!target) return;
    openPicker((media) => {
      target.value = media.url;
      target.dispatchEvent(new Event("input", { bubbles: true }));
      target.dispatchEvent(new Event("change", { bubbles: true }));
    });
  }));

  if (!areas.length) return;

  // ── ساخت ویرایشگر ──────────────────────────────────────────────────────
  function make(area) {
    const rows = parseInt(area.getAttribute("rows") || "12", 10);
    const dark = document.documentElement.dataset.theme === "dark";
    const editor = window.Jodit.make(area, {
      language: "fa",
      direction: "rtl",
      theme: dark ? "dark" : "default",
      minHeight: Math.max(220, rows * 26),
      height: "auto",
      maxHeight: 900,
      toolbarSticky: true,
      toolbarStickyOffset: 56,
      // نوار ابزار روی موبایل چندخطی می‌شود (به‌جای پنهان کردن دکمه‌ها در منوی «…»).
      toolbarAdaptive: false,
      // بدون بارگذاری از CDN (CSP فقط 'self' را مجاز می‌داند).
      sourceEditor: "area",
      beautifyHTML: false,
      // متن چسبانده‌شده از Word یا سایت‌های دیگر بدون استایل‌های اضافه درج شود.
      askBeforePasteHTML: false,
      askBeforePasteFromWord: false,
      defaultActionOnPaste: "insert_clear_html",
      showCharsCounter: true,
      showWordsCounter: true,
      showXPathInStatusbar: false,
      disablePlugins: ["speech-recognize", "about", "ai-assistant", "powered-by-jodit", "print"],
      imageDefaultWidth: null,
      buttons: [
        "source", "|", "bold", "italic", "underline", "strikethrough", "|",
        "paragraph", "fontsize", "brush", "|",
        "ul", "ol", "align", "outdent", "indent", "|",
        "link", "image", "mediaLibrary", "table", "hr", "|",
        "undo", "redo", "eraser", "fullsize", "preview",
      ],
      controls: {
        mediaLibrary: {
          name: "mediaLibrary",
          icon: "image",
          tooltip: "تصویر از کتابخانهٔ رسانه",
          exec(jodit) {
            jodit.s.save();
            openPicker((media) => {
              jodit.s.restore();
              const img = jodit.createInside.element("img");
              img.setAttribute("src", media.url);
              img.setAttribute("alt", media.alt || "");
              img.setAttribute("loading", "lazy");
              jodit.s.insertImage(img, null, null);
            });
          },
        },
      },
      // بارگذاری مستقیم (کشیدن و رها کردن یا دکمهٔ تصویر) → همان کتابخانهٔ رسانه.
      uploader: {
        url: "/admin/media?handler=Upload",
        insertImageAsBase64URI: false,
        withCredentials: true,
        headers: { "X-Requested-With": "fetch", RequestVerificationToken: csrf() },
        filesVariableName: () => "files",
        isSuccess: (resp) => !!resp.ok,
        getMessage: (resp) => resp.message || "",
        process: (resp) => ({
          files: (resp.data || []).map((f) => f.url),
          isImages: (resp.data || []).map(() => true),
          baseurl: "",
          error: resp.ok ? 0 : 1,
          msg: resp.message || "",
        }),
      },
    });

    // اگر فرم بلافاصله بعد از تایپ ارسال شود، مقدار آخر حتماً در textarea باشد.
    if (area.form) area.form.addEventListener("submit", () => { area.value = editor.value; }, true);

    // admin-edit.js (پیش‌نویس خودکار و ادغام) به نمونهٔ ویرایشگر دسترسی دارد تا متن بازیابی‌شده را داخلش بگذارد.
    area.__fratakEditor = editor;
    // هر تغییر کاربر در textarea هم نوشته و رویداد input فرستاده می‌شود تا ذخیرهٔ خودکار و «تغییرات ذخیره‌نشده» باخبر شوند.
    // با کمی تأخیر وصل می‌شود تا یکدست‌سازی HTML هنگام ساخت ویرایشگر «تغییر کاربر» حساب نشود.
    setTimeout(() => {
      editor.events.on("change", () => {
        if (area.value === editor.value) return;
        area.value = editor.value;
        area.dispatchEvent(new Event("input", { bubbles: true }));
      });
    }, 400);
    return editor;
  }

  loadJodit()
    .then(() => areas.forEach((area) => {
      try { make(area); } catch (error) { console.error("Jodit init failed", error); }
    }))
    .catch(() => toast("ویرایشگر دیداری بارگذاری نشد؛ می‌توانید متن HTML را مستقیم بنویسید.", "error"));
})();
