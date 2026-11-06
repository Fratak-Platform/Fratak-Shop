/*
 * admin-edit.js – ابزارهای فرم‌های ویرایش پنل (مرحلهٔ هفتم – فاز ۳)
 * ------------------------------------------------------------------
 *  ۱) حضور زنده: form[data-edit-key="product:…"]
 *     با SignalR (live.js) اعلام می‌کند «من این رکورد را باز کرده‌ام» و اگر همکار دیگری هم همین صفحه را باز کند،
 *     نوار زرد بالای فرم نامش را نشان می‌دهد. اگر او ذخیره کند، هشدار «نسخهٔ شما قدیمی است» می‌آید.
 *     (سمت سرور هم با فیلد مخفی __stamp جلوی بازنویسی گرفته می‌شود – EditSupport.cs.)
 *
 *  ۲) پیش‌نویس خودکار: form[data-autosave="کلید"][data-autosave-stamp][data-autosave-title][data-autosave-ignore="Input.Images,…"]
 *     هنگام تایپ، چند ثانیه بعد از آخرین تغییر مقدارهای فرم به /admin/ux?handler=Draft فرستاده می‌شود.
 *     دفعهٔ بعد که فرم باز شود و پیش‌نویس با مقدارهای فعلی فرق داشته باشد، پیشنهاد «بازیابی» یا «مقایسه و ادغام» داده می‌شود.
 *
 *  ۳) ادغام سه‌طرفه: پیش‌نویس دو نسخه دارد: base (فرم هنگام باز شدن) و mine (نوشته‌های کارمند).
 *     با نسخهٔ فعلی سرور (theirs) مقایسه می‌شود:
 *       - فقط من تغییر داده‌ام          ← مقدار من
 *       - فقط دیگری تغییر داده           ← مقدار او دست نمی‌خورد
 *       - هر دو، متفاوت (تعارض)          ← کارمند در پنجرهٔ ادغام انتخاب می‌کند
 *     مثال: من «قیمت» را عوض کرده‌ام و همکار «توضیحات» را ← هر دو تغییر بدون تعارض حفظ می‌شوند.
 *
 * قالب داده: { v:1, base:{values:{name:[…]}, checks:{name:[…]}}, mine:{…} }
 *   values = مقدار ورودی‌های متنی/انتخابی به ترتیب، checks = مقدار چک‌باکس/رادیوهای تیک‌خورده.
 * وابستگی‌ها: live.js (اختیاری)، modal.js، fa-inputs.js (فیلدهای مبلغ)، editor.js (Jodit).
 */
(function () {
  "use strict";

  const $ = (selector, root) => (root || document).querySelector(selector);
  const $$ = (selector, root) => Array.from((root || document).querySelectorAll(selector));
  const csrf = () => ($('meta[name="csrf-token"]') || {}).content || "";
  const toast = (message, type) => (window.FratakModal && window.FratakModal.toast ? window.FratakModal.toast(message, type) : null);
  const escapeHtml = (text) => String(text == null ? "" : text).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  const timeText = (date) => new Intl.DateTimeFormat("fa-IR", { hour: "2-digit", minute: "2-digit" }).format(date);

  const SYSTEM_FIELDS = ["__RequestVerificationToken", "__stamp", "__force", "after", "intent"];

  // ── خواندن و نوشتن مقدارهای فرم ─────────────────────────────────────────
  function isIgnored(name, ignore) {
    return SYSTEM_FIELDS.includes(name) || ignore.some((p) => name === p || name.startsWith(p + ".") || name.startsWith(p + "["));
  }

  function fields(form, ignore) {
    return Array.from(form.elements).filter((el) => el.name && !el.disabled && !isIgnored(el.name, ignore)
      && !["file", "submit", "button", "reset", "password", "image"].includes(el.type));
  }

  /** عکس فوری فرم: { values: {name:[…]}, checks: {name:[…]} } */
  function snapshot(form, ignore) {
    const values = {};
    const checks = {};
    fields(form, ignore).forEach((el) => {
      if (el.type === "checkbox" || el.type === "radio") {
        (checks[el.name] = checks[el.name] || []);
        if (el.checked) checks[el.name].push(el.value);
        return;
      }
      // ورودی مخفی کنار چک‌باکس هم‌نام (الگوی ASP.NET برای false) جزو مقدار نیست.
      if (el.type === "hidden" && form.querySelector(`input[type=checkbox][name="${CSS.escape(el.name)}"]`)) return;
      // متن ویرایشگر دیداری با هر تغییر در textarea نوشته می‌شود (editor.js)؛ پس همان value کافی است.
      const value = el.tagName === "SELECT" && el.multiple
        ? Array.from(el.selectedOptions).map((o) => o.value).join("\u0001") : el.value;
      (values[el.name] = values[el.name] || []).push(value);
    });
    return { values, checks };
  }

  const entryOf = (snap, name) => JSON.stringify([(snap.values || {})[name] || null, (snap.checks || {})[name] || null]);
  const namesOf = (...snaps) => Array.from(new Set(snaps.flatMap((s) => Object.keys((s && s.values) || {}).concat(Object.keys((s && s.checks) || {})))));

  function fire(el) {
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));
  }

  /** نوشتن مقدار یک فیلد (همهٔ ورودی‌های هم‌نام) از روی یک عکس. */
  function applyField(form, ignore, snap, name) {
    const values = ((snap.values || {})[name] || []).slice();
    const checks = (snap.checks || {})[name] || [];
    fields(form, ignore).filter((el) => el.name === name).forEach((el) => {
      if (el.type === "checkbox" || el.type === "radio") {
        const next = checks.includes(el.value);
        if (el.checked !== next) { el.checked = next; fire(el); }
        return;
      }
      if (el.type === "hidden" && form.querySelector(`input[type=checkbox][name="${CSS.escape(el.name)}"]`)) return;
      if (!values.length) return;
      const value = values.shift();
      // فیلد مبلغ (fa-inputs.js): ورودی قابل‌مشاهده بی‌نام است و عدد خام در ورودی مخفی هم‌نام.
      const visible = el.type === "hidden" && el.previousElementSibling && el.previousElementSibling.dataset
        && el.previousElementSibling.dataset.moneyName === name ? el.previousElementSibling : null;
      if (visible) { visible.value = value; fire(visible); return; }
      if (el.__fratakEditor) el.__fratakEditor.value = value;
      if (el.tagName === "SELECT" && el.multiple) {
        const chosen = value.split("\u0001");
        Array.from(el.options).forEach((o) => { o.selected = chosen.includes(o.value); });
      } else {
        el.value = value;
      }
      fire(el);
    });
  }

  /** برچسب خوانای یک فیلد برای پنجرهٔ ادغام. */
  function labelOf(form, name) {
    const el = fields(form, []).find((x) => x.name === name);
    if (!el) return name;
    const byFor = el.id && form.querySelector(`label[for="${CSS.escape(el.id)}"]`);
    if (byFor) return byFor.textContent.trim();
    const wrap = el.closest(".a-field, label");
    const label = wrap && (wrap.matches("label") ? wrap : $(".a-label, label", wrap));
    return (label && label.textContent.trim()) || name;
  }

  /** پیش‌نمایش کوتاه یک مقدار (متن ساده؛ برای فهرست‌ها متن گزینه، برای چک‌باکس بله/خیر). */
  function preview(form, snap, name) {
    const values = (snap.values || {})[name];
    const checks = (snap.checks || {})[name];
    if (checks && !values) return checks.length ? "بله" : "خیر";
    const el = fields(form, []).find((x) => x.name === name);
    let text = (values || []).join(" | ");
    if (el && el.tagName === "SELECT") {
      const option = Array.from(el.options).find((o) => o.value === text);
      if (option) text = option.textContent.trim();
    }
    const tmp = document.createElement("div");
    tmp.innerHTML = text; // فقط برای حذف برچسب‌ها؛ هرگز در صفحه درج نمی‌شود.
    text = (tmp.textContent || "").replace(/\s+/g, " ").trim();
    if (!text) return "∅ (خالی)";
    return text.length > 220 ? text.slice(0, 219) + "…" : text;
  }

  // ── ۱) حضور زنده و هشدار ذخیرهٔ هم‌زمان ────────────────────────────────
  $$("form[data-edit-key]").forEach((form) => {
    const key = form.dataset.editKey;
    const me = form.dataset.editMe || "";
    if (!key) return;

    const bar = document.createElement("div");
    bar.className = "a-presence";
    bar.setAttribute("role", "status");
    bar.hidden = true;
    form.prepend(bar);

    let others = [];
    let savedBy = null;
    const render = () => {
      if (savedBy) {
        bar.className = "a-presence is-stale";
        bar.innerHTML = `<b>${escapeHtml(savedBy)}</b> همین حالا این مورد را ذخیره کرد؛ نسخهٔ این صفحه قدیمی است. `
          + `تغییرات شما در پیش‌نویس خودکار می‌ماند: صفحه را تازه کنید و «مقایسه و ادغام» را بزنید. `
          + `<button type="button" class="a-btn a-btn-xs" data-presence-reload>تازه کردن صفحه</button>`;
        bar.hidden = false;
        return;
      }
      if (!others.length) { bar.hidden = true; return; }
      const names = others.map((o) => `<b>${escapeHtml(o.name)}</b>`).join("، ");
      bar.className = "a-presence";
      bar.innerHTML = `${names} هم‌اکنون همین صفحه را باز کرده${others.length > 1 ? "‌اند" : " است"}. اگر هم‌زمان ذخیره کنید، ذخیرهٔ دوم متوقف و هشدار داده می‌شود.`;
      bar.hidden = false;
    };

    bar.addEventListener("click", (e) => {
      if (!e.target.closest("[data-presence-reload]")) return;
      // پیش‌نویس پیش از تازه کردن ذخیره شود.
      document.dispatchEvent(new CustomEvent("fratak:flush-draft"));
      setTimeout(() => window.location.reload(), 600);
    });

    document.addEventListener("fratak:editors", (e) => {
      const data = e.detail || {};
      if (data.key !== key) return;
      if (data.saved) {
        if (String(data.savedBy || "") === me) return; // ذخیرهٔ خودم (مثلاً در تب دیگر بعد از بازگردانی) هشدار ندارد.
        savedBy = data.saved;
      } else if (Array.isArray(data.editors)) {
        others = data.editors.filter((x) => String(x.id) !== me);
      }
      render();
    });

    const join = () => window.FratakLive && window.FratakLive.joinEdit(key);
    if (window.FratakLive) join(); else window.addEventListener("load", join, { once: true });
    window.addEventListener("pagehide", () => window.FratakLive && window.FratakLive.leaveEdit(key));
  });

  // ── ۲ و ۳) پیش‌نویس خودکار و ادغام ──────────────────────────────────────
  $$("form[data-autosave]").forEach((form) => {
    const key = form.dataset.autosave;
    if (!key) return;
    const stamp = form.dataset.autosaveStamp || "0";
    const ignore = (form.dataset.autosaveIgnore || "").split(",").map((s) => s.trim()).filter(Boolean);
    const DEBOUNCE = 3000;
    const MAX_WAIT = 15000;

    let base = null;      // فرم هنگام باز شدن (= نسخهٔ فعلی سرور)
    let dirty = false;
    let submitting = false;
    let timer = 0;
    let firstChange = 0;
    let lastSent = "";

    // وضعیت ذخیره کنار دکمه‌های پایین فرم.
    const status = document.createElement("span");
    status.className = "a-autosave-status";
    status.setAttribute("aria-live", "polite");
    const submit = form.querySelector("button[type=submit], button:not([type])");
    const host = $(".a-actionbar", form) || (submit && submit.parentElement);
    if (host) host.prepend(status);

    const send = () => {
      clearTimeout(timer);
      timer = 0;
      if (!dirty || submitting || !base) return Promise.resolve();
      const mine = snapshot(form, ignore);
      const data = JSON.stringify({ v: 1, base, mine });
      if (data === lastSent) { dirty = false; return Promise.resolve(); }
      const body = new FormData();
      body.append("key", key);
      body.append("stamp", stamp);
      body.append("title", form.dataset.autosaveTitle || document.title);
      body.append("url", location.pathname + location.search);
      body.append("data", data);
      status.textContent = "در حال ذخیرهٔ پیش‌نویس…";
      return fetch("/admin/ux?handler=Draft", {
        method: "POST", body, credentials: "same-origin",
        headers: { "X-Requested-With": "fetch", RequestVerificationToken: csrf(), Accept: "application/json" },
      })
        .then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))))
        .then((res) => {
          if (res.ok) {
            lastSent = data;
            dirty = false;
            firstChange = 0;
            status.textContent = `پیش‌نویس ذخیره شد ${timeText(new Date())}`;
          } else {
            status.textContent = res.message || "پیش‌نویس ذخیره نشد.";
          }
        })
        .catch(() => { status.textContent = "پیش‌نویس ذخیره نشد (اتصال)."; });
    };

    const schedule = () => {
      if (!base || submitting) return;
      dirty = true;
      const now = Date.now();
      if (!firstChange) firstChange = now;
      clearTimeout(timer);
      // حداکثر هر ۱۵ ثانیه یک بار حتی اگر کاربر بی‌وقفه تایپ کند.
      timer = setTimeout(send, now - firstChange > MAX_WAIT ? 0 : DEBOUNCE);
    };

    form.addEventListener("input", schedule);
    form.addEventListener("change", schedule);
    form.addEventListener("submit", () => { submitting = true; clearTimeout(timer); });
    document.addEventListener("fratak:flush-draft", () => { send(); });
    document.addEventListener("visibilitychange", () => { if (document.visibilityState === "hidden") send(); });

    // ── بازیابی ──
    const banner = document.createElement("div");
    banner.className = "a-alert a-alert-info a-draft-banner";
    banner.hidden = true;
    form.prepend(banner);

    function plan(draft, theirs) {
      // تصمیم‌گیری سه‌طرفه برای هر فیلد.
      const result = { mineOnly: [], conflicts: [], theirsOnly: 0 };
      namesOf(draft.base, draft.mine, theirs).forEach((name) => {
        const b = entryOf(draft.base, name);
        const m = entryOf(draft.mine, name);
        const t = entryOf(theirs, name);
        if (m === t) return;
        if (m === b) { result.theirsOnly++; return; }
        if (t === b) result.mineOnly.push(name); else result.conflicts.push(name);
      });
      return result;
    }

    function applyChoices(draft, names) {
      names.forEach((name) => applyField(form, ignore, draft.mine, name));
      dirty = true;
      schedule();
    }

    function discard() {
      banner.hidden = true;
      const body = new FormData();
      body.append("key", key);
      fetch("/admin/ux?handler=DeleteDraft", {
        method: "POST", body, credentials: "same-origin",
        headers: { "X-Requested-With": "fetch", RequestVerificationToken: csrf(), Accept: "application/json" },
      }).catch(() => {});
    }

    function openMerge(draft, theirs, decision) {
      const rows = decision.mineOnly.map((n) => ({ name: n, conflict: false })).concat(decision.conflicts.map((n) => ({ name: n, conflict: true })));
      const box = document.createElement("div");
      box.className = "a-merge";
      box.setAttribute("role", "dialog");
      box.setAttribute("aria-modal", "true");
      box.setAttribute("aria-label", "مقایسه و ادغام نسخه‌ها");
      box.innerHTML = `
        <div class="a-merge-backdrop" data-merge-close></div>
        <div class="a-merge-box">
          <div class="a-merge-head">
            <b>مقایسه و ادغام</b>
            <button type="button" class="a-icon-btn" data-merge-close aria-label="بستن">✕</button>
          </div>
          <p class="a-muted text-sm px-4">برای هر فیلد انتخاب کنید کدام نسخه در فرم بماند. ${decision.theirsOnly ? `${decision.theirsOnly.toLocaleString("fa-IR")} فیلد را فقط دیگران تغییر داده‌اند و همان می‌ماند.` : ""}</p>
          <div class="a-merge-list">
            ${rows.map((r, i) => `
              <fieldset class="a-merge-row ${r.conflict ? "is-conflict" : ""}">
                <legend>${escapeHtml(labelOf(form, r.name))} ${r.conflict ? '<span class="a-badge a-badge-red">هر دو تغییر داده‌اند</span>' : ""}</legend>
                <label class="a-merge-option"><input type="radio" name="m${i}" value="theirs"> <span><small>نسخهٔ ذخیره‌شدهٔ فعلی</small><span dir="auto">${escapeHtml(preview(form, theirs, r.name))}</span></span></label>
                <label class="a-merge-option"><input type="radio" name="m${i}" value="mine" checked> <span><small>نسخهٔ شما (پیش‌نویس)</small><span dir="auto">${escapeHtml(preview(form, draft.mine, r.name))}</span></span></label>
              </fieldset>`).join("")}
          </div>
          <div class="a-merge-foot">
            <button type="button" class="a-btn a-btn-light" data-merge-close>انصراف</button>
            <button type="button" class="a-btn" data-merge-apply>اعمال در فرم</button>
          </div>
        </div>`;
      document.body.appendChild(box);
      document.documentElement.classList.add("a-no-scroll");
      const close = () => { box.remove(); document.documentElement.classList.remove("a-no-scroll"); };
      box.addEventListener("click", (e) => {
        if (e.target.closest("[data-merge-close]")) close();
        if (e.target.closest("[data-merge-apply]")) {
          const chosen = rows.filter((r, i) => (box.querySelector(`input[name="m${i}"]:checked`) || {}).value === "mine").map((r) => r.name);
          applyChoices(draft, chosen);
          close();
          banner.hidden = true;
          toast(chosen.length ? `${chosen.length.toLocaleString("fa-IR")} فیلد از پیش‌نویس در فرم قرار گرفت؛ برای ثبت نهایی «ذخیره» را بزنید.` : "فرم بدون تغییر ماند.", "success");
        }
      });
      box.addEventListener("keydown", (e) => { if (e.key === "Escape") close(); });
      const first = box.querySelector("input");
      if (first) first.focus();
    }

    function offer(draft, savedAt, changedByOthers) {
      const theirs = base;
      const decision = plan(draft, theirs);
      const total = decision.mineOnly.length + decision.conflicts.length;
      if (!total) return; // پیش‌نویس چیزی بیش از نسخهٔ فعلی ندارد.
      const when = savedAt ? new Date(savedAt) : null;
      banner.innerHTML = `
        <div class="flex-1">
          <b>پیش‌نویس ذخیره‌نشده</b>${when && !isNaN(when) ? ` از ${new Intl.DateTimeFormat("fa-IR", { dateStyle: "medium", timeStyle: "short" }).format(when)}` : ""}:
          ${total.toLocaleString("fa-IR")} فیلد با نسخهٔ فعلی فرق دارد.
          ${changedByOthers ? `<br><span class="text-sm">این مورد بعد از پیش‌نویس شما توسط دیگران هم تغییر کرده است${decision.conflicts.length ? ` و ${decision.conflicts.length.toLocaleString("fa-IR")} فیلد را هر دو تغییر داده‌اید` : ""}؛ «مقایسه و ادغام» را بزنید تا کار هیچ‌کس پاک نشود.</span>` : ""}
        </div>
        <div class="a-btn-group">
          <button type="button" class="a-btn a-btn-sm" data-draft-merge>مقایسه و ادغام</button>
          ${decision.conflicts.length ? "" : '<button type="button" class="a-btn a-btn-sm a-btn-light" data-draft-restore>بازیابی</button>'}
          <button type="button" class="a-btn a-btn-sm a-btn-light" data-draft-discard>دور انداختن</button>
        </div>`;
      banner.hidden = false;
      banner.addEventListener("click", (e) => {
        if (e.target.closest("[data-draft-merge]")) openMerge(draft, theirs, decision);
        else if (e.target.closest("[data-draft-restore]")) {
          applyChoices(draft, decision.mineOnly);
          banner.hidden = true;
          toast("پیش‌نویس در فرم قرار گرفت؛ برای ثبت نهایی «ذخیره» را بزنید.", "success");
        } else if (e.target.closest("[data-draft-discard]")) {
          if (window.FratakModal && window.FratakModal.confirm) {
            window.FratakModal.confirm({ title: "دور انداختن پیش‌نویس", message: "نوشته‌های ذخیره‌نشدهٔ این پیش‌نویس پاک می‌شود. ادامه می‌دهید؟", okText: "دور انداختن", danger: true })
              .then((ok) => { if (ok) discard(); });
          } else {
            discard();
          }
        }
      });
    }

    // عکس «نسخهٔ فعلی» بعد از آماده شدن ورودی‌های مبلغ و تاریخ (fa-inputs) گرفته می‌شود.
    const init = () => {
      base = snapshot(form, ignore);
      fetch(`/admin/ux?handler=Draft&key=${encodeURIComponent(key)}`, { credentials: "same-origin", headers: { Accept: "application/json", "X-Requested-With": "fetch" } })
        .then((r) => (r.ok ? r.json() : null))
        .then((res) => {
          if (!res || !res.ok || !res.data) return;
          let draft;
          try { draft = JSON.parse(res.data.data); } catch { return; }
          if (!draft || !draft.mine || !draft.base) return;
          offer(draft, res.data.savedAt, String(res.data.stamp) !== String(stamp));
        })
        .catch(() => {});
    };
    setTimeout(init, 50);
  });
})();
