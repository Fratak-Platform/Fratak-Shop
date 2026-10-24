/*
 * Storefront behaviour (no dependencies):
 *  - [data-countdown]      : discount countdown (unix ms) with [data-cd=days|hours|minutes|seconds]
 *  - [data-autosubmit]     : submit the owning form on change
 *  - form[data-confirm]    : custom confirmation dialog before submit (modal.js)
 *                            data-confirm-title / data-confirm-ok / data-confirm-danger
 *  - [data-alert-id]       : page alerts; dismissible ones are remembered in localStorage
 *  - [data-width]          : percentage width (CSP: no inline styles in markup)
 *  - [data-card-input]     : bank card number grouping (4-4-4-4)
 *  - [data-invoice-quick-print]: print the A4 invoice without leaving the page (hidden iframe)
 *  - [data-toast]          : auto-hide / close status messages
 *  - [data-share]          : native share or copy link
 *  - [data-filter-list]    : client-side filtering of a list (brand filter)
 *  - [data-province-select]: load cities of the selected province
 *  - [data-checkout-summary]: live shipping cost / total / slots / payment rules on checkout
 *  - [data-address-switch] : reload checkout with the options of another address
 *  - [data-faq-search]     : client-side FAQ search
 *  - [data-amount-preset]  : fill an amount input (wallet top-up)
 *  - [data-copy]           : copy text to the clipboard
 *  - GET forms             : empty fields are not sent (clean URLs)
 */
(function () {
  "use strict";

  const pad = (n) => String(n).padStart(2, "0");
  const formatPrice = (n) => Number(n).toLocaleString("en-US");

  // ── Countdown ────────────────────────────────────────────────────────
  const timers = document.querySelectorAll("[data-countdown]");
  if (timers.length) {
    const tick = () => {
      const now = Date.now();
      timers.forEach((timer) => {
        let distance = Math.max(0, Number(timer.dataset.countdown) - now);
        const days = Math.floor(distance / 86400000);
        distance %= 86400000;
        const values = {
          days: days,
          hours: pad(Math.floor(distance / 3600000)),
          minutes: pad(Math.floor((distance % 3600000) / 60000)),
          seconds: pad(Math.floor((distance % 60000) / 1000)),
        };
        Object.keys(values).forEach((key) => {
          const el = timer.querySelector(`[data-cd="${key}"]`);
          if (el) el.textContent = values[key];
        });
        const daysBox = timer.querySelector(".days-container");
        if (daysBox) daysBox.classList.toggle("hidden", days === 0);
      });
    };
    tick();
    setInterval(tick, 1000);
  }

  // ── Auto submit ──────────────────────────────────────────────────────
  document.querySelectorAll("[data-autosubmit]").forEach((input) => {
    input.addEventListener("change", () => {
      const form = input.form;
      if (!form) return;
      if (typeof form.requestSubmit === "function") form.requestSubmit();
      else form.submit();
    });
  });

  // ── Clean GET forms ──────────────────────────────────────────────────
  document.addEventListener("submit", (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement)) return;

    if (form.dataset.confirm && form.dataset.confirmed !== "1") {
      event.preventDefault();
      event.stopImmediatePropagation();
      const submitter = event.submitter || null;
      const ask = window.FratakModal
        ? window.FratakModal.confirm({
            title: form.dataset.confirmTitle,
            message: form.dataset.confirm,
            okText: form.dataset.confirmOk,
            danger: "confirmDanger" in form.dataset,
          })
        : Promise.resolve(window.confirm(form.dataset.confirm));
      ask.then((confirmed) => {
        if (!confirmed) return;
        form.dataset.confirmed = "1";
        try {
          if (typeof form.requestSubmit === "function") form.requestSubmit(submitter && submitter.form === form ? submitter : undefined);
          else form.submit();
        } finally {
          // A page that stays open (validation error) asks again next time.
          setTimeout(() => delete form.dataset.confirmed, 0);
        }
      });
      return;
    }

    if ((form.method || "get").toLowerCase() === "get") {
      Array.from(form.elements).forEach((el) => {
        if (el.name && "value" in el && el.value === "" && el.type !== "submit") el.disabled = true;
      });
      setTimeout(() => Array.from(form.elements).forEach((el) => (el.disabled = false)), 0);
    }
  }, true);

  // ── Toasts ───────────────────────────────────────────────────────────
  document.querySelectorAll("[data-toast]").forEach((toast) => {
    toast.querySelectorAll("[data-toast-close]").forEach((btn) =>
      btn.addEventListener("click", () => btn.parentElement.remove())
    );
    setTimeout(() => toast.remove(), 6000);
  });

  // ── Share / copy link ────────────────────────────────────────────────
  document.querySelectorAll("[data-share]").forEach((btn) => {
    btn.addEventListener("click", async () => {
      const url = new URL(btn.dataset.share, window.location.origin).toString();
      try {
        if (navigator.share) {
          await navigator.share({ title: btn.dataset.shareTitle || document.title, url });
        } else if (navigator.clipboard) {
          await navigator.clipboard.writeText(url);
          if (window.FratakModal) window.FratakModal.toast("لینک کپی شد.", "success");
        }
      } catch (_) { /* user cancelled */ }
    });
  });

  // ── List filter (brand search) ───────────────────────────────────────
  document.querySelectorAll("[data-filter-list]").forEach((input) => {
    const list = document.querySelector(input.dataset.filterList);
    if (!list) return;
    input.addEventListener("input", () => {
      const term = input.value.trim().toLowerCase();
      list.querySelectorAll("[data-filter-item]").forEach((item) => {
        item.hidden = term !== "" && !item.dataset.filterItem.toLowerCase().includes(term);
      });
    });
  });

  // ── Province → city ──────────────────────────────────────────────────
  document.querySelectorAll("[data-province-select]").forEach((province) => {
    const city = document.querySelector(province.dataset.provinceSelect);
    if (!city) return;

    const load = async () => {
      const selected = city.dataset.selected || city.value;
      city.innerHTML = '<option value="">انتخاب شهر</option>';
      if (!province.value) return;
      try {
        const response = await fetch(`/api/locations/provinces/${province.value}/cities`, { headers: { Accept: "application/json" } });
        if (!response.ok) return;
        const cities = await response.json();
        cities.forEach((c) => {
          const option = new Option(c.name, c.id, false, c.id === selected);
          city.add(option);
        });
      } catch (_) { /* network error: leave the list empty */ }
      city.dataset.selected = "";
    };

    province.addEventListener("change", load);
    if (province.value) load();
  });

  // ── Checkout: shipping cost, delivery slots, payment availability ──
  const summary = document.querySelector("[data-checkout-summary]");
  if (summary) {
    const items = Number(summary.dataset.items || 0);
    const wallet = Number(summary.dataset.wallet || 0);
    const codMax = Number(summary.dataset.codMax || 0);
    const couponDiscount = Number(summary.dataset.coupon || 0);
    const shippingOut = summary.querySelector("[data-shipping-output]");
    const totalOut = summary.querySelector("[data-total-output]");
    const freeHint = summary.querySelector("[data-free-hint]");
    const freeRemaining = summary.querySelector("[data-free-remaining]");
    const paymentHint = document.querySelector("[data-payment-hint]");
    const shippingInputs = document.querySelectorAll("input[data-shipping-cost]");
    const slotGroups = document.querySelectorAll("[data-slot-group]");
    const cod = document.querySelector('input[data-payment="CashOnDelivery"]');
    const walletInput = document.querySelector('input[data-payment="Wallet"]');

    const update = () => {
      const checked = document.querySelector("input[data-shipping-cost]:checked");
      const fullCost = checked ? Number(checked.dataset.shippingCost) : 0;
      const shippingDiscount = checked ? Math.min(fullCost, Number(checked.dataset.couponShipping || 0)) : 0;
      const cost = fullCost - shippingDiscount;
      const total = Math.max(0, items - couponDiscount) + cost;
      if (shippingOut) {
        shippingOut.textContent = !checked
          ? "-"
          : cost === 0
            ? shippingDiscount > 0 ? "رایگان (کد تخفیف)" : "رایگان"
            : `${formatPrice(cost)} تومان`;
      }
      if (totalOut) totalOut.textContent = formatPrice(total);

      const remaining = checked ? Number(checked.dataset.remaining || 0) : 0;
      if (freeHint) freeHint.classList.toggle("hidden", !(remaining > 0));
      if (freeRemaining) freeRemaining.textContent = formatPrice(remaining);

      // Only the time slots of the selected method are shown and submitted.
      slotGroups.forEach((group) => {
        const active = checked && checked.dataset.slotPanel === group.id;
        group.classList.toggle("hidden", !active);
        group.querySelectorAll('input[type="radio"]').forEach((radio) => {
          if (!active) radio.checked = false;
        });
      });

      const hints = [];
      if (cod) {
        const allowed = (!checked || checked.dataset.cod === "1") && (codMax <= 0 || total <= codMax);
        cod.disabled = !allowed;
        if (!allowed) hints.push("پرداخت در محل برای این روش ارسال یا این مبلغ امکان‌پذیر نیست.");
      }
      if (walletInput) {
        const enough = wallet >= total;
        walletInput.disabled = !enough;
        if (!enough) hints.push(`موجودی کیف پول (${formatPrice(wallet)} تومان) برای پرداخت کل مبلغ کافی نیست.`);
      }
      const selected = document.querySelector('input[name="Input.PaymentMethod"]:checked');
      if (!selected || selected.disabled) {
        if (selected) selected.checked = false;
        const first = document.querySelector('input[name="Input.PaymentMethod"]:not(:disabled)');
        if (first) first.checked = true;
      }
      if (paymentHint) {
        paymentHint.textContent = hints.join(" ");
        paymentHint.classList.toggle("hidden", hints.length === 0);
      }
    };

    shippingInputs.forEach((r) => r.addEventListener("change", update));
    update();
  }

  // Changing the delivery address reloads the page with the options of that address.
  document.querySelectorAll("input[data-address-switch]").forEach((radio) => {
    radio.addEventListener("change", () => {
      if (!radio.checked) return;
      const url = new URL(window.location.href);
      url.searchParams.set("addressId", radio.value);
      window.location.assign(url.toString());
    });
  });

  // ── FAQ search ───────────────────────────────────────────────────────
  const faqSearch = document.querySelector("[data-faq-search]");
  if (faqSearch) {
    const empty = document.querySelector("[data-faq-empty]");
    faqSearch.addEventListener("input", () => {
      const term = faqSearch.value.trim().toLowerCase();
      let visible = 0;
      document.querySelectorAll("[data-faq-group]").forEach((group) => {
        let groupVisible = 0;
        group.querySelectorAll("[data-faq-item]").forEach((item) => {
          const match = term === "" || item.textContent.toLowerCase().includes(term);
          item.hidden = !match;
          if (match) groupVisible++;
          if (match && term !== "") item.open = true;
        });
        group.hidden = groupVisible === 0;
        visible += groupVisible;
      });
      if (empty) empty.classList.toggle("hidden", visible > 0);
    });
  }

  // ── Amount presets (wallet top-up) ───────────────────────────────────
  document.querySelectorAll("[data-amount-preset]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const input = document.querySelector(btn.dataset.amountTarget);
      if (input) {
        input.value = btn.dataset.amountPreset;
        input.focus();
      }
    });
  });

  // ── Copy to clipboard ([data-copy]) ──────────────────────────────────
  document.querySelectorAll("[data-copy]").forEach((btn) => {
    btn.addEventListener("click", async () => {
      try {
        await navigator.clipboard.writeText(btn.dataset.copy);
        const old = btn.textContent;
        btn.textContent = "کپی شد";
        setTimeout(() => (btn.textContent = old), 1500);
      } catch (_) { /* clipboard unavailable */ }
    });
  });
  // ── Page alerts: close + remember ([data-alert-id][data-dismissible]) ──
  const DISMISSED_KEY = "fratak:dismissed-alerts";
  const readDismissed = () => {
    try {
      const list = JSON.parse(localStorage.getItem(DISMISSED_KEY) || "[]");
      return Array.isArray(list) ? list : [];
    } catch (_) {
      return [];
    }
  };
  const dismissed = readDismissed();
  document.querySelectorAll("[data-alert-id][data-dismissible]").forEach((alertBox) => {
    const id = alertBox.dataset.alertId;
    const removeBox = () => {
      const group = alertBox.closest("[data-page-alerts]");
      alertBox.remove();
      if (group && !group.querySelector("[data-alert-id]")) group.remove();
    };
    if (dismissed.includes(id)) {
      removeBox();
      return;
    }
    const close = alertBox.querySelector("[data-alert-close]");
    if (!close) return;
    close.addEventListener("click", () => {
      const list = readDismissed().filter((x) => x !== id);
      list.push(id);
      try {
        localStorage.setItem(DISMISSED_KEY, JSON.stringify(list.slice(-50)));
      } catch (_) { /* private mode */ }
      removeBox();
    });
  });

  // ── Percentage widths ([data-width]) ─────────────────────────────────
  document.querySelectorAll("[data-width]").forEach((bar) => {
    const value = Math.max(0, Math.min(100, Number(bar.dataset.width) || 0));
    bar.style.width = `${value}%`;
  });

  // ── Bank card input ([data-card-input]) ──────────────────────────────
  const toLatinDigits = (value) => value
    .replace(/[\u06F0-\u06F9]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
    .replace(/[\u0660-\u0669]/g, (d) => String(d.charCodeAt(0) - 0x0660));
  document.querySelectorAll("[data-card-input]").forEach((input) => {
    input.addEventListener("input", () => {
      const digits = toLatinDigits(input.value).replace(/\D/g, "").slice(0, 16);
      const grouped = digits.replace(/(\d{4})(?=\d)/g, "$1 ");
      if (grouped !== input.value) input.value = grouped;
    });
  });
  // ── Quick invoice print (order page) ─────────────────────────────────
  // The invoice is loaded in a hidden same-origin iframe and printed from there, so the
  // printout is the A4 invoice – not the order page. iOS can't print iframes: open it instead.
  const isIos = /iP(hone|ad|od)/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  document.querySelectorAll("[data-invoice-quick-print]").forEach((button) => {
    button.addEventListener("click", () => {
      const url = button.dataset.invoiceQuickPrint;
      if (isIos) {
        window.location.assign(url);
        return;
      }
      if (button.disabled) return;

      const label = button.querySelector("[data-label]");
      const oldText = label ? label.textContent : "";
      button.disabled = true;
      if (label) label.textContent = "در حال آماده‌سازی...";

      const frame = document.createElement("iframe");
      frame.setAttribute("aria-hidden", "true");
      frame.tabIndex = -1;
      frame.style.cssText = "position:fixed;right:0;bottom:0;width:1px;height:1px;border:0;opacity:0;pointer-events:none;";
      let done = false;

      const cleanup = () => {
        window.removeEventListener("message", onMessage);
        button.disabled = false;
        if (label) label.textContent = oldText;
        // Some browsers print asynchronously: keep the frame until the dialog is closed.
        const remove = () => frame.remove();
        try {
          frame.contentWindow.addEventListener("afterprint", () => setTimeout(remove, 500), { once: true });
        } catch (_) { /* ignore */ }
        setTimeout(remove, 120000);
      };

      const onMessage = (event) => {
        if (event.origin !== window.location.origin || event.source !== frame.contentWindow) return;
        if (!event.data || event.data.type !== "fratak-invoice-ready" || done) return;
        done = true;
        try {
          frame.contentWindow.focus();
          frame.contentWindow.print();
        } catch (_) {
          window.open(url, "_blank", "noopener");
        }
        cleanup();
      };

      window.addEventListener("message", onMessage);
      setTimeout(() => {
        if (done) return;
        done = true;
        cleanup();
        window.open(url, "_blank", "noopener");
      }, 15000);

      frame.src = url;
      document.body.appendChild(frame);
    });
  });
})();

/* Review photos: preview, remove, client-side count/size/type checks (the server re-checks everything). */
(function () {
  "use strict";
  const allowed = ["image/jpeg", "image/png", "image/webp"];
  const notify = (message) => (window.FratakModal ? window.FratakModal.toast(message, "error") : null);

  document.querySelectorAll("[data-review-images]").forEach((box) => {
    const input = box.querySelector("[data-review-images-input]");
    const preview = box.querySelector("[data-review-images-preview]");
    if (!input || !preview || typeof DataTransfer === "undefined") return;

    const max = parseInt(box.dataset.max || "3", 10);
    const maxBytes = parseInt(box.dataset.maxKb || "2048", 10) * 1024;
    let files = [];

    const render = () => {
      preview.querySelectorAll("img").forEach((img) => URL.revokeObjectURL(img.src));
      preview.textContent = "";
      const transfer = new DataTransfer();
      files.forEach((file, index) => {
        transfer.items.add(file);
        const item = document.createElement("div");
        item.className = "relative aspect-square overflow-hidden rounded-lg border bg-zinc-50";
        const img = document.createElement("img");
        img.src = URL.createObjectURL(file);
        img.alt = file.name;
        img.className = "h-full w-full object-cover";
        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "absolute top-1 left-1 flex h-6 w-6 items-center justify-center rounded-full bg-black/60 text-sm leading-none text-white";
        remove.setAttribute("aria-label", "حذف تصویر");
        remove.textContent = "×";
        remove.addEventListener("click", () => {
          files.splice(index, 1);
          render();
        });
        item.append(img, remove);
        preview.appendChild(item);
      });
      input.files = transfer.files;
    };

    input.addEventListener("change", () => {
      const picked = Array.from(input.files || []);
      for (const file of picked) {
        if (files.length >= max) {
          notify("حداکثر " + max + " تصویر قابل ارسال است.");
          break;
        }
        if (!allowed.includes(file.type)) {
          notify("فقط تصاویر JPG، PNG و WebP قابل ارسال هستند.");
          continue;
        }
        if (file.size > maxBytes) {
          notify("حجم تصویر «" + file.name + "» بیش از حد مجاز است.");
          continue;
        }
        files.push(file);
      }
      render();
    });
  });
})();

/* Instant search suggestions (GET /api/search/suggest?q=), keyboard navigable. */
(function () {
  "use strict";
  const forms = document.querySelectorAll("form[data-search-suggest]");
  if (!forms.length || !window.fetch) return;

  const make = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  };

  let uid = 0;
  forms.forEach((form) => {
    const input = form.querySelector('input[name="q"]');
    if (!input) return;

    const listId = "search-suggest-" + ++uid;
    const panel = make("div", "absolute inset-x-0 z-50 hidden max-h-[70vh] overflow-y-auto rounded-xl border bg-white text-right shadow-xl " +
      (form.hasAttribute("data-suggest-up") ? "bottom-full mb-2" : "top-full mt-2"));
    panel.id = listId;
    panel.setAttribute("role", "listbox");
    form.appendChild(panel);

    input.setAttribute("role", "combobox");
    input.setAttribute("aria-autocomplete", "list");
    input.setAttribute("aria-controls", listId);
    input.setAttribute("aria-expanded", "false");

    let timer = null;
    let controller = null;
    let active = -1;
    const cache = new Map();

    const options = () => Array.from(panel.querySelectorAll("[data-suggest-option]"));
    const close = () => {
      panel.classList.add("hidden");
      input.setAttribute("aria-expanded", "false");
      active = -1;
    };
    const open = () => {
      panel.classList.remove("hidden");
      input.setAttribute("aria-expanded", "true");
    };
    const highlight = (index) => {
      const items = options();
      items.forEach((item, i) => item.classList.toggle("bg-sky-50", i === index));
      active = index;
      if (items[index]) {
        input.setAttribute("aria-activedescendant", items[index].id);
        items[index].scrollIntoView({ block: "nearest" });
      } else {
        input.removeAttribute("aria-activedescendant");
      }
    };

    let optionId = 0;
    const option = (href, className) => {
      const link = make("a", "flex items-center gap-x-3 px-3 py-2 text-sm text-zinc-700 transition hover:bg-sky-50 " + (className || ""));
      link.href = href;
      link.id = listId + "-o" + ++optionId;
      link.setAttribute("role", "option");
      link.setAttribute("data-suggest-option", "");
      return link;
    };

    const section = (title) => make("div", "px-3 pt-3 pb-1 text-xs font-semibold text-zinc-400", title);

    const render = (data) => {
      panel.textContent = "";
      active = -1;
      const hasLinks = data.categories.length || data.brands.length;

      if (!data.products.length && !hasLinks) {
        panel.appendChild(make("div", "px-3 py-4 text-center text-sm text-zinc-500", "محصولی با این عبارت پیدا نشد."));
        open();
        return;
      }

      if (data.products.length) {
        panel.appendChild(section("محصولات"));
        data.products.forEach((product) => {
          const link = option(product.url);
          const img = make("img", "h-11 w-11 shrink-0 rounded-lg bg-zinc-50 object-contain");
          img.src = product.image;
          img.alt = "";
          img.loading = "lazy";
          img.width = 44;
          img.height = 44;
          const text = make("div", "min-w-0 flex-1");
          text.appendChild(make("div", "truncate", product.name));
          const price = make("div", "mt-0.5 flex items-center gap-x-2 text-xs");
          if (product.available) {
            price.appendChild(make("span", "font-semibold text-zinc-800", product.price));
            if (product.oldPrice) price.appendChild(make("span", "text-zinc-400 line-through", product.oldPrice));
          } else {
            price.appendChild(make("span", "text-red-400", "ناموجود"));
          }
          text.appendChild(price);
          link.append(img, text);
          panel.appendChild(link);
        });
      }

      const chips = (title, items) => {
        if (!items.length) return;
        panel.appendChild(section(title));
        const row = make("div", "flex flex-wrap gap-2 px-3 pb-2");
        items.forEach((item) => {
          const link = option(item.url, "rounded-full border px-3 py-1 text-xs");
          link.classList.remove("px-3", "py-2", "text-sm");
          link.textContent = item.name;
          row.appendChild(link);
        });
        panel.appendChild(row);
      };
      chips("دسته‌بندی‌ها", data.categories);
      chips("برندها", data.brands);

      const all = option(data.searchUrl, "justify-center border-t font-semibold text-sky-600");
      all.textContent = data.total > data.products.length
        ? "مشاهده همه " + data.total.toLocaleString("fa-IR") + " نتیجه"
        : "مشاهده نتایج جستجو";
      panel.appendChild(all);
      open();
    };

    const load = (query) => {
      if (cache.has(query)) {
        render(cache.get(query));
        return;
      }
      if (controller) controller.abort();
      controller = typeof AbortController !== "undefined" ? new AbortController() : null;
      fetch("/api/search/suggest?q=" + encodeURIComponent(query), {
        headers: { Accept: "application/json" },
        signal: controller ? controller.signal : undefined,
      })
        .then((response) => (response.ok ? response.json() : null))
        .then((data) => {
          if (!data || input.value.trim() !== query) return;
          cache.set(query, data);
          render(data);
        })
        .catch(() => { /* aborted or offline: the normal search still works */ });
    };

    input.addEventListener("input", () => {
      clearTimeout(timer);
      const query = input.value.trim();
      if (query.length < 2) {
        close();
        return;
      }
      timer = setTimeout(() => load(query), 250);
    });

    input.addEventListener("keydown", (event) => {
      const items = options();
      if (event.key === "ArrowDown" && items.length) {
        event.preventDefault();
        if (panel.classList.contains("hidden")) open();
        highlight((active + 1) % items.length);
      } else if (event.key === "ArrowUp" && items.length) {
        event.preventDefault();
        highlight(active <= 0 ? items.length - 1 : active - 1);
      } else if (event.key === "Enter" && active >= 0 && items[active]) {
        event.preventDefault();
        window.location.href = items[active].href;
      } else if (event.key === "Escape") {
        close();
      }
    });

    input.addEventListener("focus", () => {
      if (panel.childElementCount && input.value.trim().length >= 2) open();
    });

    document.addEventListener("click", (event) => {
      if (!form.contains(event.target)) close();
    });
  });
})();

/* PWA install suggestion after a few page views (counts in localStorage, snoozed for N days on "later"). */
(function () {
  "use strict";
  const box = document.querySelector("[data-pwa-install]");
  if (!box) return;

  const KEY_VIEWS = "fratak_pwa_views";
  const KEY_SNOOZE = "fratak_pwa_snooze_until";
  const KEY_DONE = "fratak_pwa_installed";
  const store = {
    get: (key) => { try { return localStorage.getItem(key); } catch (_) { return null; } },
    set: (key, value) => { try { localStorage.setItem(key, value); } catch (_) { /* private mode */ } },
  };

  const standalone = window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true;
  if (standalone || store.get(KEY_DONE)) return;

  const views = (parseInt(store.get(KEY_VIEWS) || "0", 10) || 0) + 1;
  store.set(KEY_VIEWS, String(views));

  const after = parseInt(box.dataset.after || "3", 10);
  const snoozeDays = parseInt(box.dataset.snooze || "14", 10);
  const snoozedUntil = parseInt(store.get(KEY_SNOOZE) || "0", 10) || 0;
  const eligible = () => views >= after && Date.now() > snoozedUntil;

  const ua = window.navigator.userAgent || "";
  const isIos = /iphone|ipad|ipod/i.test(ua) || (/macintosh/i.test(ua) && navigator.maxTouchPoints > 1);
  const isSafari = /safari/i.test(ua) && !/crios|fxios|edgios|opios/i.test(ua);

  let deferred = null;

  const show = (mode) => {
    box.querySelector("[data-pwa-native]").classList.toggle("hidden", mode !== "native");
    box.querySelector("[data-pwa-ios]").classList.toggle("hidden", mode !== "ios");
    box.hidden = false;
  };
  const hide = () => { box.hidden = true; };
  const snooze = () => {
    store.set(KEY_SNOOZE, String(Date.now() + snoozeDays * 86400000));
    hide();
  };

  box.querySelectorAll("[data-pwa-later]").forEach((button) => button.addEventListener("click", snooze));
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !box.hidden) snooze();
  });

  box.querySelector("[data-pwa-accept]").addEventListener("click", () => {
    if (!deferred) { hide(); return; }
    const prompt = deferred;
    deferred = null;
    prompt.prompt();
    prompt.userChoice
      .then((choice) => {
        if (choice && choice.outcome === "accepted") store.set(KEY_DONE, "1");
        else snooze();
      })
      .catch(snooze)
      .finally(hide);
  });

  window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    deferred = event;
    if (eligible()) setTimeout(() => show("native"), 1500);
  });

  window.addEventListener("appinstalled", () => {
    store.set(KEY_DONE, "1");
    hide();
  });

  if (isIos && isSafari && eligible()) setTimeout(() => show("ios"), 2500);
})();
