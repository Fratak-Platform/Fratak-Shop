/*
 * Invoice PDF, generated entirely in the browser with the vendored html2pdf bundle
 * (html2canvas + jsPDF). Nothing is uploaded; the browser shapes the Persian text,
 * so the PDF looks exactly like the printed page.
 */
(function () {
  "use strict";

  const button = document.querySelector("[data-invoice-pdf]");
  const invoice = document.getElementById("invoice");
  if (!button || !invoice) return;

  button.addEventListener("click", async () => {
    if (typeof window.html2pdf !== "function") {
      window.print();
      return;
    }

    const label = button.innerHTML;
    button.disabled = true;
    button.textContent = "در حال ساخت PDF...";

    try {
      if (document.fonts && document.fonts.ready) await document.fonts.ready;

      await window
        .html2pdf()
        .set({
          margin: [8, 8, 8, 8],
          filename: button.dataset.fileName || "invoice.pdf",
          image: { type: "jpeg", quality: 0.95 },
          html2canvas: { scale: 2, useCORS: true, backgroundColor: "#ffffff", logging: false },
          jsPDF: { unit: "mm", format: "a4", orientation: "portrait" },
          pagebreak: { mode: ["css", "legacy"], avoid: ["tr", ".break-inside-avoid"] },
        })
        .from(invoice)
        .save();
    } catch (error) {
      console.error(error);
      window.alert("ساخت فایل PDF انجام نشد؛ می‌توانید از گزینه چاپ و «ذخیره به صورت PDF» استفاده کنید.");
    } finally {
      button.disabled = false;
      button.innerHTML = label;
    }
  });
})();
