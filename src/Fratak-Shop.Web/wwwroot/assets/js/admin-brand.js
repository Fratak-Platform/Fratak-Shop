/*
 * استودیوی لوگو – پیش‌نمایش زنده (مرحلهٔ دوازدهم). صفحه: /admin/settings/brand (Areas/Admin/Pages/Settings/Brand.cshtml).
 *
 * کار این فایل:
 *   ۱) تب‌های محل‌ها (هدر، فوتر، ورود، پنل، فاکتور، ایمیل، …) را باز و بسته می‌کند و محل فعال را در فرم نگه می‌دارد
 *      تا پس از ذخیره به همان تب برگردیم.
 *   ۲) با هر تغییر فرم، مقدارهای نهایی هر محل را دقیقاً با همان فرمول سرور (Core/Services/Branding/BrandResolver.cs) حساب
 *      می‌کند و روی متغیرهای CSS لوگوی پیش‌نمایش (--bl-ih، --bl-fs، --bl-gap، --bl-pg …) می‌گذارد. HTML و CSS پیش‌نمایش
 *      همان لوگوی واقعی سایت است (BrandLockupHtml + brand-lockup.css)، پس چیزی که می‌بینید همان است که ذخیره می‌شود.
 *   ۳) جعبهٔ «موبایل» با مقدارهای موبایل و جعبهٔ «دسکتاپ» با مقدارهای دسکتاپ پر می‌شود (مستقل از عرض صفحهٔ خود مدیر).
 *   ۴) اسلایدرها با ورودی عددی هم‌گام‌اند؛ ورودی خالی = «خودکار» و مقدار خودکار در placeholder نوشته می‌شود.
 *   ۵) انتخابگر رنگ ↔ متن هگز، دکمهٔ پاک کردن (پیش‌فرض) و «بازگشت به پیش‌فرض این محل».
 *
 * مثال: ارتفاع آیکون هدر را ۶۰ کنید و اندازهٔ قلم را خالی بگذارید → قلم خودکار round(60 × 0.65) = 39 می‌شود و
 * فاصلهٔ آیکون تا متن max(6, 60/5) = 12؛ همین اعداد پس از ذخیره در سایت هم اعمال می‌شوند.
 *
 * امنیت: متن‌ها فقط با textContent نوشته می‌شوند (نه innerHTML)؛ آدرس تصویر فقط اگر با / یا http(s) شروع شود؛
 * رنگ فقط هگز معتبر. سرور هم دوباره همه را بررسی می‌کند (BrandStyleRules.Validate).
 */
(() => {
  "use strict";
  const form = document.querySelector("[data-brand-studio]");
  if (!form) return;

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => Array.from(root.querySelectorAll(selector));
  const D = form.dataset;

  // محدوده‌ها – همان BrandStyleRules (سرور).
  const R = { minIcon: 12, maxIcon: 200, minFont: 8, maxFont: 96, minTag: 6, maxTag: 48, maxGap: 80, maxOffset: 30, maxLs: 10 };

  const digits = (value) => String(value || "")
    .replace(/[۰-۹]/g, (d) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(d)))
    .replace(/[٠-٩]/g, (d) => String("٠١٢٣٤٥٦٧٨٩".indexOf(d)));
  const clamp = (v, min, max) => Math.min(max, Math.max(min, v));
  // Math.round برای عددهای مثبت همان MidpointRounding.AwayFromZero سرور است.
  const round = (v) => Math.round(v);
  const isHex = (v) => /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.test(v);
  const isAsset = (v) => /^\/(?!\/)/.test(v) || /^https?:\/\//i.test(v);
  const clip = (v, max) => {
    const t = String(v || "").trim().replace(/[<>{}]/g, "").replace(/[\u0000-\u001f]/g, "");
    return t ? t.slice(0, max) : null;
  };
  const intOf = (value) => {
    const t = digits(value).trim();
    if (!t || !/^[-+]?\d+$/.test(t)) return null;
    return parseInt(t, 10);
  };

  // ── مقدارهای پایه (کارت «هویت پایه») ───────────────────────────────
  const g = (key) => { const el = form.querySelector(`[data-g="${key}"]`); return el ? el.value.trim() : ""; };

  /** آیکون پیش‌فرض یک محل وقتی آیکون اختصاصی ندارد – همان زنجیرهٔ BrandResolver. */
  function fallbackIcon(info) {
    const custom = isAsset(g("g-logo")) ? g("g-logo") : null;
    if (info.raster) return custom || D.markPng;
    if (info.dark) return (isAsset(g("g-logo-dark")) ? g("g-logo-dark") : null) || custom || D.markDark;
    return custom || D.mark;
  }

  // ── محاسبهٔ یک محل (آینهٔ BrandResolver.Resolve) ───────────────────
  function compute(panel) {
    const info = JSON.parse(panel.dataset.blInfo);
    const field = (key) => panel.querySelector(`[data-f="${key}"]`);
    const val = (key) => { const el = field(key); return el ? el.value.trim() : ""; };
    const checked = (key, fallback) => { const el = field(key); return el ? el.checked : fallback; };
    const num = (key, min, max) => { const v = intOf(val(key)); return v === null ? null : clamp(v, min, max); };

    const visible = checked("visible", true);
    let showIcon = checked("showIcon", true);
    const showText = checked("showText", info.text);
    if (!showIcon && !showText) showIcon = true;

    const ih = num("ih", R.minIcon, R.maxIcon) ?? info.ih;
    const ihm = info.responsive ? (num("ihm", R.minIcon, R.maxIcon) ?? ih) : ih;
    const fsAuto = clamp(Math.max(12, round(ih * 0.65)), R.minFont, R.maxFont);
    const fs = num("fs", R.minFont, R.maxFont) ?? fsAuto;
    const fsmAuto = clamp(round(fs * ihm / ih), R.minFont, R.maxFont);
    const fsm = info.responsive ? (num("fsm", R.minFont, R.maxFont) ?? fsmAuto) : fs;
    const gapSet = num("gap", 0, R.maxGap);
    const pgSet = num("pg", 0, R.maxGap);
    const tsSet = num("ts", R.minTag, R.maxTag);
    const out = {
      info, visible, showIcon, showText,
      ih, ihm, fs, fsm,
      fw: parseInt(val("fw") || "800", 10) || 800,
      ls: (() => { const n = parseFloat(digits(val("ls")).replace("٫", ".").replace("/", ".")); return Number.isFinite(n) ? Math.round(clamp(n, -R.maxLs, R.maxLs) * 10) / 10 : 0; })(),
      gap: gapSet ?? clamp(Math.max(6, Math.floor(ih / 5)), 0, R.maxGap),
      gapm: gapSet ?? clamp(Math.max(6, Math.floor(ihm / 5)), 0, R.maxGap),
      pg: pgSet ?? clamp(round(fs * 0.28), 0, R.maxGap),
      pgm: pgSet ?? clamp(round(fsm * 0.28), 0, R.maxGap),
      ts: tsSet ?? clamp(Math.max(10, round(fs * 0.42)), R.minTag, R.maxTag),
      tsm: tsSet ?? clamp(Math.max(10, round(fsm * 0.42)), R.minTag, R.maxTag),
      tg: num("tg", 0, R.maxGap) ?? clamp(Math.max(2, round(fs * 0.15)), 0, R.maxGap),
      dy: num("dy", -R.maxOffset, R.maxOffset) ?? 0,
      layout: val("layout") || info.layout,
      align: val("align") || "Center",
    };
    // مقدارهای خودکار برای placeholder (اگر کاربر خالی بگذارد این‌ها اعمال می‌شوند).
    out.auto = { ih: info.ih, ihm: ih, fs: fsAuto, fsm: fsmAuto, gap: out.gap, pg: out.pg, ts: out.ts, tg: out.tg, dy: 0 };
    if (gapSet !== null) out.auto.gap = clamp(Math.max(6, Math.floor(ih / 5)), 0, R.maxGap);
    if (pgSet !== null) out.auto.pg = clamp(round(fs * 0.28), 0, R.maxGap);
    if (tsSet !== null) out.auto.ts = clamp(Math.max(10, round(fs * 0.42)), R.minTag, R.maxTag);
    if (num("tg", 0, R.maxGap) !== null) out.auto.tg = clamp(Math.max(2, round(fs * 0.15)), 0, R.maxGap);

    // متن‌ها
    out.text1 = clip(val("text1"), 40) || clip(g("text1"), 40) || D.text1;
    const accent = clip(val("text2"), 40) || clip(g("text2"), 40);
    out.text2 = accent === null ? D.text2 : accent === "-" ? null : accent;
    const tagline = clip(val("tagline"), 80) || clip(g("tagline"), 80) || clip(D.siteTagline, 80);
    out.showTagline = showText && !!tagline && checked("showTagline", info.tagline);
    out.tagline = tagline;

    // رنگ‌ها
    const c1 = val("color1"), c2 = val("color2"), c3 = val("taglineColor");
    out.c1 = isHex(c1) ? c1 : info.dark ? "#FFFFFF" : (isHex(g("color1")) ? g("color1") : D.navy);
    out.c2 = isHex(c2) ? c2 : (isHex(g("color2")) ? g("color2") : D.blue);
    out.c3 = isHex(c3) ? c3 : info.dark ? "#94A3B8" : (isHex(g("taglineColor")) ? g("taglineColor") : "#64748B");

    // آیکون
    const icon = val("iconUrl");
    out.icon = isAsset(icon) ? icon : fallbackIcon(info);
    return out;
  }

  /** نوشتن یک محاسبه روی یک لوگوی پیش‌نمایش. mobile = جعبهٔ موبایل (مقدارهای موبایل برای هر دو حالت). */
  function paint(lockup, v, mobile) {
    const px = (n) => `${n}px`;
    const ih = mobile ? v.ihm : v.ih, fs = mobile ? v.fsm : v.fs, gap = mobile ? v.gapm : v.gap;
    const pg = mobile ? v.pgm : v.pg, ts = mobile ? v.tsm : v.ts;
    const vars = {
      "--bl-ih": px(ih), "--bl-ihm": px(ih), "--bl-gap": px(gap), "--bl-gapm": px(gap),
      "--bl-fs": px(fs), "--bl-fsm": px(fs), "--bl-fw": String(v.fw), "--bl-ls": px(v.ls),
      "--bl-pg": px(pg), "--bl-pgm": px(pg), "--bl-tg": px(v.tg), "--bl-ts": px(ts), "--bl-tsm": px(ts),
      "--bl-dy": px(v.dy), "--bl-c1": v.c1, "--bl-c2": v.c2, "--bl-c3": v.c3,
    };
    Object.entries(vars).forEach(([name, value]) => lockup.style.setProperty(name, value));

    lockup.classList.remove("bl-row", "bl-row-rev", "bl-col", "bl-a-start", "bl-a-center", "bl-a-end");
    lockup.classList.add(v.layout === "IconEnd" ? "bl-row-rev" : v.layout === "IconTop" ? "bl-col" : "bl-row");
    lockup.classList.add(v.align === "Start" ? "bl-a-start" : v.align === "End" ? "bl-a-end" : "bl-a-center");
    // پیش‌نمایش لوگوی خاموش: کم‌رنگ ولی دیده می‌شود تا تنظیمش ممکن باشد.
    lockup.hidden = false;
    lockup.classList.toggle("is-off", !v.visible);

    const icons = $$(".bl-icon", lockup);
    icons.forEach((img, i) => {
      img.hidden = !v.showIcon || i > 0;
      if (i === 0 && img.getAttribute("src") !== v.icon) {
        img.src = v.icon;
        // اندازهٔ ثابت نشان پیش‌فرض برای تصویر دیگر معنی ندارد.
        img.removeAttribute("width");
      }
    });
    const textBox = $(".bl-text", lockup);
    if (textBox) {
      textBox.hidden = !v.showText;
      const p1 = $(".bl-p1", textBox), p2 = $(".bl-p2", textBox), tag = $(".bl-tag", textBox), name = $(".bl-name", textBox);
      if (p1) p1.textContent = v.text1;
      if (p2) { p2.textContent = v.text2 || ""; p2.hidden = !v.text2; }
      if (tag) { tag.textContent = v.tagline || ""; tag.hidden = !v.showTagline; }
      // کلاس‌های حالت شب سایت در پنل اثری ندارند ولی رنگ صریح باید بماند.
      if (name) name.classList.remove("brand-text-main");
      if (tag) tag.classList.remove("brand-tagline");
    }
  }

  function update(panel) {
    const v = compute(panel);
    $$("[data-bl-stage]", panel).forEach((stage) => {
      const lockup = $("[data-bl-preview]", stage);
      if (lockup) paint(lockup, v, stage.dataset.blStage === "mobile");
    });
    const note = $("[data-bl-hidden-note]", panel);
    if (note) note.hidden = v.visible;

    // placeholder ورودی‌های خالی = مقدار خودکار + جای اسلایدر
    $$("input[type=number][data-f]", panel).forEach((input) => {
      const key = input.dataset.f;
      const auto = v.auto[key];
      if (auto !== undefined) input.placeholder = `خودکار (${auto})`;
      const range = $(`[data-bl-range="#${CSS.escape(input.id)}"]`, panel);
      if (range) {
        const own = intOf(input.value);
        range.value = String(own ?? (auto ?? range.min));
        range.classList.toggle("is-auto", own === null);
      }
    });
  }

  const panels = $$("[data-bl-panel]");
  const updateAll = () => panels.forEach(update);

  // ── تب‌ها ─────────────────────────────────────────────────────────
  const activeInput = $("[data-active-place]", form);
  $$("[data-bl-tab]").forEach((tab) => tab.addEventListener("click", () => {
    const place = tab.dataset.blTab;
    $$("[data-bl-tab]").forEach((t) => {
      const on = t === tab;
      t.classList.toggle("is-active", on);
      t.setAttribute("aria-selected", on ? "true" : "false");
    });
    panels.forEach((p) => { p.hidden = p.dataset.blPanel !== place; });
    if (activeInput) activeInput.value = place;
    try { history.replaceState(null, "", `?place=${encodeURIComponent(place)}`); } catch { /* بدون history */ }
  }));

  // ── هم‌گام‌سازی کنترل‌ها ─────────────────────────────────────────
  form.addEventListener("input", (e) => {
    const t = e.target;
    if (t.matches("[data-bl-range]")) {
      const input = $(t.dataset.blRange, form);
      if (input) { input.value = t.value; input.dispatchEvent(new Event("input", { bubbles: true })); }
      return;
    }
    if (t.matches("[data-bl-swatch]")) {
      const input = $(t.dataset.blSwatch, form);
      if (input) { input.value = t.value.toUpperCase(); input.dispatchEvent(new Event("input", { bubbles: true })); }
      return;
    }
    if (t.matches("[data-bl-color]")) {
      const swatch = form.querySelector(`[data-bl-swatch="#${CSS.escape(t.id)}"]`);
      const value = t.value.trim();
      if (swatch) swatch.value = isHex(value) && value.length === 7 ? value : (t.dataset.fallback || "#000000");
      t.classList.toggle("input-validation-error", value !== "" && !isHex(value));
    }
    if (t.matches("[data-bl-image]")) thumb(t);
    const panel = t.closest("[data-bl-panel]");
    if (panel) update(panel); else updateAll();
  });
  form.addEventListener("change", (e) => {
    if (e.target.matches("[data-bl-image]")) thumb(e.target);
    const panel = e.target.closest("[data-bl-panel]");
    if (panel) update(panel); else updateAll();
  });

  /** بندانگشتی فیلد تصویر. فقط آدرس محلی یا http(s). */
  function thumb(input) {
    const box = form.querySelector(`[data-bl-thumb="${CSS.escape(input.id)}"]`);
    if (!box) return;
    const value = input.value.trim();
    box.textContent = "";
    if (isAsset(value)) {
      const img = document.createElement("img");
      img.alt = "";
      img.src = value;
      box.appendChild(img);
    }
  }

  // پاک کردن یک فیلد (= پیش‌فرض / خودکار)
  form.addEventListener("click", (e) => {
    const clear = e.target.closest("[data-bl-clear]");
    if (clear) {
      const input = $(clear.dataset.blClear, form);
      if (input) { input.value = ""; input.dispatchEvent(new Event("input", { bubbles: true })); input.focus(); }
      return;
    }
    const reset = e.target.closest("[data-bl-reset]");
    if (reset) {
      const panel = reset.closest("[data-bl-panel]");
      const info = JSON.parse(panel.dataset.blInfo);
      const run = () => {
        $$("input[data-f]:not([type=checkbox]), select[data-f]", panel).forEach((el) => {
          if (el.tagName === "SELECT") {
            const def = el.dataset.f === "fw" ? "800" : el.dataset.f === "layout" ? info.layout : "Center";
            el.value = def;
          } else {
            el.value = "";
          }
        });
        const set = (key, on) => { const el = panel.querySelector(`input[type=checkbox][data-f="${key}"]`); if (el) el.checked = on; };
        set("visible", true); set("showIcon", true); set("showText", info.text); set("showTagline", info.tagline);
        $$("[data-bl-color]", panel).forEach((el) => el.dispatchEvent(new Event("input", { bubbles: true })));
        $$("[data-bl-image]", panel).forEach(thumb);
        update(panel);
        form.dispatchEvent(new Event("input", { bubbles: true }));
      };
      if (window.FratakModal && window.FratakModal.confirm) {
        Promise.resolve(window.FratakModal.confirm("همهٔ تنظیمات این محل به پیش‌فرض برگردد؟ (تا ذخیره نکنید چیزی عوض نمی‌شود.)"))
          .then((ok) => { if (ok) run(); });
      } else {
        run();
      }
    }
  });

  updateAll();
})();
