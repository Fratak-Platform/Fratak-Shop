/*
 * ابزارهای رابط کاربری پنل مدیریت (مرحلهٔ هفتم – P2). بدون وابستگی و سازگار با CSP.
 * محل بارگذاری: _AdminLayout.cshtml (بعد از admin.js و live.js).
 *
 *  ۱) جست‌وجوی سریع (Ctrl+K یا ⌘K یا دکمه‌های [data-palette-open]):
 *     صفحه‌های منو همان لحظه در مرورگر فیلتر می‌شوند و سفارش/مشتری/محصول/تیکت از
 *     GET /admin/ux?handler=Search&q=… گرفته می‌شود (سرور بر اساس مجوز کارمند محدود می‌کند).
 *     بدون عبارت: سنجاق‌ها و صفحه‌های اخیر (از body[data-palette]) نمایش داده می‌شود.
 *  ۲) سنجاق ([data-pin]): ستارهٔ نوار بالا → POST /admin/ux?handler=Pin
 *  ۳) فیلترهای ذخیره‌شده: بالای اولین فرم GET صفحه چیپ‌های فیلتر (main[data-saved-filters]) و دکمهٔ
 *     «ذخیرهٔ این فیلتر» ساخته می‌شود → POST ?handler=Filter / ?handler=DeleteFilter
 *  ۴) کشوی راهنما ([data-help-open] / [data-help-drawer]) – میانبر: کلید «?»
 *  ۵) حالت تیره ([data-theme-toggle]) – در کوکی fratak_admin_theme ذخیره می‌شود تا سرور همان را رندر کند.
 */
(function () {
  "use strict";

  const $ = (selector, root) => (root || document).querySelector(selector);
  const $$ = (selector, root) => Array.from((root || document).querySelectorAll(selector));
  const csrf = () => ($('meta[name="csrf-token"]') || {}).content || "";
  const toast = (message, type) => (window.FratakModal && window.FratakModal.toast ? window.FratakModal.toast(message, type) : null);

  /** ارسال فرم کوچک با توکن ضد جعل؛ خروجی JSON سرور ({ ok, message, data }). */
  function post(handler, fields) {
    const body = new FormData();
    Object.entries(fields).forEach(([key, value]) => body.append(key, value == null ? "" : String(value)));
    return fetch(`/admin/ux?handler=${handler}`, {
      method: "POST",
      body,
      credentials: "same-origin",
      headers: { "X-Requested-With": "fetch", RequestVerificationToken: csrf(), Accept: "application/json" },
    }).then((response) => (response.ok ? response.json() : Promise.reject(new Error(String(response.status)))));
  }

  /** یکدست‌سازی متن فارسی برای مقایسه (ی/ک عربی، ارقام فارسی، نیم‌فاصله). */
  const normalize = (text) => String(text || "")
    .replace(/[يى]/g, "ی").replace(/ك/g, "ک").replace(/\u200c/g, " ")
    .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 1776))
    .toLowerCase().trim();

  const isTyping = (target) => target && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName));

  // ── ۱) جست‌وجوی سریع ────────────────────────────────────────────────────
  const paletteData = (() => {
    try { return JSON.parse(document.body.dataset.palette || "{}"); } catch { return {}; }
  })();

  // صفحه‌های منو (فقط آن‌هایی که کارمند می‌بیند، چون از خود منوی رندرشده خوانده می‌شوند).
  const navPages = (() => {
    const seen = new Set();
    return $$(".a-nav .a-nav-link[href^='/admin']").map((link) => ({ url: link.getAttribute("href"), title: link.textContent.trim() }))
      .filter((p) => p.title && !seen.has(p.url) && seen.add(p.url));
  })();

  let palette = null;
  let paletteInput = null;
  let paletteList = null;
  let activeIndex = 0;
  let searchTimer = 0;
  let searchAbort = null;

  function buildPalette() {
    palette = document.createElement("div");
    palette.className = "a-palette";
    palette.hidden = true;
    palette.innerHTML =
      `<div class="a-palette-backdrop" data-palette-close></div>
       <div class="a-palette-box" role="dialog" aria-modal="true" aria-label="جست‌وجوی سریع">
         <div class="a-palette-head">
           <input type="search" class="a-palette-input" placeholder="شمارهٔ سفارش، موبایل، نام محصول یا مشتری، شمارهٔ تیکت، نام صفحه…" aria-label="عبارت جست‌وجو" maxlength="100" autocomplete="off" role="combobox" aria-expanded="true" aria-controls="a-palette-list">
           <kbd class="a-kbd">Esc</kbd>
         </div>
         <div class="a-palette-list" id="a-palette-list" role="listbox"></div>
         <div class="a-palette-foot"><span><kbd class="a-kbd">↑</kbd><kbd class="a-kbd">↓</kbd> جابه‌جایی</span><span><kbd class="a-kbd">Enter</kbd> باز کردن</span></div>
       </div>`;
    document.body.appendChild(palette);
    paletteInput = $(".a-palette-input", palette);
    paletteList = $(".a-palette-list", palette);

    palette.addEventListener("click", (e) => { if (e.target.closest("[data-palette-close]")) closePalette(); });
    paletteInput.addEventListener("input", () => {
      renderLocal();
      clearTimeout(searchTimer);
      searchTimer = setTimeout(searchServer, 220);
    });
    paletteInput.addEventListener("keydown", (e) => {
      const items = $$(".a-palette-item", paletteList);
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        if (!items.length) return;
        activeIndex = (activeIndex + (e.key === "ArrowDown" ? 1 : -1) + items.length) % items.length;
        highlight(items);
      } else if (e.key === "Enter") {
        e.preventDefault();
        const item = items[activeIndex];
        if (item) window.location.href = item.getAttribute("href");
      } else if (e.key === "Escape") {
        closePalette();
      }
    });
  }

  function highlight(items) {
    items.forEach((item, i) => item.classList.toggle("is-active", i === activeIndex));
    const current = items[activeIndex];
    if (current) {
      if (current.scrollIntoView) current.scrollIntoView({ block: "nearest" });
      paletteInput.setAttribute("aria-activedescendant", current.id);
    }
  }

  /** ساخت امن فهرست (فقط textContent؛ هیچ HTML از سرور مستقیم درج نمی‌شود). */
  function renderGroups(groups) {
    paletteList.replaceChildren();
    let index = 0;
    groups.filter((g) => g.items.length).forEach((group) => {
      const head = document.createElement("div");
      head.className = "a-palette-group";
      head.textContent = group.title;
      paletteList.appendChild(head);
      group.items.forEach((item) => {
        if (typeof item.url !== "string" || !item.url.startsWith("/") || item.url.startsWith("//")) return;
        const link = document.createElement("a");
        link.className = "a-palette-item";
        link.href = item.url;
        link.id = `a-palette-${index++}`;
        link.setAttribute("role", "option");
        const title = document.createElement("span");
        title.className = "a-palette-title";
        title.textContent = item.title;
        link.appendChild(title);
        if (item.subtitle) {
          const sub = document.createElement("span");
          sub.className = "a-palette-sub";
          sub.textContent = item.subtitle;
          link.appendChild(sub);
        }
        paletteList.appendChild(link);
      });
    });
    if (!paletteList.children.length) {
      const empty = document.createElement("p");
      empty.className = "a-palette-empty";
      empty.textContent = paletteInput.value.trim().length < 2 ? "حداقل دو حرف بنویسید." : "نتیجه‌ای پیدا نشد.";
      paletteList.appendChild(empty);
    }
    activeIndex = 0;
    highlight($$(".a-palette-item", paletteList));
  }

  let serverGroups = [];

  function localGroups() {
    const q = normalize(paletteInput.value);
    if (!q) {
      return [
        { title: "سنجاق‌شده‌ها", items: paletteData.pins || [] },
        { title: "صفحه‌های اخیر", items: paletteData.recents || [] },
      ];
    }
    const words = q.split(/\s+/);
    const pages = navPages.concat(paletteData.pins || []).filter((p, i, all) => all.findIndex((x) => x.url === p.url) === i)
      .filter((p) => words.every((w) => normalize(p.title).includes(w))).slice(0, 6);
    return [{ title: "صفحه‌ها", items: pages }];
  }

  function renderLocal() {
    serverGroups = [];
    renderGroups(localGroups());
  }

  function searchServer() {
    const q = paletteInput.value.trim();
    if (q.length < 2) return;
    if (searchAbort) searchAbort.abort();
    searchAbort = new AbortController();
    fetch(`/admin/ux?handler=Search&q=${encodeURIComponent(q)}`, { credentials: "same-origin", headers: { Accept: "application/json" }, signal: searchAbort.signal })
      .then((r) => (r.ok ? r.json() : []))
      .then((hits) => {
        if (paletteInput.value.trim() !== q) return; // پاسخ قدیمی
        const byGroup = new Map();
        hits.forEach((h) => {
          if (!byGroup.has(h.group)) byGroup.set(h.group, []);
          byGroup.get(h.group).push({ url: h.url, title: h.title, subtitle: h.subtitle });
        });
        serverGroups = Array.from(byGroup, ([title, items]) => ({ title, items }));
        renderGroups(localGroups().concat(serverGroups));
      })
      .catch(() => {});
  }

  function openPalette() {
    if (!palette) buildPalette();
    palette.hidden = false;
    document.documentElement.classList.add("a-no-scroll");
    paletteInput.value = "";
    renderLocal();
    setTimeout(() => paletteInput.focus(), 0);
  }

  function closePalette() {
    if (!palette || palette.hidden) return;
    palette.hidden = true;
    document.documentElement.classList.remove("a-no-scroll");
  }

  $$("[data-palette-open]").forEach((button) => button.addEventListener("click", openPalette));

  // ── ۴) کشوی راهنما ──────────────────────────────────────────────────────
  const drawer = $("[data-help-drawer]");
  let lastFocus = null;

  function openHelp() {
    if (!drawer) return;
    lastFocus = document.activeElement;
    drawer.hidden = false;
    document.documentElement.classList.add("a-no-scroll");
    const close = $("[data-help-close].a-icon-btn", drawer);
    if (close) close.focus();
  }

  function closeHelp() {
    if (!drawer || drawer.hidden) return;
    drawer.hidden = true;
    document.documentElement.classList.remove("a-no-scroll");
    if (lastFocus && lastFocus.focus) lastFocus.focus();
  }

  $$("[data-help-open]").forEach((button) => button.addEventListener("click", openHelp));
  if (drawer) drawer.addEventListener("click", (e) => { if (e.target.closest("[data-help-close]")) closeHelp(); });

  // میانبرهای صفحه‌کلید سراسری.
  document.addEventListener("keydown", (e) => {
    if ((e.ctrlKey || e.metaKey) && (e.key === "k" || e.key === "K" || e.key === "ک")) {
      e.preventDefault();
      if (palette && !palette.hidden) closePalette(); else openPalette();
    } else if (e.key === "Escape") {
      closePalette();
      closeHelp();
    } else if (e.key === "?" && !isTyping(e.target) && !e.ctrlKey && !e.metaKey) {
      e.preventDefault();
      openHelp();
    }
  });

  // ── ۲) سنجاق ───────────────────────────────────────────────────────────
  $$("[data-pin]").forEach((button) => button.addEventListener("click", () => {
    button.disabled = true;
    post("Pin", { url: button.dataset.pinUrl, title: button.dataset.pinTitle, icon: button.dataset.pinIcon })
      .then((result) => {
        if (!result.ok) return toast(result.message, "error");
        const pinned = !!(result.data && result.data.pinned);
        button.classList.toggle("is-pinned", pinned);
        button.setAttribute("aria-pressed", pinned ? "true" : "false");
        toast(pinned ? "به سنجاق‌شده‌های منو اضافه شد." : "سنجاق برداشته شد.", "success");
      })
      .catch(() => toast("ارتباط برقرار نشد. دوباره تلاش کنید.", "error"))
      .finally(() => { button.disabled = false; });
  }));

  // ── ۳) فیلترهای ذخیره‌شده ────────────────────────────────────────────────
  const main = $("main[data-saved-filters]");
  const filterForm = main && ($("form.a-filters", main) || $("form[method='get' i]", main));

  /** کوئری فعلی بدون «page» و پارامترهای خالی، با ترتیب ثابت. مثال: «?status=Paid&from=1404/08/01». */
  function currentQuery() {
    const params = new URLSearchParams(window.location.search);
    params.delete("page");
    params.delete("handler");
    const clean = new URLSearchParams();
    Array.from(params.entries()).filter(([, v]) => v !== "").sort(([a], [b]) => a.localeCompare(b)).forEach(([k, v]) => clean.append(k, v));
    const text = clean.toString();
    return text ? `?${text}` : "";
  }

  const sameQuery = (a, b) => {
    const sort = (q) => { const p = new URLSearchParams(q); p.delete("page"); return Array.from(p.entries()).sort().join("&"); };
    return sort(a) === sort(b);
  };

  if (filterForm) {
    let filters = [];
    try { filters = JSON.parse(main.dataset.savedFilters || "[]"); } catch { filters = []; }
    const path = window.location.pathname;
    const bar = document.createElement("div");
    bar.className = "a-saved-filters";
    filterForm.parentNode.insertBefore(bar, filterForm);

    const render = () => {
      bar.replaceChildren();
      const label = document.createElement("span");
      label.className = "a-saved-label";
      label.textContent = "فیلترهای من:";
      if (filters.length) bar.appendChild(label);

      filters.forEach((f) => {
        const chip = document.createElement("span");
        chip.className = "a-saved-chip" + (sameQuery(f.query, window.location.search) ? " is-active" : "");
        const link = document.createElement("a");
        link.href = path + f.query;
        link.textContent = f.name;
        const remove = document.createElement("button");
        remove.type = "button";
        remove.textContent = "×";
        remove.setAttribute("aria-label", `حذف فیلتر ${f.name}`);
        remove.addEventListener("click", () => {
          const ask = window.FratakModal ? window.FratakModal.confirm({ message: `فیلتر «${f.name}» حذف شود؟`, okText: "حذف", danger: true }) : Promise.resolve(true);
          ask.then((yes) => {
            if (!yes) return;
            post("DeleteFilter", { path, name: f.name }).then(() => {
              filters = filters.filter((x) => x.name !== f.name);
              render();
            }).catch(() => toast("حذف انجام نشد.", "error"));
          });
        });
        chip.append(link, remove);
        bar.appendChild(chip);
      });

      const query = currentQuery();
      if (query && !filters.some((f) => sameQuery(f.query, query))) {
        const save = document.createElement("button");
        save.type = "button";
        save.className = "a-saved-add";
        save.textContent = "+ ذخیرهٔ این فیلتر";
        save.addEventListener("click", () => askName(save, query));
        bar.appendChild(save);
      }
      bar.hidden = !bar.children.length;
    };

    /** پنجرهٔ کوچک درون‌صفحه‌ای برای نام فیلتر (به‌جای prompt مرورگر). */
    const askName = (anchor, query) => {
      const box = document.createElement("form");
      box.className = "a-saved-form";
      box.innerHTML = '<input class="a-input a-input-sm" maxlength="40" required placeholder="نام فیلتر، مثلاً «پرداخت در محل امروز»" aria-label="نام فیلتر"><button class="a-btn a-btn-sm" type="submit">ذخیره</button><button class="a-btn a-btn-sm a-btn-light" type="button">انصراف</button>';
      anchor.replaceWith(box);
      const input = $("input", box);
      input.focus();
      $("button[type=button]", box).addEventListener("click", render);
      box.addEventListener("submit", (e) => {
        e.preventDefault();
        const name = input.value.trim();
        if (!name) return;
        post("Filter", { path, name, query }).then((result) => {
          if (!result.ok) return toast(result.message, "error");
          filters = filters.filter((x) => x.name !== name).concat([{ name, query }]);
          render();
          toast("فیلتر ذخیره شد.", "success");
        }).catch(() => toast("ذخیره انجام نشد.", "error"));
      });
    };

    render();
  }

  // ── ۶) بارگذاری کتابخانهٔ رسانه (صفحهٔ /admin/media): کشیدن و رها کردن یا انتخاب فایل ──
  const dropzone = $("form[data-media-upload]");
  if (dropzone) {
    const input = $("[data-media-input]", dropzone);
    const status = $("[data-media-status]", dropzone);
    const upload = (files) => {
      if (!files || !files.length) return;
      const body = new FormData();
      Array.from(files).forEach((file) => body.append("files", file));
      status.hidden = false;
      status.textContent = `در حال بارگذاری ${files.length.toLocaleString("fa-IR")} فایل…`;
      fetch(dropzone.action || "/admin/media?handler=Upload", {
        method: "POST", body, credentials: "same-origin",
        headers: { "X-Requested-With": "fetch", RequestVerificationToken: csrf() },
      }).then((r) => r.json()).then((result) => {
        if (!result.ok) { status.textContent = result.message; return toast(result.message, "error"); }
        toast(result.message, "success");
        window.location.href = "/admin/media";
      }).catch(() => { status.textContent = "بارگذاری انجام نشد."; });
    };
    input.addEventListener("change", () => upload(input.files));
    ["dragenter", "dragover"].forEach((type) => dropzone.addEventListener(type, (e) => { e.preventDefault(); dropzone.classList.add("is-over"); }));
    ["dragleave", "drop"].forEach((type) => dropzone.addEventListener(type, (e) => { e.preventDefault(); dropzone.classList.remove("is-over"); }));
    dropzone.addEventListener("drop", (e) => upload(e.dataTransfer && e.dataTransfer.files));
  }

  // ── ۵) حالت تیره ───────────────────────────────────────────────────────
  $$("[data-theme-toggle]").forEach((button) => button.addEventListener("click", () => {
    const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
    document.documentElement.dataset.theme = next;
    const meta = $('meta[name="color-scheme"]');
    if (meta) meta.content = next;
    const secure = window.location.protocol === "https:" ? "; Secure" : "";
    document.cookie = `fratak_admin_theme=${next}; Path=/admin; Max-Age=31536000; SameSite=Lax${secure}`;
  }));
})();
