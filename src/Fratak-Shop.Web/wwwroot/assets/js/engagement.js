/*
 * Storefront engagement (no dependencies, works on cached pages):
 *  - window.FratakApi.token() / .post(url, body) : antiforgery-protected JSON calls (/api/…)
 *  - input[data-af-deferred]  : cached pages render forms without a token; it is fetched right
 *                               before the first submit (capture phase, before confirm dialogs)
 *  - [data-captcha-refresh]   : new captcha image (/api/captcha)
 *  - [data-product-view]      : product view beacon (visit counter + recently viewed)
 *  - [data-product-alert]     : «خبرم کن» toggles (back in stock / price drop)
 *  - [data-price-history]     : price history chart (SVG, loaded when opened)
 *  - [data-vote]              : helpful / not helpful votes of reviews and answers
 *  - form[data-answer-form]   : customer answers to product questions
 *  - [data-push]              : browser push notifications on/off (notifications page)
 */
(function () {
  "use strict";

  const toast = (message, type) => {
    if (message && window.FratakModal) window.FratakModal.toast(message, type || "info");
  };
  const GENERIC_ERROR = "ارتباط با سرور برقرار نشد؛ دوباره تلاش کنید.";

  // ── Antiforgery token ────────────────────────────────────────────────
  let tokenRequest = null;
  const pageToken = () => {
    const input = document.querySelector('input[name="__RequestVerificationToken"]:not([data-af-deferred])');
    return input && input.value ? input.value : null;
  };
  const token = (fresh) => {
    if (!fresh) {
      const existing = pageToken();
      if (existing) return Promise.resolve(existing);
    }
    if (!tokenRequest || fresh) {
      tokenRequest = fetch("/api/antiforgery", { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } })
        .then((response) => (response.ok ? response.json() : Promise.reject(new Error(GENERIC_ERROR))))
        .then((data) => data.token)
        .catch((error) => {
          tokenRequest = null;
          throw error;
        });
    }
    return tokenRequest;
  };

  // Errors are shown here; callers get a rejected promise with { handled: true }.
  const post = (url, body) =>
    token()
      .then((value) =>
        fetch(url, {
          method: "POST",
          credentials: "same-origin",
          headers: { "Content-Type": "application/json", Accept: "application/json", RequestVerificationToken: value },
          body: JSON.stringify(body || {}),
        }),
      )
      .then((response) =>
        response
          .json()
          .catch(() => ({}))
          .then((data) => {
            if (response.status === 401 && data.login) {
              toast(data.error, "info");
              setTimeout(() => { window.location.href = data.login; }, 900);
              return Promise.reject({ handled: true });
            }
            if (response.status === 429) {
              toast("تعداد درخواست‌ها زیاد است؛ کمی بعد دوباره تلاش کنید.", "error");
              return Promise.reject({ handled: true });
            }
            if (!response.ok) {
              toast(data.error || GENERIC_ERROR, "error");
              return Promise.reject({ handled: true, data });
            }
            return data;
          }),
      )
      .catch((error) => {
        if (!error || !error.handled) toast(GENERIC_ERROR, "error");
        throw { handled: true };
      });

  window.FratakApi = { token, post };

  // ── Deferred antiforgery on cached pages ─────────────────────────────
  document.addEventListener(
    "submit",
    (event) => {
      const form = event.target;
      if (!(form instanceof HTMLFormElement)) return;
      const input = form.querySelector("input[data-af-deferred]");
      if (!input || input.value) return;

      event.preventDefault();
      event.stopImmediatePropagation();
      const submitter = event.submitter && event.submitter.form === form ? event.submitter : null;
      token(true)
        .then((value) => {
          document.querySelectorAll("input[data-af-deferred]").forEach((field) => { field.value = value; });
          if (typeof form.requestSubmit === "function") form.requestSubmit(submitter || undefined);
          else form.submit();
        })
        .catch(() => toast(GENERIC_ERROR, "error"));
    },
    true,
  );

  // ── Captcha refresh ──────────────────────────────────────────────────
  document.addEventListener("click", (event) => {
    const button = event.target.closest("[data-captcha-refresh]");
    if (!button) return;
    event.preventDefault();
    const box = button.closest("[data-captcha]") || document;
    button.disabled = true;
    fetch("/api/captcha", { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } })
      .then((response) => (response.ok ? response.json() : Promise.reject()))
      .then((data) => {
        const image = box.querySelector("[data-captcha-image]");
        const field = box.querySelector("[data-captcha-token]");
        const answer = box.querySelector('input[name="CaptchaAnswer"]');
        if (image) image.src = data.image;
        if (field) field.value = data.token;
        if (answer) {
          answer.value = "";
          answer.focus();
        }
      })
      .catch(() => toast(GENERIC_ERROR, "error"))
      .finally(() => { button.disabled = false; });
  });

  // ── Product view beacon ──────────────────────────────────────────────
  const viewed = document.querySelector("[data-product-view]");

  // ── آمار بازدید داخلی (مرحلهٔ هفتم – فاز ۷) ──────────────────────────
  // یک درخواست کوچک برای هر صفحه: مسیر، پارامترهای UTM و ارجاع‌دهنده (برای تشخیص منبع ورودی) و شناسهٔ محصول
  // (برای قیف «دیدن محصول»). سرور ربات‌ها و پنل مدیریت را نمی‌شمارد و IP ذخیره نمی‌کند.
  if (window.fetch && document.visibilityState !== "prerender" && !navigator.webdriver) {
    fetch("/api/a/hit", {
      method: "POST",
      credentials: "same-origin",
      keepalive: true,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        p: location.pathname,
        q: location.search,
        r: document.referrer || "",
        pid: viewed ? viewed.dataset.productView : null,
      }),
    }).catch(() => { /* آمار بهترین تلاش است */ });
  }
  if (viewed && window.fetch) {
    fetch("/api/products/" + encodeURIComponent(viewed.dataset.productView) + "/view", {
      method: "POST",
      credentials: "same-origin",
      keepalive: true,
    }).catch(() => { /* counting is best effort */ });
  }

  // ── «خبرم کن» ────────────────────────────────────────────────────────
  document.querySelectorAll("[data-product-alert]").forEach((button) => {
    button.addEventListener("click", () => {
      const type = parseInt(button.dataset.productAlert, 10);
      const active = button.getAttribute("aria-pressed") !== "true";
      const label = button.querySelector("[data-alert-label]");
      button.disabled = true;
      post("/api/products/" + encodeURIComponent(button.dataset.product) + "/alerts", { type, active })
        .then((data) => {
          const on = type === 1 ? data.backInStock : data.priceDrop;
          button.setAttribute("aria-pressed", on ? "true" : "false");
          if (label) label.textContent = on ? button.dataset.onText : button.dataset.offText;
          toast(data.message, "success");
          if (on && window.FratakPush) window.FratakPush.suggest();
        })
        .catch(() => { /* already shown */ })
        .finally(() => { button.disabled = false; });
    });
  });

  // ── Price history chart ──────────────────────────────────────────────
  const SVG_NS = "http://www.w3.org/2000/svg";
  const svg = (tag, attributes) => {
    const node = document.createElementNS(SVG_NS, tag);
    Object.keys(attributes || {}).forEach((key) => node.setAttribute(key, attributes[key]));
    return node;
  };
  const el = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  };
  const toman = (value) => Number(value).toLocaleString("fa-IR") + " تومان";

  const drawChart = (box, data) => {
    box.textContent = "";
    const points = (data.points || []).slice().sort((a, b) => a.t - b.t);
    if (!points.length) {
      box.appendChild(el("p", "text-xs text-zinc-400", "هنوز سابقه‌ای برای قیمت این کالا ثبت نشده است."));
      return;
    }

    const stats = el("div", "mb-3 grid grid-cols-3 gap-2 text-center text-xs");
    [["کمترین", data.lowest, "text-emerald-600"], ["بیشترین", data.highest, "text-red-500"], ["قیمت فعلی", data.current, "text-sky-600"]].forEach(([title, value, color]) => {
      const cell = el("div", "rounded-lg bg-zinc-50 p-2 dark:bg-zinc-800");
      cell.appendChild(el("div", "text-zinc-400", title));
      cell.appendChild(el("div", "mt-1 font-semibold " + color, value != null ? toman(value) : "-"));
      stats.appendChild(cell);
    });
    box.appendChild(stats);

    if (!data.hasData) {
      box.appendChild(el("p", "mb-2 text-xs text-zinc-400", "قیمت این کالا در " + Number(data.days).toLocaleString("fa-IR") + " روز گذشته تغییری نداشته است."));
    }

    const W = 600, H = 200, PAD_X = 8, PAD_TOP = 14, PAD_BOTTOM = 22;
    const now = Date.now();
    const first = points[0].t;
    const last = Math.max(now, points[points.length - 1].t);
    const span = Math.max(last - first, 1);
    const prices = points.map((p) => p.price);
    let min = Math.min.apply(null, prices);
    let max = Math.max.apply(null, prices);
    if (max === min) { max += max * 0.05 || 1; min -= min * 0.05 || 1; }
    const x = (t) => PAD_X + ((t - first) / span) * (W - PAD_X * 2);
    const y = (price) => PAD_TOP + (1 - (price - min) / (max - min)) * (H - PAD_TOP - PAD_BOTTOM);

    const wrap = el("div", "relative");
    wrap.setAttribute("dir", "ltr");
    const chart = svg("svg", { viewBox: "0 0 " + W + " " + H, class: "h-48 w-full touch-none select-none", role: "img", "aria-label": "نمودار تغییرات قیمت" });

    [0, 0.5, 1].forEach((ratio) => {
      const gy = PAD_TOP + ratio * (H - PAD_TOP - PAD_BOTTOM);
      chart.appendChild(svg("line", { x1: PAD_X, x2: W - PAD_X, y1: gy, y2: gy, stroke: "currentColor", "stroke-opacity": "0.12", "stroke-width": "1" }));
    });

    // Step line: a price holds until the next change; out-of-stock spans are dashed grey.
    points.forEach((point, index) => {
      const next = points[index + 1];
      const x1 = x(point.t);
      const x2 = next ? x(next.t) : x(last);
      const y1 = y(point.price);
      let d = "M" + x1 + "," + y1 + " H" + x2;
      if (next) d += " V" + y(next.price);
      chart.appendChild(svg("path", {
        d,
        fill: "none",
        stroke: point.inStock ? "#0069FD" : "#a1a1aa",
        "stroke-width": "2.5",
        "stroke-linejoin": "round",
        "stroke-dasharray": point.inStock ? "" : "5 4",
      }));
      chart.appendChild(svg("circle", { cx: x1, cy: y1, r: "3", fill: point.inStock ? "#0069FD" : "#a1a1aa" }));
    });

    const label = (text, lx, anchor) => {
      const node = svg("text", { x: lx, y: H - 6, "font-size": "11", fill: "currentColor", "fill-opacity": "0.55", "text-anchor": anchor });
      node.textContent = text;
      chart.appendChild(node);
    };
    label(points[0].date, PAD_X, "start");
    label("امروز", W - PAD_X, "end");

    const cursor = svg("line", { y1: PAD_TOP, y2: H - PAD_BOTTOM, stroke: "#0069FD", "stroke-opacity": "0.4", visibility: "hidden" });
    chart.appendChild(cursor);
    const tip = el("div", "pointer-events-none absolute top-0 hidden -translate-x-1/2 whitespace-nowrap rounded-lg bg-zinc-800 px-2 py-1 text-center text-[11px] text-white shadow");
    tip.setAttribute("dir", "rtl");

    const show = (clientX) => {
      const rect = chart.getBoundingClientRect();
      const t = first + ((clientX - rect.left) / rect.width * W - PAD_X) / (W - PAD_X * 2) * span;
      let current = points[0];
      points.forEach((point) => { if (point.t <= t) current = point; });
      const cx = Math.min(Math.max((clientX - rect.left) / rect.width * W, PAD_X), W - PAD_X);
      cursor.setAttribute("x1", cx);
      cursor.setAttribute("x2", cx);
      cursor.setAttribute("visibility", "visible");
      tip.textContent = "";
      tip.appendChild(el("div", null, current.date));
      tip.appendChild(el("div", "font-semibold", current.inStock ? toman(current.price) : "ناموجود"));
      tip.style.left = Math.min(Math.max(clientX - rect.left, 50), rect.width - 50) + "px";
      tip.classList.remove("hidden");
    };
    const hide = () => {
      cursor.setAttribute("visibility", "hidden");
      tip.classList.add("hidden");
    };
    chart.addEventListener("pointermove", (event) => show(event.clientX));
    chart.addEventListener("pointerdown", (event) => show(event.clientX));
    chart.addEventListener("pointerleave", hide);

    wrap.append(chart, tip);
    box.appendChild(wrap);
    box.appendChild(el("p", "mt-2 text-[11px] text-zinc-400",
      "قیمت‌ها پس از تخفیف و مربوط به " + Number(data.days).toLocaleString("fa-IR") + " روز گذشته است؛ خط‌چین یعنی کالا ناموجود بوده."));
  };

  document.querySelectorAll("[data-price-history]").forEach((details) => {
    let loaded = false;
    details.addEventListener("toggle", () => {
      if (!details.open || loaded) return;
      loaded = true;
      const box = details.querySelector("[data-price-chart]");
      fetch("/api/products/" + encodeURIComponent(details.dataset.priceHistory) + "/price-history", { headers: { Accept: "application/json" } })
        .then((response) => (response.ok ? response.json() : Promise.reject()))
        .then((data) => drawChart(box, data))
        .catch(() => {
          loaded = false;
          box.textContent = "";
          box.appendChild(el("p", "text-xs text-red-400", "نمودار بارگذاری نشد؛ دوباره باز کنید."));
        });
    });
  });

  // ── Helpful votes ────────────────────────────────────────────────────
  document.addEventListener("click", (event) => {
    const button = event.target.closest("[data-vote] [data-vote-value]");
    if (!button) return;
    const box = button.closest("[data-vote]");
    if (box.dataset.busy) return;
    box.dataset.busy = "1";
    post("/api/comments/" + encodeURIComponent(box.dataset.vote) + "/vote", { helpful: button.dataset.voteValue === "true" })
      .then((data) => {
        box.querySelector('[data-vote-count="helpful"]').textContent = data.helpful;
        box.querySelector('[data-vote-count="notHelpful"]').textContent = data.notHelpful;
        box.querySelectorAll("[data-vote-value]").forEach((item) => {
          const pressed = data.myVote !== null && String(data.myVote) === item.dataset.voteValue;
          item.setAttribute("aria-pressed", pressed ? "true" : "false");
        });
      })
      .catch(() => { /* already shown */ })
      .finally(() => { delete box.dataset.busy; });
  });

  // ── Customer answers ─────────────────────────────────────────────────
  document.querySelectorAll("form[data-answer-form]").forEach((form) => {
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      const status = form.querySelector("[data-answer-status]");
      const button = form.querySelector('button[type="submit"]');
      const content = form.elements.content.value.trim();
      if (content.length < 5) {
        if (status) status.textContent = "پاسخ کوتاه است.";
        return;
      }
      button.disabled = true;
      post("/api/comments/" + encodeURIComponent(form.dataset.answerForm) + "/answer", { content })
        .then((data) => {
          const done = el("p", "mt-2 rounded-lg bg-emerald-50 p-2 text-xs text-emerald-700", data.message || "پاسخ شما ثبت شد و پس از بررسی نمایش داده می‌شود.");
          form.replaceWith(done);
        })
        .catch(() => { button.disabled = false; });
    });
  });
})();

/* Browser push notifications (Web Push + VAPID). The service worker is /sw.js. */
(function () {
  "use strict";
  const supported = "serviceWorker" in navigator && "PushManager" in window && "Notification" in window && window.isSecureContext;
  const SUGGESTED_KEY = "fratak_push_suggested";

  let config = null;
  const loadConfig = () => {
    if (!config) {
      config = fetch("/api/push/config", { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } })
        .then((response) => (response.ok ? response.json() : { enabled: false }))
        .catch(() => ({ enabled: false }));
    }
    return config;
  };

  const keyBytes = (base64) => {
    const padded = (base64 + "===".slice((base64.length + 3) % 4)).replace(/-/g, "+").replace(/_/g, "/");
    const raw = atob(padded);
    const bytes = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
    return bytes;
  };

  const registration = () =>
    navigator.serviceWorker.getRegistration("/").then((existing) => existing || navigator.serviceWorker.register("/sw.js", { scope: "/" }))
      .then(() => navigator.serviceWorker.ready);

  const currentSubscription = () => registration().then((reg) => reg.pushManager.getSubscription());

  const enable = () =>
    loadConfig().then((cfg) => {
      if (!cfg.enabled) return Promise.reject(new Error("disabled"));
      return Notification.requestPermission().then((permission) => {
        if (permission !== "granted") return Promise.reject(new Error("denied"));
        return registration()
          .then((reg) => reg.pushManager.getSubscription().then((existing) =>
            existing || reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(cfg.publicKey) })))
          .then((subscription) => {
            const json = subscription.toJSON();
            return window.FratakApi.post("/api/push/subscribe", { endpoint: json.endpoint, keys: json.keys });
          });
      });
    });

  const disable = () =>
    currentSubscription().then((subscription) => {
      if (!subscription) return null;
      const endpoint = subscription.endpoint;
      return subscription.unsubscribe().then(() => window.FratakApi.post("/api/push/unsubscribe", { endpoint }));
    });

  // Gentle suggestion after the first «خبرم کن» (once per browser).
  const suggest = () => {
    if (!supported || Notification.permission !== "default" || !window.FratakModal) return;
    try {
      if (localStorage.getItem(SUGGESTED_KEY)) return;
      localStorage.setItem(SUGGESTED_KEY, "1");
    } catch (_) { return; }
    loadConfig().then((cfg) => {
      if (!cfg.enabled || !cfg.signedIn) return;
      window.FratakModal.confirm({
        title: "اعلان مرورگر",
        message: "می‌خواهید خبرها (موجود شدن، کاهش قیمت، وضعیت سفارش) را به‌صورت اعلان مرورگر هم دریافت کنید؟",
        okText: "بله، فعال شود",
        cancelText: "فعلاً نه",
      }).then((accepted) => {
        if (accepted) enable().then(() => window.FratakModal.toast("اعلان مرورگر فعال شد.", "success")).catch(() => {});
      });
    });
  };

  window.FratakPush = { supported, enable, disable, suggest };

  // Notifications page: on/off switch.
  const box = document.querySelector("[data-push]");
  if (!box) return;
  const button = box.querySelector("[data-push-toggle]");
  const status = box.querySelector("[data-push-status]");
  const say = (text) => { if (status) status.textContent = text; };
  const render = (on) => {
    button.setAttribute("aria-pressed", on ? "true" : "false");
    button.textContent = on ? "غیرفعال کردن" : "فعال کردن";
    say(on ? "اعلان مرورگر روی این دستگاه فعال است." : "اعلان مرورگر روی این دستگاه فعال نیست.");
  };

  if (!supported) {
    button.disabled = true;
    say("مرورگر شما از اعلان پشتیبانی نمی‌کند (در آیفون ابتدا سایت را به صفحه اصلی اضافه کنید).");
    return;
  }

  loadConfig().then((cfg) => {
    if (!cfg.enabled) {
      button.disabled = true;
      say("اعلان مرورگر در حال حاضر فعال نیست.");
      return;
    }
    if (Notification.permission === "denied") {
      button.disabled = true;
      say("اجازه اعلان در تنظیمات مرورگر مسدود شده است.");
      return;
    }
    box.hidden = false;
    currentSubscription().then((subscription) => render(!!subscription && Notification.permission === "granted")).catch(() => render(false));

    button.addEventListener("click", () => {
      const on = button.getAttribute("aria-pressed") === "true";
      button.disabled = true;
      (on ? disable() : enable())
        .then(() => {
          render(!on);
          window.FratakModal && window.FratakModal.toast(on ? "اعلان مرورگر غیرفعال شد." : "اعلان مرورگر فعال شد.", "success");
        })
        .catch((error) => {
          if (error && error.message === "denied") say("اجازه نمایش اعلان داده نشد.");
        })
        .finally(() => { button.disabled = false; });
    });
  });
})();
