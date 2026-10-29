/*
 * سرویس ایمیل – «اتصال سریع» (مرحلهٔ دوازدهم). صفحه: /admin/system/email (Areas/Admin/Pages/System/Email.cshtml).
 *
 * هر دکمهٔ [data-email-preset] یک سرویس آماده است (فهرست در Core/Services/Notifications/EmailProviderService.cs → EmailPresets).
 * با زدن آن: سرور، درگاه و رمزنگاری پر می‌شود، نام سرویس در فیلد پنهان Preset می‌رود، راهنمای همان سرویس نمایش داده می‌شود
 * و اگر نام کاربری خالی است، نمونهٔ آن در placeholder نوشته می‌شود. مدیر فقط نام کاربری، رمز و فرستنده را وارد می‌کند.
 *
 * مثال: سایت روی www.fratak.ir است و «ایمیل هاست» را می‌زنید → سرور = mail.fratak.ir، درگاه = 465، رمزنگاری = SSL/TLS.
 * اگر نشانی فرستنده خالی باشد و نام کاربری یک ایمیل باشد، همان نام کاربری پیشنهاد می‌شود.
 *
 * چیزی ذخیره نمی‌شود تا «ذخیره» زده شود (سرور همه را دوباره بررسی می‌کند).
 */
(() => {
  "use strict";
  const form = document.querySelector("[data-email-form]");
  if (!form) return;

  const field = (key) => form.querySelector(`[data-email-field="${key}"]`);
  const help = form.querySelector("[data-email-help]");
  const helpText = form.querySelector("[data-email-help-text]");
  const domain = (form.dataset.domain || "").replace(/^www\./i, "");
  const changed = (el) => el && el.dispatchEvent(new Event("input", { bubbles: true }));

  form.querySelectorAll("[data-email-preset]").forEach((button) => button.addEventListener("click", () => {
    const d = button.dataset;
    form.querySelectorAll("[data-email-preset]").forEach((b) => b.classList.toggle("is-active", b === button));
    const preset = field("preset");
    if (preset) { preset.value = d.emailPreset || ""; changed(preset); }

    if (d.host) {
      const host = field("host");
      // «mail.{domain}» → دامنهٔ همین سایت (بدون www). روی localhost مدیر باید خودش دامنه را بنویسد.
      const value = d.host.replace("{domain}", domain && !/^(localhost|\d+\.\d+\.\d+\.\d+)$/i.test(domain) ? domain : "your-domain.ir");
      if (host) { host.value = value; changed(host); }
      const port = field("port");
      if (port && d.port) { port.value = d.port; changed(port); }
      const security = field("security");
      if (security && d.security) { security.value = d.security; changed(security); }
    }
    const user = field("user");
    if (user && d.userHint) user.placeholder = d.userHint;

    if (help && helpText) {
      helpText.textContent = d.help || "";
      help.hidden = !d.help;
    }
    if (user && !user.readOnly) user.focus();
  }));

  // نام کاربری ایمیل است و فرستنده خالی؟ همان را پیشنهاد کن (بیشتر سرویس‌ها فرستندهٔ دیگری را نمی‌پذیرند).
  const user = field("user"), from = field("from");
  if (user && from) {
    user.addEventListener("change", () => {
      const value = user.value.trim();
      if (!from.value.trim() && /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(value)) { from.value = value; changed(from); }
    });
  }
})();
