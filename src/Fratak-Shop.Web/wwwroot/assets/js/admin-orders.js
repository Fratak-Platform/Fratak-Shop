/*
 * رفتارهای صفحه‌های سفارش در پنل (بدون وابستگی؛ سازگار با CSP چون اسکریپت درون‌خطی ندارد).
 *
 *  - form[data-staff-order]        : فرم «ثبت سفارش برای مشتری» (/admin/orders/new)
 *        جست‌وجوی زندهٔ محصول، جدول اقلام با تعداد، جمع تخمینی و مبلغ به حروف.
 *        نام فیلدها همان چیزی است که سرور می‌خواند: input.Items[0].ProductId / input.Items[0].Quantity
 *  - select[data-province-select]  : پر کردن شهرها بعد از انتخاب استان (از /api/locations/provinces/{id}/cities)
 *  - [data-toggle-target="#id"]    : نمایش/پنهان کردن یک بخش (مثلاً فرم آدرس تازه)
 *  - [data-order-board]            : کانبان سفارش‌ها با کشیدن و رها کردن (Pointer Events؛ روی موبایل هم کار می‌کند)
 *  - [data-pick-product="url"]     : دور ۲۴ – انتخاب «یک» کالا از فهرست (مثلاً فرم «ایده ← طرح» استودیوی طراحی)؛
 *        شناسه در input[data-pick-product-id] و نام در [data-pick-product-label] می‌نشیند؛ دکمهٔ [data-pick-product-clear] پاکش می‌کند.
 *
 * هر بخش فقط وقتی اجرا می‌شود که عنصرش در صفحه باشد؛ پس این فایل در همهٔ صفحه‌های پنل بی‌خطر بارگذاری می‌شود.
 */
(function () {
  "use strict";

  const $ = (selector, root) => (root || document).querySelector(selector);
  const $$ = (selector, root) => Array.from((root || document).querySelectorAll(selector));
  const toast = (message, type) => (window.FratakModal && window.FratakModal.toast ? window.FratakModal.toast(message, type) : null);

  /** ۱۲۳۴۵۶۷ → «1,234,567» (همان قالب Format.Price سمت سرور). */
  const money = (value) => Math.round(Number(value) || 0).toLocaleString("en-US");

  /** مبلغ به حروف با همان تابع fa-inputs.js؛ اگر بارگذاری نشده بود، خالی. */
  const words = (value) => (window.FratakFa && window.FratakFa.toWords ? window.FratakFa.toWords(value) + " تومان" : "");

  /** ساخت امن عنصر بدون innerHTML (متن‌ها با textContent درج می‌شوند تا XSS ممکن نباشد). */
  function el(tag, attrs, children) {
    const node = document.createElement(tag);
    Object.entries(attrs || {}).forEach(([key, value]) => {
      if (value === null || value === undefined || value === false) return;
      if (key === "text") node.textContent = value;
      else if (key === "className") node.className = value;
      else node.setAttribute(key, value === true ? "" : String(value));
    });
    (children || []).forEach((child) => child && node.appendChild(child));
    return node;
  }

  // ── نمایش/پنهان کردن ─────────────────────────────────────────────────
  $$("[data-toggle-target]").forEach((button) => button.addEventListener("click", () => {
    const target = $(button.dataset.toggleTarget);
    if (!target) return;
    target.hidden = !target.hidden;
    if (!target.hidden) {
      const first = $("input:not([type=hidden]), select, textarea", target);
      if (first) first.focus();
    }
  }));

  // ── استان ← شهر ─────────────────────────────────────────────────────
  $$("select[data-province-select]").forEach((province) => {
    const city = $(province.dataset.provinceSelect);
    if (!city) return;
    province.addEventListener("change", async () => {
      city.innerHTML = "";
      city.appendChild(el("option", { value: "", text: province.value ? "در حال بارگذاری…" : "ابتدا استان" }));
      city.disabled = true;
      if (!province.value) return;
      try {
        const response = await fetch(`/api/locations/provinces/${encodeURIComponent(province.value)}/cities`, { headers: { Accept: "application/json" } });
        const cities = response.ok ? await response.json() : [];
        city.innerHTML = "";
        city.appendChild(el("option", { value: "", text: cities.length ? "انتخاب کنید" : "شهری ثبت نشده" }));
        cities.forEach((c) => city.appendChild(el("option", { value: c.id, text: c.name })));
        city.disabled = cities.length === 0;
      } catch (_) {
        city.innerHTML = "";
        city.appendChild(el("option", { value: "", text: "خطا در دریافت شهرها" }));
      }
    });
  });

  // ── کانبان سفارش‌ها ───────────────────────────────────────────────────
  const board = $("[data-order-board]");
  if (board && board.dataset.orderBoard === "move") initBoard(board);

  /**
   * کشیدن و رها کردن با Pointer Events (HTML5 Drag&Drop روی لمسی کار نمی‌کند).
   * کارت بعد از ۶ پیکسل حرکت «برداشته» می‌شود تا کلیک معمولی روی لینک سفارش خراب نشود.
   * فقط یک ستون جلو مجاز است؛ پاسخ سرور { ok, message, redirect? } است.
   */
  function initBoard(board) {
    const columns = $$("[data-board-column]", board);
    const order = columns.map((c) => c.dataset.boardColumn);
    const csrf = () => ($('meta[name="csrf-token"]') || {}).content || "";
    let drag = null;
    let suppressClick = false;

    board.addEventListener("pointerdown", (event) => {
      const card = event.target.closest("[data-board-card]");
      if (!card || event.button !== 0) return;
      drag = { card, startX: event.clientX, startY: event.clientY, started: false, pointerId: event.pointerId, over: null, touch: event.pointerType === "touch" };
      // لمسی: برای اینکه اسکرول ستون‌ها خراب نشود، کارت با «نگه داشتن» (۳۰۰ میلی‌ثانیه) برداشته می‌شود.
      if (drag.touch) {
        const pending = drag;
        pending.timer = setTimeout(() => {
          if (drag === pending && !pending.started) {
            start({ clientX: pending.startX, clientY: pending.startY });
            pending.card.style.left = `${pending.startX - pending.offsetX}px`;
            pending.card.style.top = `${pending.startY - pending.offsetY}px`;
            if (navigator.vibrate) navigator.vibrate(15);
          }
        }, 300);
      }
    });
    // وقتی کارت برداشته شده، حرکت انگشت نباید صفحه را اسکرول کند.
    document.addEventListener("touchmove", (event) => { if (drag && drag.started) event.preventDefault(); }, { passive: false });

    document.addEventListener("pointermove", (event) => {
      if (!drag || event.pointerId !== drag.pointerId) return;
      const dx = event.clientX - drag.startX;
      const dy = event.clientY - drag.startY;
      if (!drag.started) {
        if (Math.hypot(dx, dy) < 6) return;
        if (drag.touch) { clearTimeout(drag.timer); drag = null; return; } // حرکت پیش از نگه داشتن = اسکرول معمولی
        start(event);
      }
      event.preventDefault();
      drag.card.style.left = `${event.clientX - drag.offsetX}px`;
      drag.card.style.top = `${event.clientY - drag.offsetY}px`;
      const under = document.elementFromPoint(event.clientX, event.clientY);
      const column = under && under.closest("[data-board-column]");
      if (column !== drag.over) {
        if (drag.over) drag.over.classList.remove("is-over");
        drag.over = column;
        if (column && column !== drag.home) column.classList.add("is-over");
      }
      autoScroll(event.clientX);
    }, { passive: false });

    document.addEventListener("pointerup", (event) => { if (drag && event.pointerId === drag.pointerId) finish(); });
    document.addEventListener("pointercancel", () => { if (drag) cancel(); });
    // کلیکی که بلافاصله بعد از کشیدن رخ می‌دهد نباید لینک سفارش را باز کند.
    board.addEventListener("click", (event) => {
      if (suppressClick) { event.preventDefault(); event.stopPropagation(); suppressClick = false; }
    }, true);

    function start(event) {
      const rect = drag.card.getBoundingClientRect();
      drag.started = true;
      drag.home = drag.card.closest("[data-board-column]");
      drag.offsetX = event.clientX - rect.left;
      drag.offsetY = event.clientY - rect.top;
      drag.placeholder = drag.card.cloneNode(true);
      drag.placeholder.classList.add("is-placeholder");
      drag.placeholder.removeAttribute("data-board-card");
      drag.card.parentNode.insertBefore(drag.placeholder, drag.card);
      drag.card.style.width = `${rect.width}px`;
      drag.card.classList.add("is-dragging");
      document.body.appendChild(drag.card);
    }

    function autoScroll(x) {
      const rect = board.getBoundingClientRect();
      if (x < rect.left + 40) board.scrollLeft -= 12;
      else if (x > rect.right - 40) board.scrollLeft += 12;
    }

    function restore() {
      const { card, placeholder } = drag;
      card.classList.remove("is-dragging");
      card.style.left = card.style.top = card.style.width = "";
      placeholder.parentNode.replaceChild(card, placeholder);
      columns.forEach((c) => c.classList.remove("is-over"));
    }

    function cancel() {
      clearTimeout(drag.timer);
      if (drag.started) restore();
      drag = null;
    }

    async function finish() {
      const current = drag;
      clearTimeout(current.timer);
      if (!current.started) { drag = null; return; }
      suppressClick = true;
      setTimeout(() => { suppressClick = false; }, 300);
      const target = current.over;
      restore();
      drag = null;
      if (!target || target === current.home) return;

      const from = current.card.dataset.status;
      const to = target.dataset.boardColumn;
      if (order.indexOf(to) !== order.indexOf(from) + 1) {
        toast("از روی تخته فقط می‌توان سفارش را یک مرحله جلو برد؛ برای لغو یا مرجوعی صفحهٔ سفارش را باز کنید.", "error");
        return;
      }

      const body = new FormData();
      body.append("id", current.card.dataset.boardCard);
      body.append("fromStatus", from);
      body.append("toStatus", to);
      current.card.setAttribute("aria-busy", "true");
      let result;
      try {
        const response = await fetch(board.dataset.moveUrl, {
          method: "POST", body, credentials: "same-origin",
          headers: { "X-Requested-With": "fetch", RequestVerificationToken: csrf(), Accept: "application/json" },
        });
        result = response.ok ? await response.json() : { ok: false, message: "خطا در ارتباط با سرور." };
      } catch (_) {
        result = { ok: false, message: "خطا در ارتباط با سرور." };
      }
      current.card.removeAttribute("aria-busy");

      if (result.ok) {
        moveCard(current.card, current.home, target);
        toast(result.message || "وضعیت سفارش تغییر کرد.", "success");
        return;
      }
      if (result.redirect) {
        const go = window.FratakModal
          ? await window.FratakModal.confirm({ message: `${result.message} صفحهٔ سفارش باز شود؟`, okText: "باز کردن سفارش" })
          : false;
        if (go) window.location.href = result.redirect;
        return;
      }
      toast(result.message || "تغییر وضعیت انجام نشد.", "error");
    }

    function moveCard(card, fromColumn, toColumn) {
      const list = $("[data-board-list]", toColumn);
      const emptyNote = $("[data-board-empty]", list);
      if (emptyNote) emptyNote.remove();
      card.dataset.status = toColumn.dataset.boardColumn;
      list.insertBefore(card, list.firstChild);
      [fromColumn, toColumn].forEach((column, i) => {
        const counter = $("[data-board-count]", column);
        if (!counter) return;
        const value = Number(counter.textContent.replace(/[^0-9]/g, "")) || 0;
        counter.textContent = Math.max(0, value + (i === 0 ? -1 : 1)).toLocaleString("en-US");
      });
    }
  }

  // ── فرم ثبت سفارش پنل ───────────────────────────────────────────────
  const form = $("form[data-staff-order]");
  if (form) initStaffOrder(form);

  function initStaffOrder(form) {
    const search = $("[data-product-search]", form);
    const results = $("[data-product-results]", form);
    const body = $("[data-order-items]", form);
    const empty = $("[data-items-empty]", form);
    const shipping = $("[data-shipping-select]", form);
    const submit = $("[data-submit-order]", form);
    const canSubmit = submit ? !submit.disabled : false;
    /** اقلام فعلی؛ کلید = شناسهٔ محصول. */
    const items = new Map();
    let hits = [];
    let active = -1;
    let timer = 0;
    let lastQuery = "";

    // اقلام فرمی که ثبتش خطا داده بود (سرور در data-initial-items می‌گذارد).
    try {
      JSON.parse(form.dataset.initialItems || "[]").forEach((p) => add(p, p.qty || 1, true));
    } catch (_) { /* JSON خراب → فرم خالی */ }
    render();

    search.addEventListener("input", () => {
      clearTimeout(timer);
      timer = setTimeout(lookup, 250);
    });
    search.addEventListener("keydown", (event) => {
      if (results.hidden || hits.length === 0) {
        if (event.key === "Enter") event.preventDefault(); // Enter در جست‌وجو نباید کل فرم را ثبت کند
        return;
      }
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        active = (active + (event.key === "ArrowDown" ? 1 : -1) + hits.length) % hits.length;
        $$(".a-picker-item", results).forEach((node, i) => node.classList.toggle("is-active", i === active));
      } else if (event.key === "Enter") {
        event.preventDefault();
        pick(hits[Math.max(active, 0)]);
      } else if (event.key === "Escape") {
        results.hidden = true;
      }
    });
    document.addEventListener("click", (event) => {
      if (!results.contains(event.target) && event.target !== search) results.hidden = true;
    });
    if (shipping) shipping.addEventListener("change", totals);

    async function lookup() {
      const q = search.value.trim();
      if (q.length < 2) { results.hidden = true; return; }
      lastQuery = q;
      try {
        const response = await fetch(`${search.dataset.productSearch}&q=${encodeURIComponent(q)}`, { headers: { Accept: "application/json" } });
        if (!response.ok || q !== lastQuery) return;
        hits = await response.json();
      } catch (_) { hits = []; }
      active = -1;
      results.innerHTML = "";
      if (hits.length === 0) {
        results.appendChild(el("div", { className: "a-picker-empty", text: "محصول فعال و موجودی با این نام پیدا نشد." }));
      }
      hits.forEach((p) => {
        const priceText = p.finalPrice < p.price ? `${money(p.finalPrice)} (به‌جای ${money(p.price)})` : money(p.price);
        const button = el("button", { type: "button", className: "a-picker-item" }, [
          el("img", { src: p.image, alt: "", loading: "lazy" }),
          el("span", { className: "flex-1 min-w-0" }, [
            el("span", { className: "block font-semibold truncate", text: p.name }),
            el("span", { className: "block a-muted text-xs", text: `${p.sku} · ${priceText} تومان · موجودی ${p.stock}` }),
          ]),
        ]);
        button.addEventListener("click", () => pick(p));
        results.appendChild(button);
      });
      results.hidden = false;
    }

    function pick(p) {
      if (!p) return;
      add(p, 1, false);
      results.hidden = true;
      search.value = "";
      search.focus();
      render();
    }

    function add(p, qty, silent) {
      const existing = items.get(p.id);
      if (existing) {
        existing.qty = Math.min(existing.qty + qty, p.stock);
        if (!silent) toast("تعداد این کالا یکی بیشتر شد.", "success");
      } else {
        items.set(p.id, { ...p, qty: Math.max(1, Math.min(qty, p.stock || qty)) });
      }
    }

    function render() {
      body.innerHTML = "";
      let index = 0;
      items.forEach((item) => {
        const qty = el("input", {
          type: "number", min: 1, max: item.stock, value: item.qty, required: true,
          className: "a-input a-input-sm a-qty a-ltr", name: `input.Items[${index}].Quantity`, "aria-label": `تعداد ${item.name}`,
        });
        qty.addEventListener("input", () => {
          const value = Math.floor(Number(qty.value) || 0);
          item.qty = Math.max(0, Math.min(value, item.stock));
          if (value > item.stock) { qty.value = item.stock; toast(`موجودی «${item.name}» فقط ${item.stock} عدد است.`, "error"); }
          lineTotal.textContent = money(item.finalPrice * item.qty);
          totals();
        });
        const remove = el("button", { type: "button", className: "a-btn a-btn-danger-soft a-btn-xs", text: "حذف" });
        remove.addEventListener("click", () => { items.delete(item.id); render(); });
        const lineTotal = el("span", { text: money(item.finalPrice * item.qty) });
        body.appendChild(el("tr", {}, [
          el("td", { "data-label": "محصول" }, [
            el("input", { type: "hidden", name: `input.Items[${index}].ProductId`, value: item.id }),
            el("div", { className: "font-semibold", text: item.name }),
            el("div", { className: "a-muted text-xs a-ltr", text: `${item.sku} · موجودی ${item.stock}` }),
          ]),
          el("td", { "data-label": "قیمت واحد", className: "num" }, [
            el("span", { text: money(item.finalPrice) }),
            item.finalPrice < item.price ? el("del", { className: "block a-muted text-xs", text: money(item.price) }) : null,
          ]),
          el("td", { "data-label": "تعداد" }, [qty]),
          el("td", { "data-label": "جمع", className: "num" }, [lineTotal]),
          el("td", { className: "actions", "data-label": "" }, [remove]),
        ]));
        index++;
      });
      empty.hidden = items.size > 0;
      totals();
    }

    /** جمع تخمینی: کالاها + هزینهٔ پایهٔ روش ارسال (رایگان بالای سقف). مبلغ قطعی را سرور حساب می‌کند. */
    function totals() {
      let sum = 0;
      items.forEach((item) => { sum += item.finalPrice * item.qty; });
      let ship = 0;
      const option = shipping && shipping.selectedOptions[0];
      if (option) {
        ship = Number(option.dataset.cost) || 0;
        const free = option.dataset.free ? Number(option.dataset.free) : 0;
        if (free > 0 && sum >= free) ship = 0;
      }
      const total = items.size > 0 ? sum + ship : 0;
      $("[data-sum-items]", form).textContent = money(sum);
      $("[data-sum-shipping]", form).textContent = items.size > 0 ? (ship === 0 ? "رایگان" : money(ship)) : "0";
      $("[data-sum-total]", form).textContent = money(total);
      $("[data-sum-words]", form).textContent = total > 0 ? words(total) : "";
      if (submit) submit.disabled = !canSubmit || items.size === 0 || Array.from(items.values()).some((i) => i.qty < 1);
    }
  }

  // ── دور ۲۴: انتخاب یک کالا (فرم «ایده ← طرح») ─────────────────────────
  // نمونه: <input data-pick-product="?handler=Products"> + <input type="hidden" name="productId" data-pick-product-id>
  $$("[data-pick-product]").forEach((search) => {
    const box = search.closest("[data-pick-product-box]") || search.parentElement;
    const results = $("[data-pick-product-results]", box);
    const hidden = $("[data-pick-product-id]", box);
    const label = $("[data-pick-product-label]", box);
    const clear = $("[data-pick-product-clear]", box);
    if (!results || !hidden) return;
    let timer = 0;
    let lastQuery = "";

    const choose = (p) => {
      hidden.value = p ? p.id : "";
      if (label) label.textContent = p ? p.name : "";
      if (clear) clear.hidden = !p;
      results.hidden = true;
      search.value = "";
    };

    search.addEventListener("input", () => {
      clearTimeout(timer);
      timer = setTimeout(lookup, 250);
    });
    search.addEventListener("keydown", (event) => {
      if (event.key === "Enter") event.preventDefault(); // Enter در جست‌وجو فرم را ثبت نکند
      if (event.key === "Escape") results.hidden = true;
    });
    document.addEventListener("click", (event) => {
      if (!results.contains(event.target) && event.target !== search) results.hidden = true;
    });
    if (clear) clear.addEventListener("click", () => choose(null));

    async function lookup() {
      const q = search.value.trim();
      if (q.length < 2) { results.hidden = true; return; }
      lastQuery = q;
      let hits = [];
      try {
        const response = await fetch(`${search.dataset.pickProduct}&q=${encodeURIComponent(q)}`, { headers: { Accept: "application/json" } });
        if (!response.ok || q !== lastQuery) return;
        hits = await response.json();
      } catch (_) { hits = []; }
      results.innerHTML = "";
      if (hits.length === 0) {
        results.appendChild(el("div", { className: "a-picker-empty", text: "کالای فعال و موجودی با این نام پیدا نشد." }));
      }
      hits.forEach((p) => {
        const button = el("button", { type: "button", className: "a-picker-item" }, [
          el("img", { src: p.image, alt: "", loading: "lazy" }),
          el("span", { className: "flex-1 min-w-0" }, [
            el("span", { className: "block font-semibold truncate", text: p.name }),
            el("span", { className: "block a-muted text-xs", text: `${money(p.finalPrice)} تومان · موجودی ${p.stock}` }),
          ]),
        ]);
        button.addEventListener("click", () => choose(p));
        results.appendChild(button);
      });
      results.hidden = false;
    }
  });
})();
