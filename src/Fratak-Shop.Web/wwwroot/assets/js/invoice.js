/*
 * A4 invoice page:
 *  - scaled preview on narrow screens (the sheet itself always keeps its A4 layout)
 *  - print: the browser prints exactly the sheet (see @media print in invoice.css)
 *  - PDF: generated entirely in the browser with the vendored html2pdf bundle
 *    (html2canvas + jsPDF) from a clean full-size copy of the sheet – nothing is uploaded
 *  - ?print=1: used by the "quick print" button of the order page (hidden iframe); the page
 *    tells its parent when fonts and images are ready, or prints itself when opened directly.
 */
(function () {
  "use strict";

  const sheet = document.querySelector("[data-invoice-sheet]");
  const stage = document.querySelector("[data-invoice-stage]");
  if (!sheet || !stage) return;

  const A4_WIDTH = 794;

  // ── Ready: web font + images loaded ──────────────────────────────────
  const ready = (async () => {
    try {
      if (document.fonts && document.fonts.ready) await document.fonts.ready;
    } catch (_) { /* ignore */ }
    const images = Array.from(sheet.querySelectorAll("img"));
    await Promise.all(images.map((img) => (img.complete ? Promise.resolve() : new Promise((resolve) => {
      img.addEventListener("load", resolve, { once: true });
      img.addEventListener("error", resolve, { once: true });
    }))));
  })();

  // ── Scaled preview ───────────────────────────────────────────────────
  const fit = () => {
    const available = stage.clientWidth;
    if (available >= A4_WIDTH + 24) {
      stage.classList.remove("is-scaled");
      stage.style.height = "";
      sheet.style.removeProperty("--inv-scale");
      sheet.style.marginRight = "";
      return;
    }
    const scale = Math.max(0.3, (available - 16) / A4_WIDTH);
    stage.classList.add("is-scaled");
    sheet.style.setProperty("--inv-scale", scale.toFixed(4));
    sheet.style.marginRight = `${Math.max(0, (available - A4_WIDTH * scale) / 2)}px`;
    stage.style.height = `${Math.ceil(sheet.offsetHeight * scale) + 48}px`;
  };
  fit();
  window.addEventListener("resize", fit);
  ready.then(fit);

  // ── Print ────────────────────────────────────────────────────────────
  document.querySelectorAll("[data-invoice-print]").forEach((button) => {
    button.addEventListener("click", () => ready.then(() => window.print()));
  });

  // ── PDF ──────────────────────────────────────────────────────────────
  const pdfButton = document.querySelector("[data-invoice-pdf]");
  if (pdfButton) {
    pdfButton.addEventListener("click", async () => {
      if (typeof window.html2pdf !== "function") {
        window.print();
        return;
      }

      const label = pdfButton.innerHTML;
      pdfButton.disabled = true;
      pdfButton.textContent = "در حال ساخت PDF...";

      // A clean, unscaled copy: the PDF never depends on the screen size.
      const copy = sheet.cloneNode(true);
      copy.removeAttribute("style");
      copy.removeAttribute("id");
      copy.style.margin = "0";
      copy.style.boxShadow = "none";
      copy.style.transform = "none";
      copy.style.minHeight = "1100px"; // < one A4 page (1123px): no blank trailing page

      try {
        await ready;
        await window
          .html2pdf()
          .set({
            margin: 0,
            filename: pdfButton.dataset.fileName || "invoice.pdf",
            image: { type: "jpeg", quality: 0.98 },
            html2canvas: {
              scale: 2,
              useCORS: true,
              backgroundColor: "#ffffff",
              logging: false,
              windowWidth: A4_WIDTH,
              scrollX: 0,
              scrollY: 0,
            },
            jsPDF: { unit: "mm", format: "a4", orientation: "portrait", compress: true },
            pagebreak: { mode: ["css", "legacy"], avoid: ["tr", ".inv-bottom", ".inv-footer", ".inv-party"] },
          })
          .from(copy)
          .save();
      } catch (error) {
        console.error(error);
        const message = "ساخت فایل PDF انجام نشد؛ می‌توانید از دکمه «چاپ» و گزینه «ذخیره به صورت PDF» استفاده کنید.";
        if (window.FratakModal) window.FratakModal.alert({ title: "خطا در ساخت PDF", message, type: "danger" });
      } finally {
        pdfButton.disabled = false;
        pdfButton.innerHTML = label;
      }
    });
  }

  // ── Quick print (?print=1) ───────────────────────────────────────────
  if (document.body.dataset.autoPrint === "1") {
    ready.then(() => {
      if (window.parent && window.parent !== window) {
        window.parent.postMessage({ type: "fratak-invoice-ready" }, window.location.origin);
      } else {
        setTimeout(() => window.print(), 150);
      }
    });
  }
})();
