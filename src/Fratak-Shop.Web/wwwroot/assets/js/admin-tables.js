/*
 * admin-tables.js – جدول‌های «مدیریت داده» پنل (مرحلهٔ هفتم – فاز ۳)
 * ------------------------------------------------------------------
 * این فایل فقط رفتار سمت مرورگر را اضافه می‌کند؛ همهٔ کارها بدون JavaScript هم با فرم‌های معمولی انجام‌شدنی است.
 *
 *  ۱) ویرایش درجا: خانه‌هایی که ویژگی data-inline دارند (فیلدهای ساده: متن، عدد، مبلغ، بله/خیر، فهرست).
 *     کلیک یا Enter روی خانه ← ورودی کوچک ← Enter یا خروج از ورودی = ذخیره، Esc = انصراف.
 *     ذخیره با POST به data-inline-url جدول (هندلر Inline صفحهٔ Manage/List) و جایگزینی محتوای خانه با HTML برگشتی سرور.
 *     مثال نشانه‌گذاری:
 *       <table data-inline-url="/admin/manage/faq-items?handler=Inline">
 *         <tr data-id="…"><td data-inline="DisplayOrder" data-kind="Int" data-value="3">۳</td></tr>
 *
 *  ۲) ترتیب دستی با کشیدن و رها کردن: tbody[data-reorder-url] و دستگیرهٔ [data-drag-handle] در هر ردیف.
 *     با Pointer Events نوشته شده تا روی موبایل (لمس) هم کار کند. بعد از رها کردن، شناسه‌ها به ترتیب جدید فرستاده می‌شوند.
 *
 *  ۳) فیلتر کارت‌ها: input[data-card-filter="selector"] کارت‌های منطبق را نگه می‌دارد (صفحهٔ «همه جدول‌ها»).
 *
 * وابستگی: modal.js (FratakModal.toast) و meta[name=csrf-token] در _AdminLayout.
 */
(function () {
  "use strict";

  const $ = (selector, root) => (root || document).querySelector(selector);
  const $$ = (selector, root) => Array.from((root || document).querySelectorAll(selector));
  const csrf = () => ($('meta[name="csrf-token"]') || {}).content || "";
  const toast = (message, type) => (window.FratakModal && window.FratakModal.toast ? window.FratakModal.toast(message, type) : null);

  /** یکدست‌سازی متن فارسی برای جست‌وجو (ی/ک عربی، ارقام فارسی). */
  const normalize = (text) => String(text || "")
    .replace(/[يى]/g, "ی").replace(/ك/g, "ک").replace(/\u200c/g, " ")
    .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 1776))
    .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 1632))
    .toLowerCase().trim();

  /** ارسال فرم با توکن ضد جعل و دریافت JSON ({ ok, message, ... }). آرایه‌ها چند بار با همان نام فرستاده می‌شوند. */
  function post(url, fields) {
    const body = new FormData();
    Object.entries(fields).forEach(([key, value]) => {
      (Array.isArray(value) ? value : [value]).forEach((v) => body.append(key, v == null ? "" : String(v)));
    });
    return fetch(url, {
      method: "POST",
      body,
      credentials: "same-origin",
      headers: { "X-Requested-With": "fetch", RequestVerificationToken: csrf(), Accept: "application/json" },
    }).then((response) => (response.ok ? response.json() : Promise.reject(new Error(String(response.status)))));
  }

  // ── ۱) ویرایش درجا ─────────────────────────────────────────────────────
  function optionsOf(cell) {
    // گزینه‌های فیلد فهرستی (enum) روی سرستون همان ستون است: data-options='[{"v":"Percent","l":"درصدی"}]'
    const table = cell.closest("table");
    const th = table && $(`thead th[data-field="${cell.dataset.inline}"]`, table);
    try { return th && th.dataset.options ? JSON.parse(th.dataset.options) : []; } catch { return []; }
  }

  function buildEditor(cell) {
    const kind = cell.dataset.kind;
    const value = cell.dataset.value || "";
    let input;
    if (kind === "Enum") {
      input = document.createElement("select");
      input.className = "a-input a-input-sm";
      if (!cell.dataset.required) input.add(new Option("—", ""));
      optionsOf(cell).forEach((o) => input.add(new Option(o.l, o.v, false, o.v === value)));
      input.value = value;
    } else {
      input = document.createElement("input");
      input.className = "a-input a-input-sm";
      input.value = value;
      if (kind === "Int") { input.inputMode = "numeric"; input.dir = "ltr"; }
      if (kind === "Decimal") { input.inputMode = "decimal"; input.dir = "ltr"; }
      if (kind === "Url" || kind === "Slug" || kind === "Color" || cell.dataset.ltr) input.dir = "ltr";
      if (cell.dataset.required) input.required = true;
    }
    input.setAttribute("aria-label", "مقدار جدید");
    return input;
  }

  function startEdit(cell) {
    if (cell.classList.contains("is-editing") || cell.classList.contains("is-saving")) return;
    const table = cell.closest("table");
    const url = table && table.dataset.inlineUrl;
    const row = cell.closest("tr[data-id]");
    if (!url || !row) return;

    // بله/خیر: بدون ورودی، با یک کلیک عوض می‌شود.
    if (cell.dataset.kind === "Bool") {
      save(cell, url, row.dataset.id, cell.dataset.value === "true" || cell.dataset.value === "True" ? "false" : "true");
      return;
    }

    const original = cell.innerHTML;
    const input = buildEditor(cell);
    cell.classList.add("is-editing");
    cell.innerHTML = "";
    cell.appendChild(input);
    input.focus();
    if (input.select) input.select();

    let done = false;
    const cancel = () => {
      if (done) return;
      done = true;
      cell.classList.remove("is-editing");
      cell.innerHTML = original;
      cell.focus();
    };
    const commit = () => {
      if (done) return;
      const next = input.value.trim();
      if (next === (cell.dataset.value || "")) { cancel(); return; }
      if (cell.dataset.required && next === "") { toast("این فیلد نمی‌تواند خالی باشد.", "error"); input.focus(); return; }
      done = true;
      cell.classList.remove("is-editing");
      cell.innerHTML = original;
      save(cell, url, row.dataset.id, next);
    };
    input.addEventListener("keydown", (e) => {
      if (e.key === "Enter") { e.preventDefault(); commit(); }
      else if (e.key === "Escape") { e.preventDefault(); cancel(); }
    });
    input.addEventListener("blur", () => setTimeout(commit, 0));
    if (input.tagName === "SELECT") input.addEventListener("change", commit);
  }

  function save(cell, url, id, value) {
    cell.classList.add("is-saving");
    post(url, { id, field: cell.dataset.inline, value })
      .then((data) => {
        if (!data.ok) { toast(data.message || "ذخیره نشد.", "error"); return; }
        cell.innerHTML = data.html;
        cell.dataset.value = data.value;
        cell.classList.add("is-saved");
        setTimeout(() => cell.classList.remove("is-saved"), 1200);
      })
      .catch(() => toast("ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.", "error"))
      .finally(() => cell.classList.remove("is-saving"));
  }

  document.addEventListener("click", (e) => {
    const cell = e.target.closest && e.target.closest("td[data-inline]");
    if (!cell || e.target.closest("a, button, input, select, textarea")) return;
    startEdit(cell);
  });
  document.addEventListener("keydown", (e) => {
    if (e.key !== "Enter" && e.key !== "F2") return;
    const cell = e.target.matches && e.target.matches("td[data-inline]") ? e.target : null;
    if (!cell) return;
    e.preventDefault();
    startEdit(cell);
  });

  // ── ۲) ترتیب دستی با کشیدن و رها کردن ─────────────────────────────────
  $$("tbody[data-reorder-url]").forEach((tbody) => {
    let dragging = null;
    let startOrder = "";

    const order = () => $$("tr[data-id]", tbody).map((tr) => tr.dataset.id);

    tbody.addEventListener("pointerdown", (e) => {
      const handle = e.target.closest("[data-drag-handle]");
      if (!handle || e.button > 0) return;
      e.preventDefault();
      dragging = handle.closest("tr");
      startOrder = order().join(",");
      dragging.classList.add("is-dragging");
      handle.setPointerCapture && handle.setPointerCapture(e.pointerId);
    });

    tbody.addEventListener("pointermove", (e) => {
      if (!dragging) return;
      // ردیفی که نشانگر روی آن است را پیدا و ردیف کشیده‌شده را قبل/بعدش می‌گذاریم.
      const rows = $$("tr[data-id]", tbody).filter((tr) => tr !== dragging);
      for (const tr of rows) {
        const box = tr.getBoundingClientRect();
        if (e.clientY >= box.top && e.clientY <= box.bottom) {
          const before = e.clientY < box.top + box.height / 2;
          tbody.insertBefore(dragging, before ? tr : tr.nextSibling);
          break;
        }
      }
    });

    const finish = () => {
      if (!dragging) return;
      dragging.classList.remove("is-dragging");
      const moved = dragging;
      dragging = null;
      const ids = order();
      if (ids.join(",") === startOrder) return;
      moved.classList.add("is-saving");
      post(tbody.dataset.reorderUrl, { ids })
        .then((data) => {
          toast(data.message || (data.ok ? "ترتیب ذخیره شد." : "ذخیره نشد."), data.ok ? "success" : "error");
          if (!data.ok) window.location.reload();
        })
        .catch(() => { toast("ذخیرهٔ ترتیب انجام نشد.", "error"); window.location.reload(); })
        .finally(() => moved.classList.remove("is-saving"));
    };
    tbody.addEventListener("pointerup", finish);
    tbody.addEventListener("pointercancel", finish);

    // صفحه‌کلید: دستگیره را انتخاب و با ↑/↓ جابه‌جا کنید (دسترس‌پذیری).
    tbody.addEventListener("keydown", (e) => {
      const handle = e.target.closest && e.target.closest("[data-drag-handle]");
      if (!handle || (e.key !== "ArrowUp" && e.key !== "ArrowDown")) return;
      e.preventDefault();
      const tr = handle.closest("tr");
      startOrder = order().join(",");
      const sibling = e.key === "ArrowUp" ? tr.previousElementSibling : tr.nextElementSibling;
      if (!sibling) return;
      tbody.insertBefore(tr, e.key === "ArrowUp" ? sibling : sibling.nextSibling);
      handle.focus();
      dragging = tr;
      finish();
    });
  });

  // ── ۳) فیلتر کارت‌ها ──────────────────────────────────────────────────
  $$("input[data-card-filter]").forEach((input) => {
    const items = $$(input.dataset.cardFilter);
    const empty = $("[data-card-empty]");
    input.addEventListener("input", () => {
      const terms = normalize(input.value).split(/\s+/).filter(Boolean);
      let visible = 0;
      items.forEach((item) => {
        const text = normalize(item.dataset.search || item.textContent);
        const show = terms.every((t) => text.includes(t));
        item.hidden = !show;
        if (show) visible++;
      });
      $$("[data-card-group]").forEach((group) => {
        group.hidden = !$$("[data-card-item]", group).some((item) => !item.hidden);
      });
      if (empty) empty.hidden = visible > 0;
    });
  });
})();
