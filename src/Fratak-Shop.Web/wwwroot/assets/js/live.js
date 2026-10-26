/*
 * live.js – اتصال زندهٔ سایت و پنل مدیریت (SignalR، آدرس /hubs/live)
 *
 * چه می‌کند؟
 *  - اعلان شخصی مشتری («notification») و اعلان داخلی کارکنان («staff») را بدون رفرش نشان می‌دهد:
 *    یک پیام کوچک (toast) + صدای کوتاه + به‌روزرسانی شمارندهٔ زنگوله؛
 *  - اگر تب در پس‌زمینه باشد و اجازهٔ اعلان مرورگر داده شده باشد، اعلان سیستمی هم نشان می‌دهد؛
 *  - «refresh»: شمارندهٔ اعلان‌ها را از سرور دوباره می‌گیرد (اعلان همگانیِ گروهی)؛
 *  - «editors»: فهرست کسانی که هم‌زمان یک رکورد را ویرایش می‌کنند (رویداد DOM به نام fratak:editors).
 *
 * کجا بارگذاری می‌شود؟
 *  - سایت: فقط برای کاربر واردشده (_Layout.cshtml، body با data-realtime="1")؛
 *  - پنل: همیشه (_AdminLayout.cshtml).
 *  پیش‌نیاز: dependencies/signalr/signalr.min.js قبل از این فایل.
 *
 * نشانه‌گذاری HTML که این فایل می‌شناسد:
 *  - [data-live-count="notifications"] : شمارندهٔ زنگولهٔ سایت
 *  - [data-live-count="staff"]         : شمارندهٔ زنگولهٔ پنل
 *  - [data-staff-list]                 : فهرست اعلان‌های منوی زنگولهٔ پنل (آیتم جدید بالای آن اضافه می‌شود)
 *  - [data-sound-toggle]               : دکمهٔ روشن/خاموش صدای اعلان
 *
 * استفاده از کد دیگر:
 *  window.FratakLive.on("editors", (data) => …);   // گوش دادن به یک رویداد سرور
 *  window.FratakLive.joinEdit("product:<id>");       // اعلام «در حال ویرایش این رکورد هستم»
 */
(function () {
  "use strict";

  const body = document.body;
  if (!body || body.dataset.realtime !== "1" || !window.signalR) return;

  const isAdmin = body.classList.contains("a-body");
  const SOUND_KEY = "fratak_sound";
  const handlers = {};
  let connection = null;
  let audioContext = null;

  // ── صدا ────────────────────────────────────────────────────────────
  // صدا با Web Audio ساخته می‌شود (فایل صوتی لازم نیست). مرورگرها پخش صدا را فقط بعد از اولین
  // تعامل کاربر با صفحه (کلیک/لمس/کلید) اجازه می‌دهند؛ پس AudioContext بعد از اولین تعامل باز می‌شود.
  const soundEnabled = () => localStorage.getItem(SOUND_KEY) !== "off";

  function unlockAudio() {
    if (audioContext) return;
    const Ctx = window.AudioContext || window.webkitAudioContext;
    if (!Ctx) return;
    try {
      audioContext = new Ctx();
    } catch (_) {
      audioContext = null;
    }
  }
  ["pointerdown", "keydown", "touchstart"].forEach((type) =>
    window.addEventListener(type, unlockAudio, { once: true, passive: true }));

  /** «دینگ» دو نُتی کوتاه و ملایم. */
  function playChime() {
    if (!soundEnabled() || !audioContext) return;
    try {
      if (audioContext.state === "suspended") audioContext.resume();
      const now = audioContext.currentTime;
      [[880, 0], [1318.5, 0.12]].forEach(([frequency, offset]) => {
        const oscillator = audioContext.createOscillator();
        const gain = audioContext.createGain();
        oscillator.type = "sine";
        oscillator.frequency.value = frequency;
        gain.gain.setValueAtTime(0.0001, now + offset);
        gain.gain.exponentialRampToValueAtTime(0.18, now + offset + 0.02);
        gain.gain.exponentialRampToValueAtTime(0.0001, now + offset + 0.45);
        oscillator.connect(gain).connect(audioContext.destination);
        oscillator.start(now + offset);
        oscillator.stop(now + offset + 0.5);
      });
    } catch (_) { /* صدا اختیاری است */ }
  }

  function renderSoundToggles() {
    document.querySelectorAll("[data-sound-toggle]").forEach((button) => {
      const on = soundEnabled();
      button.setAttribute("aria-pressed", on ? "true" : "false");
      const label = button.querySelector("[data-sound-label]");
      if (label) label.textContent = on ? "صدای اعلان: روشن" : "صدای اعلان: خاموش";
    });
  }
  document.addEventListener("click", (event) => {
    const button = event.target.closest("[data-sound-toggle]");
    if (!button) return;
    event.preventDefault();
    localStorage.setItem(SOUND_KEY, soundEnabled() ? "off" : "on");
    unlockAudio();
    renderSoundToggles();
    if (soundEnabled()) playChime();
  });
  renderSoundToggles();

  // ── toast ──────────────────────────────────────────────────────────
  function toastContainer() {
    let box = document.getElementById("live-toasts");
    if (!box) {
      box = document.createElement("div");
      box.id = "live-toasts";
      box.className = "fixed bottom-4 left-4 z-[1000] flex w-[min(22rem,calc(100vw-2rem))] flex-col gap-2";
      box.setAttribute("role", "status");
      box.setAttribute("aria-live", "polite");
      body.appendChild(box);
    }
    return box;
  }

  /** فقط آدرس داخلی همین سایت قابل کلیک است. */
  function safeUrl(url) {
    return typeof url === "string" && url.startsWith("/") && !url.startsWith("//") ? url : null;
  }

  function showToast(data) {
    const box = toastContainer();
    const url = safeUrl(data.url);
    const item = document.createElement(url ? "a" : "div");
    if (url) item.href = url;
    item.className = "live-toast flex items-start gap-3 rounded-xl border border-sky-100 bg-white p-3 text-right text-sm text-zinc-700 shadow-lg transition duration-300";

    const dot = document.createElement("span");
    dot.className = "mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full bg-sky-500";
    const text = document.createElement("div");
    text.className = "min-w-0 flex-1";
    const title = document.createElement("p");
    title.className = "font-semibold text-zinc-800";
    title.textContent = data.title || "اعلان جدید";
    text.appendChild(title);
    if (data.body) {
      const bodyText = document.createElement("p");
      bodyText.className = "mt-0.5 line-clamp-2 text-xs text-zinc-500";
      bodyText.textContent = data.body;
      text.appendChild(bodyText);
    }
    const close = document.createElement("button");
    close.type = "button";
    close.className = "shrink-0 rounded p-0.5 text-zinc-400 hover:text-zinc-700";
    close.setAttribute("aria-label", "بستن");
    close.textContent = "×";
    close.addEventListener("click", (event) => {
      event.preventDefault();
      event.stopPropagation();
      remove();
    });

    item.append(dot, text, close);
    box.prepend(item);
    // حداکثر ۴ پیام هم‌زمان
    while (box.children.length > 4) box.lastElementChild.remove();

    let timer = setTimeout(remove, 8000);
    item.addEventListener("mouseenter", () => clearTimeout(timer));
    item.addEventListener("mouseleave", () => { timer = setTimeout(remove, 4000); });

    function remove() {
      clearTimeout(timer);
      item.classList.add("opacity-0");
      setTimeout(() => item.remove(), 300);
    }
  }

  // ── شمارنده‌ها ─────────────────────────────────────────────────────
  function setCount(name, value) {
    document.querySelectorAll(`[data-live-count="${name}"]`).forEach((badge) => {
      let next = value;
      if (typeof next !== "number") next = (parseInt(badge.dataset.value || "0", 10) || 0) + 1;
      badge.dataset.value = String(next);
      badge.textContent = next > 99 ? "99+" : String(next);
      badge.hidden = next <= 0;
    });
  }

  function refreshNotificationCount() {
    fetch("/api/notifications/unread", { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } })
      .then((response) => (response.ok ? response.json() : null))
      .then((json) => { if (json && typeof json.unread === "number") setCount("notifications", json.unread); })
      .catch(() => {});
  }

  // ── اعلان سیستمی وقتی تب پنهان است ─────────────────────────────────
  // (اگر پوش مرورگر فعال باشد سرور خودش پوش می‌فرستد؛ این فقط برای وقتی است که صفحه باز ولی پنهان است
  //  و از تکرار با tag یکسان جلوگیری می‌شود.)
  function systemNotify(data, tag) {
    if (!document.hidden || !("Notification" in window) || Notification.permission !== "granted" || !navigator.serviceWorker) return;
    navigator.serviceWorker.getRegistration("/").then((registration) => {
      if (!registration) return;
      registration.showNotification(data.title || "اعلان جدید", {
        body: data.body || "",
        icon: "/assets/image/pwa/icon-192.png",
        badge: "/assets/image/pwa/icon-192.png",
        tag,
        dir: "rtl",
        lang: "fa",
        data: { url: safeUrl(data.url) || "/" },
      });
    }).catch(() => {});
  }

  // ── پوش مرورگر (وقتی سایت/پنل بسته است) ───────────────────────────
  // مشکل‌های قبلی که اینجا حل می‌شوند:
  //  ۱) پیشنهاد فعال‌سازی فقط بعد از «خبرم کن» نشان داده می‌شد؛ پس بیشتر کاربران هرگز فعالش نمی‌کردند.
  //     حالا بعد از چند ثانیه گشت‌وگذار (حداکثر هر ۱۴ روز یک بار) پیشنهاد می‌شود.
  //  ۲) اشتراکی که سرور کنارش گذاشته بود (خطاهای پیاپی) یا به حساب دیگری وصل بود، دیگر هرگز تازه نمی‌شد.
  //     حالا اگر اجازه داده شده باشد، روزی یک بار اشتراک دوباره برای حساب فعلی ثبت می‌شود.
  //  ۳) کارکنان در پنل راهی برای فعال کردن پوش نداشتند ([data-push-enable] در منوی زنگولهٔ پنل).
  const pushSupported = "serviceWorker" in navigator && "PushManager" in window && "Notification" in window && window.isSecureContext;
  const PUSH_SYNC_KEY = "fratak_push_sync";
  const PUSH_ASK_KEY = "fratak_push_asked";

  function base64UrlToBytes(base64) {
    const padded = (base64 + "===".slice((base64.length + 3) % 4)).replace(/-/g, "+").replace(/_/g, "/");
    const raw = atob(padded);
    const bytes = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
    return bytes;
  }

  function getJson(url) {
    return fetch(url, { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } })
      .then((response) => (response.ok ? response.json() : Promise.reject(new Error(String(response.status)))));
  }

  /** آیا کلید عمومی اشتراک فعلی همان کلید VAPID فعلی سرور است؟ (اگر کلید سرور عوض شده باشد، پوش‌ها با 403 رد می‌شوند.) */
  function sameServerKey(subscription, publicKey) {
    const current = subscription && subscription.options && subscription.options.applicationServerKey;
    if (!current) return true; // مرورگرهای قدیمی این را نمی‌دهند؛ فرض می‌کنیم درست است.
    const a = new Uint8Array(current);
    const b = base64UrlToBytes(publicKey);
    if (a.length !== b.length) return false;
    for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
    return true;
  }

  /**
   * ثبت اشتراک این مرورگر برای حساب فعلی (اجازهٔ اعلان باید از قبل داده شده باشد).
   * مرحلهٔ هفتم – اصلاحیه («وقتی سایت بسته است اعلان نمی‌آید»):
   *  • اثر انگشت «endpoint|کلید سرور|حساب» ذخیره می‌شود؛ اگر هر کدام عوض شود (ورود با حساب دیگر، تعویض کلید VAPID،
   *    اشتراک تازهٔ مرورگر) همان لحظه دوباره ثبت می‌شود – قبلاً تا ۲۴ ساعت صبر می‌کرد.
   *  • اگر کلید سرور با کلید اشتراک فرق کند، اشتراک قدیمی لغو و اشتراک تازه ساخته می‌شود (وگرنه سرویس پوش همیشه 403 می‌داد).
   *  • بدون تغییر هم هر ۶ تا ۱۲ ساعت یک بار تازه می‌شود تا اشتراکی که سرور کنار گذاشته (خطاهای پیاپی) زنده شود.
   */
  function syncPush(force) {
    if (!pushSupported || Notification.permission !== "granted") return Promise.resolve(false);
    const stored = (localStorage.getItem(PUSH_SYNC_KEY) || "").split("~");
    const last = parseInt(stored[0] || "0", 10) || 0;
    const fingerprint = stored[1] || "";
    const period = (6 + Math.random() * 6) * 3600 * 1000;
    // کارایی: در یک زبانهٔ باز حداکثر هر ۱۰ دقیقه یک بار بررسی می‌شود (نه با هر بار باز شدن صفحه).
    const checkedAt = parseInt(sessionStorage.getItem(PUSH_SYNC_KEY) || "0", 10) || 0;
    if (!force && Date.now() - checkedAt < 10 * 60 * 1000) return Promise.resolve(true);
    try { sessionStorage.setItem(PUSH_SYNC_KEY, String(Date.now())); } catch (e) { /* حالت خصوصی */ }

    return getJson("/api/push/config").then((config) => {
      if (!config.enabled || !config.publicKey) return false;
      return navigator.serviceWorker.getRegistration("/")
        .then((existing) => existing || navigator.serviceWorker.register("/sw.js", { scope: "/" }))
        .then(() => navigator.serviceWorker.ready)
        .then((registration) => registration.pushManager.getSubscription()
          .then((subscription) => {
            if (subscription && !sameServerKey(subscription, config.publicKey)) {
              return subscription.unsubscribe().catch(() => false).then(() => null);
            }
            return subscription;
          })
          .then((subscription) => subscription || registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: base64UrlToBytes(config.publicKey) })))
        .then((subscription) => {
          const json = subscription.toJSON();
          const current = [json.endpoint, config.publicKey, config.account || ""].join("|");
          if (!force && current === fingerprint && Date.now() - last < period) return true;
          return getJson("/api/antiforgery").then((token) => fetch("/api/push/subscribe", {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json", Accept: "application/json", RequestVerificationToken: token.token },
            body: JSON.stringify({ endpoint: json.endpoint, keys: json.keys }),
          })).then((response) => {
            if (response.ok) localStorage.setItem(PUSH_SYNC_KEY, Date.now() + "~" + current);
            return response.ok;
          });
        });
    }).catch(() => false);
  }

  /** درخواست اجازه از کاربر و فعال‌سازی پوش. */
  function enablePush() {
    if (!pushSupported) return Promise.resolve("unsupported");
    return Notification.requestPermission().then((permission) => {
      if (permission !== "granted") return permission;
      return syncPush(true).then((ok) => (ok ? "granted" : "failed"));
    });
  }

  function renderPushButtons() {
    document.querySelectorAll("[data-push-enable]").forEach((button) => {
      const label = button.querySelector("[data-push-label]") || button;
      if (!pushSupported) {
        label.textContent = "اعلان مرورگر پشتیبانی نمی‌شود";
        button.disabled = true;
      } else if (Notification.permission === "granted") {
        label.textContent = "اعلان مرورگر: فعال";
      } else if (Notification.permission === "denied") {
        label.textContent = "اعلان مرورگر مسدود است (از تنظیمات مرورگر اجازه دهید)";
      } else {
        label.textContent = "فعال‌سازی اعلان مرورگر";
      }
    });
  }

  document.addEventListener("click", (event) => {
    const button = event.target.closest("[data-push-enable]");
    if (!button) return;
    event.preventDefault();
    enablePush().then((result) => {
      renderPushButtons();
      if (result === "granted") showToast({ title: "اعلان مرورگر فعال شد", body: "حتی وقتی صفحه بسته است، اعلان‌ها روی این دستگاه نمایش داده می‌شوند." });
      else if (result === "failed") showToast({ title: "فعال‌سازی ناموفق بود", body: "اعلان مرورگر در تنظیمات فروشگاه خاموش است یا اتصال برقرار نشد." });
    });
  });
  renderPushButtons();

  // پیشنهاد ملایم در سایت (نه پنل): ۲۰ ثانیه بعد از ورود به صفحه، حداکثر هر ۱۴ روز یک بار.
  function suggestPush() {
    if (isAdmin || !pushSupported || Notification.permission !== "default" || !window.FratakModal) return;
    const asked = parseInt(localStorage.getItem(PUSH_ASK_KEY) || "0", 10) || 0;
    if (Date.now() - asked < 14 * 24 * 3600 * 1000) return;
    setTimeout(() => {
      if (document.hidden) return;
      getJson("/api/push/config").then((config) => {
        if (!config.enabled) return;
        localStorage.setItem(PUSH_ASK_KEY, String(Date.now()));
        window.FratakModal.confirm({
          title: "اعلان‌های فروشگاه",
          message: "وضعیت سفارش، پاسخ تیکت و تخفیف‌ها را حتی وقتی سایت بسته است به‌صورت اعلان دریافت کنید؟",
          okText: "بله، فعال شود",
          cancelText: "فعلاً نه",
        }).then((accepted) => { if (accepted) enablePush().then(renderPushButtons); });
      }).catch(() => {});
    }, 20000);
  }

  // ── رویدادهای سرور ─────────────────────────────────────────────────
  function emit(method, data) {
    (handlers[method] || []).forEach((fn) => {
      try { fn(data); } catch (_) { /* خطای یک شنونده بقیه را خراب نکند */ }
    });
  }

  function onNotification(data) {
    showToast(data);
    playChime();
    setCount("notifications", typeof data.unread === "number" ? data.unread : undefined);
    systemNotify(data, "live-notification");
    emit("notification", data);
  }

  function onStaff(data) {
    showToast(data);
    playChime();
    setCount("staff", typeof data.unread === "number" ? data.unread : undefined);
    prependStaffItem(data);
    systemNotify(data, "live-staff");
    emit("staff", data);
  }

  /** افزودن اعلان تازه به بالای فهرست منوی زنگولهٔ پنل. */
  function prependStaffItem(data) {
    const list = document.querySelector("[data-staff-list]");
    if (!list) return;
    list.querySelector("[data-staff-empty]")?.remove();
    const url = safeUrl(data.url);
    const item = document.createElement("a");
    item.className = "a-bell-item is-unread";
    item.href = data.id ? `/admin/inbox?open=${encodeURIComponent(data.id)}` : (url || "/admin/inbox");
    const title = document.createElement("span");
    title.className = "a-bell-title";
    title.textContent = data.title || "";
    const meta = document.createElement("span");
    meta.className = "a-bell-meta";
    meta.textContent = (data.body ? data.body + " · " : "") + "همین حالا";
    item.append(title, meta);
    list.prepend(item);
    while (list.children.length > 8) list.lastElementChild.remove();
  }

  // ── اتصال ──────────────────────────────────────────────────────────
  function connect() {
    connection = new window.signalR.HubConnectionBuilder()
      .withUrl("/hubs/live")
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000, 60000])
      .configureLogging(window.signalR.LogLevel.None)
      .build();

    connection.on("notification", onNotification);
    connection.on("staff", onStaff);
    connection.on("refresh", (data) => {
      if (!data || data.scope === "notifications") refreshNotificationCount();
      emit("refresh", data);
    });
    connection.on("editors", (data) => {
      emit("editors", data);
      document.dispatchEvent(new CustomEvent("fratak:editors", { detail: data }));
    });

    // بعد از وصل دوباره، ممکن است در این فاصله اعلانی آمده باشد: شمارنده تازه می‌شود و
    // صفحه‌های ویرایش دوباره حضورشان را اعلام می‌کنند.
    connection.onreconnected(() => {
      if (!isAdmin) refreshNotificationCount();
      emit("reconnected", null);
    });
    // اگر همهٔ تلاش‌های خودکار شکست خورد، هر دقیقه دوباره امتحان می‌شود.
    connection.onclose(() => setTimeout(start, 60000));

    start();
  }

  function start() {
    if (!connection || connection.state !== window.signalR.HubConnectionState.Disconnected) return;
    connection.start().then(() => emit("connected", null)).catch(() => setTimeout(start, 30000));
  }

  const editing = new Set();

  window.FratakLive = {
    /** گوش دادن به یک رویداد: notification | staff | refresh | editors | connected | reconnected */
    on(method, fn) {
      (handlers[method] = handlers[method] || []).push(fn);
    },
    /** صدا زدن یک متد هاب (فقط وقتی وصل است). */
    invoke(method, ...args) {
      if (!connection || connection.state !== window.signalR.HubConnectionState.Connected) return Promise.resolve();
      return connection.invoke(method, ...args).catch(() => {});
    },
    /** اعلام حضور در صفحهٔ ویرایش یک رکورد (بعد از وصل شدن دوباره هم خودکار تکرار می‌شود). */
    joinEdit(key) {
      editing.add(key);
      return this.invoke("JoinEdit", key);
    },
    leaveEdit(key) {
      editing.delete(key);
      return this.invoke("LeaveEdit", key);
    },
    /** اعلام «ذخیره کردم» به بقیهٔ ویرایشگرهای همان رکورد. */
    saved(key) {
      return this.invoke("Saved", key);
    },
    toast: showToast,
    chime: playChime,
  };

  const rejoin = () => editing.forEach((key) => window.FratakLive.invoke("JoinEdit", key));
  window.FratakLive.on("connected", rejoin);
  window.FratakLive.on("reconnected", rejoin);

  connect();
  syncPush(false);
  suggestPush();
})();
