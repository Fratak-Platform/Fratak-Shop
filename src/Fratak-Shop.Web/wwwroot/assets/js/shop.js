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
