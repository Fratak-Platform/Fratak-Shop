/*
 * ═══════════════════════════════════════════════════════════════════════════
 *  گالری تصویر پنل (دور بیستم): حذف دقیق هر تصویر در ویرایش کالا + انتخاب عکس‌های پیشنهادی هوشمند
 * ═══════════════════════════════════════════════════════════════════════════
 *  مشکل قبلی: حذف تصویر در ویرایش کالا به شمارهٔ ردیف فرم (Images[n]) وابسته بود؛ اگر ترتیب ردیف‌ها با تصویری که
 *  کاربر دیده یکی نبود (بازگردانی خودکار فرم مرورگر، ردیف جاافتاده …) تصویر دیگری (مثلاً آخری) حذف می‌شد.
 *  راه‌حل: حذف «بر اساس نام فایل» — هر کارت نام فایل خودش را دارد و با زدن «حذف» یک ورودی مخفی
 *  Input.RemovedImages=نام‌فایل ساخته می‌شود؛ سرور فقط همان نام را حذف می‌کند. پیش از ارسال هم شمارهٔ ردیف‌ها از نو
 *  (به ترتیب کارت‌ها) نوشته می‌شود تا فهرست ASP.NET هیچ‌وقت جای خالی نداشته باشد.
 *
 *  نشانه‌گذاری:
 *   [data-gallery="remove"][data-remove-name="Input.RemovedImages"][data-reindex="Input.Images"]  ← ظرف (ویرایش کالا)
 *   [data-gallery="select"]                                                                    ← ظرف (پیشنهادهای هوشمند)
 *     [data-gallery-item][data-name="x.webp"][data-full="/uploads/…"][data-caption="…"]          ← هر کارت
 *       img[data-gallery-open]        : کلیک ← لایت‌باکس با بزرگ‌نمایی و دکمهٔ «حذف این تصویر» / «انتخاب»
 *       [data-gallery-remove]         : دکمهٔ حذف/بازگردانی روی کارت (حالت remove)
 *       input[type=checkbox][data-gallery-check] : تیک انتخاب (حالت select)
 *   [data-gallery-all="ظرف"] / [data-gallery-none="ظرف"] : انتخاب همه / هیچ‌کدام (مقدار = id ظرف)
 *   [data-gallery-count="ظرف"] : شمارندهٔ انتخاب‌شده‌ها
 *
 *  حذف برگشت‌پذیر است تا «ذخیره» زده نشده (کارت کم‌رنگ می‌شود و دکمهٔ «بازگردانی» دارد) – پس پنجرهٔ تأیید لازم نیست.
 *  امنیت: هیچ innerHTML با دادهٔ کاربر ساخته نمی‌شود؛ سرور هم فقط نام‌هایی را می‌پذیرد که واقعاً مال همین کالا/کار هستند.
 *  محل بارگذاری: _AdminLayout (بعد از lightbox.js).
 */
(function () {
  "use strict";

  const $$ = (selector, root) => Array.from((root || document).querySelectorAll(selector));

  function items(gallery, includeRemoved) {
    return $$("[data-gallery-item]", gallery).filter((card) => includeRemoved || !card.classList.contains("is-removed"));
  }

  // ── حالت حذف (ویرایش کالا) ────────────────────────────────────────────
  function hiddenFor(gallery, name) {
    return $$("input[type=hidden][data-removed-input]", gallery).find((input) => input.value === name) || null;
  }

  /** علامت حذف/بازگردانی یک کارت. remove=true ← ورودی مخفی RemovedImages ساخته می‌شود. */
  function setRemoved(gallery, card, remove) {
    const name = card.getAttribute("data-name");
    if (!name) return;
    const field = gallery.getAttribute("data-remove-name") || "Input.RemovedImages";
    let hidden = hiddenFor(gallery, name);
    if (remove && !hidden) {
      hidden = document.createElement("input");
      hidden.type = "hidden";
      hidden.name = field;
      hidden.value = name;
      hidden.setAttribute("data-removed-input", "");
      gallery.appendChild(hidden);
    } else if (!remove && hidden) {
      hidden.remove();
    }
    card.classList.toggle("is-removed", remove);
    const button = card.querySelector("[data-gallery-remove]");
    if (button) {
      button.textContent = remove ? "بازگردانی" : "حذف";
      button.setAttribute("aria-pressed", remove ? "true" : "false");
      button.setAttribute("aria-label", remove ? "بازگردانی این تصویر" : "حذف این تصویر");
    }
    // تصویر حذف‌شده نمی‌تواند «اصلی» بماند: اولین تصویر باقی‌مانده اصلی می‌شود.
    const primary = card.querySelector("input[type=radio]");
    if (remove && primary && primary.checked) {
      const next = items(gallery).map((c) => c.querySelector("input[type=radio]")).find(Boolean);
      if (next) next.checked = true;
    }
    card.dispatchEvent(new Event("change", { bubbles: true }));
  }

  /** شمارهٔ ردیف‌های Input.Images[n] را به ترتیب کارت‌ها از ۰ پشت سر هم می‌نویسد (بدون جای خالی). */
  function reindex(gallery) {
    const prefix = gallery.getAttribute("data-reindex");
    if (!prefix) return;
    const pattern = new RegExp("^" + prefix.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + "\\[\\d+\\]");
    items(gallery, true).forEach((card, n) => {
      $$("input, select, textarea", card).forEach((el) => {
        if (el.name && pattern.test(el.name)) el.name = el.name.replace(pattern, prefix + "[" + n + "]");
      });
    });
  }

  // ── حالت انتخاب (پیشنهادهای هوشمند) ───────────────────────────────────
  function checkOf(card) {
    return card.querySelector("input[type=checkbox][data-gallery-check]");
  }

  function updateCount(gallery) {
    if (!gallery.id) return;
    const total = items(gallery).length;
    const chosen = items(gallery).filter((card) => checkOf(card) && checkOf(card).checked).length;
    $$("[data-gallery-count]").filter((el) => el.getAttribute("data-gallery-count") === gallery.id)
      .forEach((el) => { el.textContent = chosen.toLocaleString("fa-IR") + " از " + total.toLocaleString("fa-IR") + " انتخاب شده"; });
    items(gallery).forEach((card) => card.classList.toggle("is-selected", !!(checkOf(card) && checkOf(card).checked)));
  }

  function setChecked(card, value) {
    const box = checkOf(card);
    if (!box || box.checked === value) return;
    box.checked = value;
    box.dispatchEvent(new Event("change", { bubbles: true }));
  }

  // ── لایت‌باکس با دکمه‌های عملیات ──────────────────────────────────────
  function openGallery(gallery, card) {
    if (!window.FratakLightbox) return;
    const mode = gallery.getAttribute("data-gallery");
    const list = items(gallery).map((c) => ({
      url: c.getAttribute("data-full") || (c.querySelector("img") || {}).src,
      caption: c.getAttribute("data-caption") || "",
      data: c,
    }));
    const start = Math.max(0, list.findIndex((it) => it.data === card));
    const actions = mode === "remove"
      ? [{ label: "حذف این تصویر", danger: true, onClick: (item, i, api) => { setRemoved(gallery, item.data, true); api.remove(i); } }]
      : [
          {
            label: (item) => (checkOf(item.data) && checkOf(item.data).checked ? "✓ انتخاب شده (لغو)" : "انتخاب این تصویر"),
            onClick: (item, i, api) => { setChecked(item.data, !(checkOf(item.data) && checkOf(item.data).checked)); api.refresh(); },
          },
          {
            label: "رد این تصویر",
            danger: true,
            onClick: (item, i, api) => { setChecked(item.data, false); api.refresh(); },
          },
        ];
    window.FratakLightbox.open(list, start, { actions });
  }

  function init(gallery) {
    if (gallery.dataset.galleryReady) return;
    gallery.dataset.galleryReady = "1";
    const mode = gallery.getAttribute("data-gallery");

    gallery.addEventListener("click", (e) => {
      const target = e.target instanceof Element ? e.target : null;
      if (!target) return;
      const card = target.closest("[data-gallery-item]");
      if (!card || !gallery.contains(card)) return;
      if (target.closest("[data-gallery-remove]")) {
        e.preventDefault();
        setRemoved(gallery, card, !card.classList.contains("is-removed"));
        return;
      }
      if (target.closest("img[data-gallery-open]")) {
        e.preventDefault(); // لایت‌باکس عمومی (شنوندهٔ document) دوباره باز نکند
        if (card.classList.contains("is-removed")) return;
        openGallery(gallery, card);
      }
    });

    if (mode === "select") {
      gallery.addEventListener("change", (e) => { if (e.target instanceof Element && e.target.matches("[data-gallery-check]")) updateCount(gallery); });
      updateCount(gallery);
    }

    const form = gallery.closest("form");
    if (form && gallery.hasAttribute("data-reindex")) form.addEventListener("submit", () => reindex(gallery));
  }

  document.addEventListener("click", (e) => {
    const target = e.target instanceof Element ? e.target : null;
    if (!target) return;
    const all = target.closest("[data-gallery-all], [data-gallery-none]");
    if (!all) return;
    const id = all.getAttribute("data-gallery-all") || all.getAttribute("data-gallery-none");
    const gallery = id ? document.getElementById(id) : null;
    if (!gallery) return;
    e.preventDefault();
    const value = all.hasAttribute("data-gallery-all");
    items(gallery).forEach((card) => setChecked(card, value));
    updateCount(gallery);
  });

  function start() { $$("[data-gallery]").forEach(init); }
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start);
  else start();

  window.FratakGallery = { init, reindex, setRemoved };
})();
