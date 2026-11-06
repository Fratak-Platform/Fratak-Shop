/*
 * ابزارهای کاتالوگ پنل مدیریت (مرحلهٔ هفتم – فاز ۵) – بدون وابستگی و سازگار با CSP.
 * محل استفاده: صفحهٔ «تغییر گروهی قیمت» (/admin/products/bulk-price).
 *
 *  ۱) [data-bp-target] + [data-bp-mode]: گزینه‌های «نوع تغییر» بر اساس قیمت انتخاب‌شده فیلتر می‌شوند
 *     (هر <option> با data-targets="0,2" می‌گوید برای کدام قیمت‌ها معتبر است). سرور هم دوباره بررسی می‌کند.
 *  ۲) رادیوی محدوده: با «انتخاب‌شده‌ها»، فیلترها کم‌رنگ و غیرفعال می‌شوند (سرور هم آن‌ها را نادیده می‌گیرد).
 *  ۳) پیش‌نمایش: «پنهان کردن ردیف‌های بدون تغییر» و «فقط هشدارها».
 */
(function () {
  "use strict";

  const $ = (selector, root) => (root || document).querySelector(selector);
  const $$ = (selector, root) => Array.from((root || document).querySelectorAll(selector));

  // ── ۱) نوع تغییر بر اساس قیمت ───────────────────────────────────────────
  const target = $("[data-bp-target]");
  const mode = $("[data-bp-mode]");
  if (target && mode) {
    const syncModes = () => {
      const value = target.value;
      let firstValid = null;
      $$("option", mode).forEach((option) => {
        const valid = (option.dataset.targets || "").split(",").includes(value);
        option.hidden = !valid;
        option.disabled = !valid;
        // گزینه‌ای با data-default-for="1" برای آن قیمت پیش‌فرض است (مثلاً «درصد تخفیف» برای حراج).
        const preferred = (option.dataset.defaultFor || "").split(",").includes(value);
        if (valid && (firstValid === null || preferred)) firstValid = option;
      });
      const current = mode.options[mode.selectedIndex];
      if ((!current || current.disabled) && firstValid) {
        mode.value = firstValid.value;
        // نمایش/پنهان فیلد درصد/مبلغ (admin.js به change گوش می‌دهد).
        mode.dispatchEvent(new Event("change"));
      }
    };
    target.addEventListener("change", syncModes);
    syncModes();
  }

  // ── ۲) محدوده: انتخاب‌شده‌ها یا فیلتر ────────────────────────────────────
  const filters = $("[data-scope-filter]");
  const radios = $$('input[name="Form.ScopeKind"]');
  if (filters && radios.length) {
    const syncScope = () => {
      const selected = radios.some((r) => r.checked && r.value === "selected");
      filters.classList.toggle("opacity-50", selected);
      $$("select, input", filters).forEach((field) => { field.disabled = selected; });
    };
    radios.forEach((r) => r.addEventListener("change", syncScope));
    syncScope();
  }

  // ── ۳) فیلتر ردیف‌های پیش‌نمایش ──────────────────────────────────────────
  const preview = $("[data-bp-preview]");
  if (preview) {
    const hideSkipped = $("[data-bp-hide-skipped]", preview);
    const onlyWarnings = $("[data-bp-only-warnings]", preview);
    const syncRows = () => {
      $$("[data-bp-row]", preview).forEach((row) => {
        const skipped = row.dataset.skipped === "1";
        const warn = row.dataset.warn === "1";
        row.hidden = (hideSkipped && hideSkipped.checked && skipped) || (onlyWarnings && onlyWarnings.checked && !warn);
      });
    };
    [hideSkipped, onlyWarnings].forEach((box) => box && box.addEventListener("change", syncRows));
    syncRows();
    // بعد از پیش‌نمایش، صفحه به جدول نتیجه می‌رود.
    if (!location.hash) preview.scrollIntoView({ block: "start" });
  }
})();
