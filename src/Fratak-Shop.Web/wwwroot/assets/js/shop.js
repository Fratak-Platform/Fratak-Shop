/*
 * Storefront behaviour (no dependencies):
 *  - [data-countdown]      : discount countdown (unix ms) with [data-cd=days|hours|minutes|seconds]
 *  - [data-autosubmit]     : submit the owning form on change
 *  - form[data-confirm]    : confirmation dialog before submit
 *  - [data-toast]          : auto-hide / close status messages
 *  - [data-share]          : native share or copy link
 *  - [data-filter-list]    : client-side filtering of a list (brand filter)
 *  - [data-province-select]: load cities of the selected province
 *  - [data-checkout-summary]: live shipping cost / total on the checkout page
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

  // ── Checkout totals ──────────────────────────────────────────────────
  const summary = document.querySelector("[data-checkout-summary]");
  if (summary) {
    const payable = Number(summary.dataset.payable || 0);
    const shippingOut = summary.querySelector("[data-shipping-output]");
    const totalOut = summary.querySelector("[data-total-output]");
    const update = () => {
      const checked = document.querySelector("input[data-shipping-cost]:checked");
      const cost = checked ? Number(checked.dataset.shippingCost) : 0;
      if (shippingOut) shippingOut.textContent = !checked ? "-" : cost === 0 ? "رایگان" : `${formatPrice(cost)} تومان`;
      if (totalOut) totalOut.textContent = formatPrice(payable + cost);
    };
    document.querySelectorAll("input[data-shipping-cost]").forEach((r) => r.addEventListener("change", update));
    update();
  }
})();
