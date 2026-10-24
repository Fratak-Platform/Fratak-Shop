/*
 * Storefront behaviour (no dependencies):
 *  - [data-countdown]      : discount countdown (unix ms) with [data-cd=days|hours|minutes|seconds]
 *  - [data-autosubmit]     : submit the owning form on change
 *  - form[data-confirm]    : confirmation dialog before submit
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

    if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
      event.preventDefault();
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
          window.alert("لینک کپی شد.");
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
      const cost = checked ? Number(checked.dataset.shippingCost) : 0;
      const total = items + cost;
      if (shippingOut) shippingOut.textContent = !checked ? "-" : cost === 0 ? "رایگان" : `${formatPrice(cost)} تومان`;
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
})();
