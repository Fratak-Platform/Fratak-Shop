/*
 * Fratak modal dialogs – replaces the browser's native alert() / confirm().
 *
 *   FratakModal.confirm({ title, message, okText, cancelText, danger }) -> Promise<boolean>
 *   FratakModal.alert({ title, message, okText, type })                 -> Promise<void>
 *   FratakModal.toast(message, type)                                     -> small auto-hiding notice
 *
 * Accessible (role="dialog", aria-modal, focus trap, Esc = cancel, focus is restored),
 * no innerHTML with caller text (XSS safe), works without any other script.
 */
(function () {
  "use strict";

  if (window.FratakModal) return;

  const ICONS = {
    // phosphor icons (256 viewBox)
    info: "M128,24A104,104,0,1,0,232,128,104.11,104.11,0,0,0,128,24Zm0,192a88,88,0,1,1,88-88A88.1,88.1,0,0,1,128,216Zm16-40a8,8,0,0,1-8,8,16,16,0,0,1-16-16V128a8,8,0,0,1,0-16,16,16,0,0,1,16,16v40A8,8,0,0,1,144,176ZM112,84a12,12,0,1,1,12,12A12,12,0,0,1,112,84Z",
    warning: "M236.8,188.09,149.35,36.22h0a24.76,24.76,0,0,0-42.7,0L19.2,188.09a23.51,23.51,0,0,0,0,23.72A24.35,24.35,0,0,0,40.55,224h174.9a24.35,24.35,0,0,0,21.33-12.19A23.51,23.51,0,0,0,236.8,188.09ZM222.93,203.8a8.5,8.5,0,0,1-7.48,4.2H40.55a8.5,8.5,0,0,1-7.48-4.2,7.59,7.59,0,0,1,0-7.72L120.52,44.21a8.75,8.75,0,0,1,15,0l87.45,151.87A7.59,7.59,0,0,1,222.93,203.8ZM120,144V104a8,8,0,0,1,16,0v40a8,8,0,0,1-16,0Zm20,36a12,12,0,1,1-12-12A12,12,0,0,1,140,180Z",
    success: "M173.66,98.34a8,8,0,0,1,0,11.32l-56,56a8,8,0,0,1-11.32,0l-24-24a8,8,0,0,1,11.32-11.32L112,148.69l50.34-50.35A8,8,0,0,1,173.66,98.34ZM232,128A104,104,0,1,1,128,24,104.11,104.11,0,0,1,232,128Zm-16,0a88,88,0,1,0-88,88A88.1,88.1,0,0,0,216,128Z",
  };

  const TONES = {
    info: { ring: "bg-sky-100", fill: "#0284c7", ok: "bg-sky-500 hover:bg-sky-600 focus-visible:ring-sky-300" },
    success: { ring: "bg-emerald-100", fill: "#059669", ok: "bg-emerald-500 hover:bg-emerald-600 focus-visible:ring-emerald-300" },
    warning: { ring: "bg-amber-100", fill: "#d97706", ok: "bg-sky-500 hover:bg-sky-600 focus-visible:ring-sky-300" },
    danger: { ring: "bg-red-100", fill: "#dc2626", ok: "bg-red-500 hover:bg-red-600 focus-visible:ring-red-300" },
  };

  const svgNs = "http://www.w3.org/2000/svg";

  function icon(type) {
    const svg = document.createElementNS(svgNs, "svg");
    svg.setAttribute("viewBox", "0 0 256 256");
    svg.setAttribute("width", "26");
    svg.setAttribute("height", "26");
    svg.setAttribute("aria-hidden", "true");
    const path = document.createElementNS(svgNs, "path");
    path.setAttribute("d", ICONS[type === "danger" ? "warning" : type] || ICONS.info);
    path.setAttribute("fill", (TONES[type] || TONES.info).fill);
    svg.appendChild(path);
    return svg;
  }

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  }

  let open = null; // one dialog at a time

  function show(options, withCancel) {
    if (open) open.close(false);

    const type = options.danger ? "danger" : options.type || (withCancel ? "warning" : "info");
    const tone = TONES[type] || TONES.info;
    const previousFocus = document.activeElement;

    return new Promise((resolve) => {
      const overlay = el("div", "fratak-modal fixed inset-0 z-[100] flex items-end sm:items-center justify-center bg-zinc-900/50 p-0 sm:p-4 opacity-0 transition-opacity duration-150");
      const dialog = el("div", "fratak-modal-dialog w-full sm:max-w-md rounded-t-2xl sm:rounded-2xl bg-white p-5 sm:p-6 shadow-2xl translate-y-4 sm:translate-y-0 sm:scale-95 transition-transform duration-150");
      dialog.setAttribute("role", withCancel ? "alertdialog" : "dialog");
      dialog.setAttribute("aria-modal", "true");
      dialog.dir = "rtl";

      const head = el("div", "flex items-start gap-x-3");
      const badge = el("span", `flex h-11 w-11 shrink-0 items-center justify-center rounded-full ${tone.ring}`);
      badge.appendChild(icon(type));
      const texts = el("div", "flex-1 pt-0.5");
      const titleId = `fratak-modal-title-${Date.now()}`;
      const title = el("h2", "text-base font-bold text-zinc-800", options.title || (withCancel ? "تأیید عملیات" : "پیام"));
      title.id = titleId;
      dialog.setAttribute("aria-labelledby", titleId);
      texts.appendChild(title);
      if (options.message) {
        const messageId = `${titleId}-text`;
        const message = el("p", "mt-2 text-sm leading-7 text-zinc-600 whitespace-pre-line", options.message);
        message.id = messageId;
        dialog.setAttribute("aria-describedby", messageId);
        texts.appendChild(message);
      }
      head.append(badge, texts);

      const actions = el("div", "mt-6 flex flex-col-reverse gap-2 sm:flex-row sm:justify-start");
      const ok = el("button", `inline-flex justify-center rounded-xl px-5 py-2.5 text-sm font-semibold text-white transition focus:outline-none focus-visible:ring-4 ${tone.ok}`,
        options.okText || (withCancel ? "تأیید" : "متوجه شدم"));
      ok.type = "button";
      actions.appendChild(ok);
      let cancel = null;
      if (withCancel) {
        cancel = el("button", "inline-flex justify-center rounded-xl border border-zinc-200 px-5 py-2.5 text-sm text-zinc-700 transition hover:bg-zinc-50 focus:outline-none focus-visible:ring-4 focus-visible:ring-zinc-200",
          options.cancelText || "انصراف");
        cancel.type = "button";
        actions.appendChild(cancel);
      }

      dialog.append(head, actions);
      overlay.appendChild(dialog);
      document.body.appendChild(overlay);
      const scrollLock = document.documentElement.style.overflow;
      document.documentElement.style.overflow = "hidden";

      const close = (result) => {
        if (!open || open.overlay !== overlay) return;
        open = null;
        document.removeEventListener("keydown", onKey, true);
        document.documentElement.style.overflow = scrollLock;
        overlay.classList.add("opacity-0");
        setTimeout(() => overlay.remove(), 160);
        if (previousFocus && typeof previousFocus.focus === "function") previousFocus.focus({ preventScroll: true });
        resolve(result);
      };

      const onKey = (event) => {
        if (event.key === "Escape") {
          event.preventDefault();
          close(false);
        } else if (event.key === "Tab") {
          // keep the focus inside the dialog
          const focusable = [ok, cancel].filter(Boolean);
          const index = focusable.indexOf(document.activeElement);
          event.preventDefault();
          const next = event.shiftKey ? index - 1 : index + 1;
          focusable[(next + focusable.length) % focusable.length].focus();
        }
      };

      ok.addEventListener("click", () => close(true));
      if (cancel) cancel.addEventListener("click", () => close(false));
      overlay.addEventListener("mousedown", (event) => {
        if (event.target === overlay) close(false);
      });
      document.addEventListener("keydown", onKey, true);

      open = { overlay, close };
      requestAnimationFrame(() => {
        overlay.classList.remove("opacity-0");
        dialog.classList.remove("translate-y-4", "sm:scale-95");
        (withCancel && options.danger ? cancel : ok).focus({ preventScroll: true });
      });
    });
  }

  let toastBox = null;

  function toast(message, type) {
    if (!toastBox) {
      toastBox = el("div", "fixed inset-x-0 bottom-20 md:bottom-6 z-[110] flex flex-col items-center gap-2 px-3 pointer-events-none");
      toastBox.setAttribute("aria-live", "polite");
      document.body.appendChild(toastBox);
    }
    const tone = type === "error" ? "bg-red-600" : type === "success" ? "bg-emerald-600" : "bg-zinc-800";
    const item = el("div", `pointer-events-auto rounded-xl px-4 py-2.5 text-sm text-white shadow-lg transition-opacity duration-200 ${tone}`, message);
    toastBox.appendChild(item);
    setTimeout(() => {
      item.classList.add("opacity-0");
      setTimeout(() => item.remove(), 250);
    }, 2600);
  }

  window.FratakModal = {
    confirm: (options) => show(typeof options === "string" ? { message: options } : options || {}, true),
    alert: (options) => show(typeof options === "string" ? { message: options } : options || {}, false).then(() => undefined),
    toast,
  };
})();
