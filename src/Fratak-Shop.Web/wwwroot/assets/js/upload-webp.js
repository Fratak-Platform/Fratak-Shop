/*
 * upload-webp.js – دور نوزدهم (A28): تبدیل عکس به WebP در خود مرورگر، پیش از آپلود (فقط پنل مدیریت)
 *
 * چرا؟
 *  - تبدیل JPG/PNG بزرگ به WebP روی سرور حافظه و CPU زیادی می‌گیرد (عکس ۱۲ مگاپیکسلی ≈ ۴۸ مگابایت حافظه هنگام باز شدن).
 *    روی هاست اشتراکی ضعیف همین کار می‌تواند استخر برنامه را از سقف حافظه رد کند.
 *  - مرورگر کارمند همین کار را رایگان انجام می‌دهد؛ فایل کوچک‌تر هم سریع‌تر آپلود می‌شود.
 *  - سرور فایل WebP سالم (ضلع بزرگ‌تر ≤ ۲۵۶۰) را دوباره تبدیل نمی‌کند (WebPEncoder.Normalize)؛ ولی همیشه پاک‌سازی امنیتی می‌کند.
 *
 * چه می‌کند؟
 *  - روی هر <input type="file"> پنل که عکس می‌پذیرد (accept شامل image/ و نه application/pdf)، وقتی فایل انتخاب شد:
 *    هر JPG/PNG را روی canvas می‌کشد (ضلع بزرگ‌تر حداکثر ۲۵۶۰، هرگز بزرگ‌تر نمی‌شود) و با کیفیت ۰٫۸۲ به WebP تبدیل می‌کند.
 *  - اگر مرورگر WebP نسازد (Safari قدیمی) یا خروجی بزرگ‌تر از اصل شد، همان فایل اصلی می‌ماند (هیچ‌چیز خراب نمی‌شود).
 *  - GIF (ممکن است متحرک باشد) و WebP دست نمی‌خورند.
 *  - رویداد change اصلی را نگه می‌دارد و بعد از تبدیل دوباره می‌فرستد تا کدهای دیگر (پیش‌نمایش، آپلود گروهی) فایل نهایی را ببینند.
 *
 * غیرفعال کردن برای یک ورودی: data-no-webp
 * محل بارگذاری: _AdminLayout.cshtml (پیش از admin.js).
 */
(function () {
  "use strict";

  const MAX_SIDE = 2560;
  const QUALITY = 0.82;
  const DONE = "webpDone";

  function wantsImages(input) {
    if (!input || input.type !== "file" || input.hasAttribute("data-no-webp")) return false;
    const accept = (input.getAttribute("accept") || "").toLowerCase();
    return accept.indexOf("image/") >= 0 && accept.indexOf("application/pdf") < 0;
  }

  function convertible(file) {
    return file && (file.type === "image/jpeg" || file.type === "image/png") && file.size > 0;
  }

  function loadImage(file) {
    return new Promise(function (resolve, reject) {
      const url = URL.createObjectURL(file);
      const img = new Image();
      img.onload = function () { resolve({ img: img, url: url }); };
      img.onerror = function () { URL.revokeObjectURL(url); reject(new Error("decode")); };
      img.src = url;
    });
  }

  async function toWebp(file) {
    if (!convertible(file)) return file;
    let loaded;
    try {
      loaded = await loadImage(file);
    } catch (e) {
      return file;
    }
    try {
      const img = loaded.img;
      const longest = Math.max(img.naturalWidth, img.naturalHeight);
      if (!longest) return file;
      const scale = longest > MAX_SIDE ? MAX_SIDE / longest : 1;
      const width = Math.max(1, Math.round(img.naturalWidth * scale));
      const height = Math.max(1, Math.round(img.naturalHeight * scale));
      const canvas = document.createElement("canvas");
      canvas.width = width;
      canvas.height = height;
      const ctx = canvas.getContext("2d");
      if (!ctx) return file;
      ctx.imageSmoothingQuality = "high";
      ctx.drawImage(img, 0, 0, width, height);
      const blob = await new Promise(function (resolve) { canvas.toBlob(resolve, "image/webp", QUALITY); });
      // مرورگری که WebP نمی‌سازد PNG برمی‌گرداند؛ و اگر حجم بیشتر شد (عکس کوچک و بهینه) همان اصل بهتر است.
      if (!blob || blob.type !== "image/webp" || (scale === 1 && blob.size >= file.size)) return file;
      const name = file.name.replace(/\.(jpe?g|png)$/i, "") + ".webp";
      return new File([blob], name, { type: "image/webp", lastModified: Date.now() });
    } finally {
      URL.revokeObjectURL(loaded.url);
    }
  }

  document.addEventListener("change", async function (event) {
    const input = event.target;
    if (!wantsImages(input) || !input.files || input.files.length === 0) return;
    if (input.dataset[DONE] === "1") {
      delete input.dataset[DONE];
      return;
    }
    const files = Array.prototype.slice.call(input.files);
    if (!files.some(convertible) || typeof DataTransfer === "undefined") return;

    // کدهای دیگر فعلاً این change را نبینند؛ بعد از تبدیل دوباره فرستاده می‌شود.
    event.stopImmediatePropagation();
    input.setAttribute("aria-busy", "true");
    try {
      const converted = [];
      for (const file of files) converted.push(await toWebp(file));
      const transfer = new DataTransfer();
      converted.forEach(function (file) { transfer.items.add(file); });
      input.files = transfer.files;
    } catch (e) {
      // هر خطایی: فایل‌های اصلی می‌مانند.
    } finally {
      input.removeAttribute("aria-busy");
      input.dataset[DONE] = "1";
      input.dispatchEvent(new Event("change", { bubbles: true }));
    }
  }, true);
})();
