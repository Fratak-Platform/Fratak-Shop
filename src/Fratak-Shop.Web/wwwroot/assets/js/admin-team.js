/*
 * admin-team.js – کار تیمی کارکنان در پنل (مرحلهٔ هفتم – فاز ۱۰)
 *
 *  پیشنهاد منشن: در هر <textarea data-mentions> یا <input data-mentions> (یادداشت مشتری/سفارش، توضیح کار) با تایپ «@» فهرست همکاران
 *  باز می‌شود؛ با کلیک یا Enter، «@نام_همکار » در متن قرار می‌گیرد. بعد از ذخیرهٔ یادداشت، سرور به همکار اعلان می‌دهد.
 *  فهرست همکاران یک بار از /admin/tasks?handler=staff گرفته می‌شود (۲ دقیقه در مرورگر کش).
 *  مثال: «@سا» ← پیشنهاد «سارا محمدی» ← متن: «@سارا_محمدی لطفاً تماس بگیر».
 */
(() => {
  "use strict";

  const fields = document.querySelectorAll("textarea[data-mentions], input[data-mentions]");
  if (!fields.length) return;

  let staffPromise = null;
  const loadStaff = () => {
    staffPromise ??= fetch("/admin/tasks?handler=staff", { credentials: "same-origin", headers: { Accept: "application/json" } })
      .then((r) => (r.ok ? r.json() : []))
      .catch(() => []);
    return staffPromise;
  };

  // همان یکسان‌سازی سمت سرور (Mentions.Normalize): ي/ك ← ی/ک، فاصله و نیم‌فاصله ← «_».
  const normalize = (s) => (s || "").replace(/[يى]/g, "ی").replace(/ك/g, "ک").replace(/[\s\u200c\u00a0-]+/g, "_").toLowerCase();

  // «@» + حروف تا محل نشانگر، به شرط اینکه قبل از @ حرف یا رقم نباشد (ایمیل منشن حساب نمی‌شود).
  const TOKEN = /(^|[^\p{L}\p{Nd}_])@([\p{L}\p{Mn}\p{Nd}_\u200c]{0,60})$/u;

  fields.forEach((field) => {
    const box = document.createElement("div");
    box.className = "a-mention-menu";
    box.setAttribute("role", "listbox");
    box.hidden = true;
    field.insertAdjacentElement("afterend", box);
    if (getComputedStyle(field.parentElement).position === "static") field.parentElement.style.position = "relative";

    let items = [];
    let active = 0;
    let tokenStart = -1;

    const close = () => { box.hidden = true; items = []; tokenStart = -1; };

    const render = () => {
      box.replaceChildren(...items.map((m, i) => {
        const option = document.createElement("button");
        option.type = "button";
        option.className = "a-mention-option" + (i === active ? " is-active" : "");
        option.setAttribute("role", "option");
        const name = document.createElement("b");
        name.textContent = m.name;
        const handle = document.createElement("span");
        handle.textContent = "@" + m.handle;
        option.append(name, handle);
        option.addEventListener("mousedown", (e) => { e.preventDefault(); pick(i); });
        return option;
      }));
      box.hidden = items.length === 0;
    };

    const pick = (i) => {
      const m = items[i];
      if (!m || tokenStart < 0) return;
      const caret = field.selectionStart;
      const before = field.value.slice(0, tokenStart);
      const after = field.value.slice(caret);
      const insert = "@" + m.handle + " ";
      field.value = before + insert + after;
      const pos = before.length + insert.length;
      field.setSelectionRange(pos, pos);
      field.dispatchEvent(new Event("input", { bubbles: true }));
      close();
      field.focus();
    };

    field.addEventListener("input", async () => {
      const caret = field.selectionStart;
      const match = TOKEN.exec(field.value.slice(0, caret));
      if (!match) { close(); return; }
      tokenStart = caret - match[2].length - 1;
      const query = normalize(match[2]);
      const staff = await loadStaff();
      items = staff.filter((m) => normalize(m.handle).includes(query) || normalize(m.name).includes(query)).slice(0, 8);
      active = 0;
      render();
    });

    field.addEventListener("keydown", (e) => {
      if (box.hidden) return;
      if (e.key === "ArrowDown") { e.preventDefault(); active = (active + 1) % items.length; render(); }
      else if (e.key === "ArrowUp") { e.preventDefault(); active = (active - 1 + items.length) % items.length; render(); }
      else if (e.key === "Enter" || e.key === "Tab") { e.preventDefault(); pick(active); }
      else if (e.key === "Escape") { close(); }
    });

    field.addEventListener("blur", () => setTimeout(close, 150));
  });
})();
