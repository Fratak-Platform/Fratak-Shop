/*
 * صفحهٔ «بررسی تأمین‌کننده» در پنل (/admin/products/supplier-sync) – مرحلهٔ هفتم، فاز ۵.
 *
 * پیش‌نمایش اثر پیش از تأیید گروهی: با هر تغییر انتخاب یا عدد موجودی، خلاصه‌ای کنار نوار گروهی نوشته می‌شود؛ مثلاً
 *   «موجودی: ۳ کالا (جمع ‎+12 عدد) · قیمت: ۲ کالا (میانگین ‎+3.5٪) · ⚠ ۱ کالا زیان‌ده»
 * داده‌ها از data-* هر ردیف (tr[data-sync-row]) خوانده می‌شود: data-kind، data-old، data-new، data-loss
 * و ورودی موجودی ردیف (input[data-sync-qty]) اگر کارمند عدد را عوض کرده باشد.
 * اسکریپت درون‌خطی ندارد (سازگار با CSP) و فقط اگر [data-supplier-sync] در صفحه باشد کار می‌کند.
 */
(function () {
  "use strict";

  const root = document.querySelector("[data-supplier-sync]");
  if (!root) return;
  const preview = root.querySelector("[data-supplier-preview]");
  if (!preview) return;

  const STOCK_KINDS = ["OutOfStock", "BackInStock", "StockIncrease", "StockDecrease"];
  const fa = (n) => Number(n).toLocaleString("fa-IR", { maximumFractionDigits: 1 });
  const signed = (n) => (n > 0 ? "+" : n < 0 ? "−" : "") + fa(Math.abs(n));

  function refresh() {
    let stockCount = 0, stockDelta = 0, priceCount = 0, pctSum = 0, pctCount = 0, loss = 0, other = 0;
    root.querySelectorAll("tr[data-sync-row]").forEach((row) => {
      const box = row.querySelector("input[data-row-check]");
      if (!box || !box.checked) return;
      const kind = row.dataset.kind;
      const oldValue = parseFloat(row.dataset.old);
      let newValue = parseFloat(row.dataset.new);
      if (STOCK_KINDS.indexOf(kind) >= 0) {
        const qty = row.querySelector("input[data-sync-qty]");
        if (qty && qty.value !== "") newValue = parseFloat(qty.value);
        stockCount++;
        if (!isNaN(oldValue) && !isNaN(newValue)) stockDelta += newValue - oldValue;
      } else if (kind === "PriceChange") {
        priceCount++;
        if (!isNaN(oldValue) && oldValue > 0 && !isNaN(newValue)) { pctSum += (newValue - oldValue) * 100 / oldValue; pctCount++; }
        if (row.dataset.loss === "1") loss++;
      } else {
        other++;
      }
    });

    const parts = [];
    if (stockCount) parts.push("موجودی: " + fa(stockCount) + " کالا (جمع \u200E" + signed(stockDelta) + " عدد)");
    if (priceCount) parts.push("قیمت خرید: " + fa(priceCount) + " کالا" + (pctCount ? " (میانگین \u200E" + signed(pctSum / pctCount) + "٪)" : ""));
    if (loss) parts.push("⚠ " + fa(loss) + " کالا با قیمت تازه زیان‌ده");
    if (other) parts.push(fa(other) + " گزارش");
    preview.textContent = parts.join(" · ");
    preview.classList.toggle("text-red-600", loss > 0);
  }

  root.addEventListener("change", refresh);
  root.addEventListener("input", (e) => { if (e.target.matches && e.target.matches("input[data-sync-qty]")) refresh(); });

  // تغییر عدد موجودی یعنی کارمند این ردیف را می‌خواهد؛ خودکار انتخاب می‌شود.
  root.querySelectorAll("input[data-sync-qty]").forEach((input) => input.addEventListener("change", () => {
    const row = input.closest("tr[data-sync-row]");
    const box = row && row.querySelector("input[data-row-check]");
    if (box && !box.checked) { box.checked = true; box.dispatchEvent(new Event("change", { bubbles: true })); }
  }));

  refresh();
})();
