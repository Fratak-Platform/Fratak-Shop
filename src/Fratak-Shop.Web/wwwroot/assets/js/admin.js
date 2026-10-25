/*
 * Admin panel behaviour (no dependencies, CSP friendly – no inline scripts):
 *  - [data-sidebar-open|close]   : mobile navigation drawer
 *  - [data-dropdown]             : user menu
 *  - [data-toast]                : auto-hide status messages
 *  - form[data-confirm] / button[data-confirm] : custom confirmation (modal.js) before submitting
 *  - [data-autosubmit]           : submit the owning form on change
 *  - [data-check-all] / [data-bulk]: select rows and show the bulk-action bar
 *  - [data-repeat]               : add / remove repeatable rows from a <template>
 *  - [data-slug-source]          : fill an empty slug field from the name
 *  - [data-counter]              : character counter (SEO title / description)
 *  - [data-copy]                 : copy text
 *  - [data-show-when]            : show a block only when a select / checkbox has a value
 *  - [data-live]                 : refresh a fragment periodically (live monitoring)
 *  - [data-file-preview]         : preview chosen images
 *  - GET filter forms            : empty fields are not sent (clean URLs)
 *  - [data-placeholder-editor]   : برچسب‌های قابل کلیک + پیش‌نمایش زندهٔ پیام
 *  - [data-sms-counter]          : شمارندهٔ نویسه و تعداد پیامک
 *  - select[data-ready-pick]     : درج «پیام آماده» در فرم
 */
(function () {
  "use strict";

  const $ = (selector, root) => (root || document).querySelector(selector);
  const $$ = (selector, root) => Array.from((root || document).querySelectorAll(selector));

  // ── Sidebar drawer (mobile) ──────────────────────────────────────────
  const sidebar = $("[data-sidebar]");
  const setSidebar = (open) => {
    if (!sidebar) return;
    sidebar.classList.toggle("is-open", open);
    document.body.style.overflow = open ? "hidden" : "";
  };
  $$("[data-sidebar-open]").forEach((b) => b.addEventListener("click", () => setSidebar(true)));
  $$("[data-sidebar-close]").forEach((b) => b.addEventListener("click", () => setSidebar(false)));
  document.addEventListener("keydown", (e) => { if (e.key === "Escape") setSidebar(false); });
  const active = $(".a-nav-link.is-active");
  if (active && active.scrollIntoView) active.scrollIntoView({ block: "center" });

  // ── Dropdown ─────────────────────────────────────────────────────────
  $$("[data-dropdown]").forEach((root) => {
    const toggle = $("[data-dropdown-toggle]", root);
    const menu = $("[data-dropdown-menu]", root);
    if (!toggle || !menu) return;
    toggle.addEventListener("click", (e) => {
      e.stopPropagation();
      const open = menu.hidden;
      menu.hidden = !open;
      toggle.setAttribute("aria-expanded", String(open));
    });
    document.addEventListener("click", (e) => {
      if (!root.contains(e.target)) { menu.hidden = true; toggle.setAttribute("aria-expanded", "false"); }
    });
  });

  // ── Toasts ───────────────────────────────────────────────────────────
  $$("[data-toast]").forEach((box) => {
    $$("[data-toast-close]", box).forEach((b) => b.addEventListener("click", () => b.closest(".a-toast").remove()));
    setTimeout(() => { $$(".a-toast-success", box).forEach((t) => t.remove()); }, 6000);
  });

  // ── Confirmation dialogs ─────────────────────────────────────────────
  const ask = (el) => {
    const message = el.dataset.confirm || "آیا مطمئن هستید؟";
    if (window.FratakModal) {
      return window.FratakModal.confirm({
        title: el.dataset.confirmTitle || "تأیید عملیات",
        message,
        okText: el.dataset.confirmOk || "بله، انجام شود",
        cancelText: "انصراف",
        danger: el.dataset.confirmDanger !== undefined,
      });
    }
    return Promise.resolve(window.confirm(message));
  };

  document.addEventListener("submit", (e) => {
    const form = e.target;
    if (!(form instanceof HTMLFormElement)) return;
    const submitter = e.submitter;
    const source = submitter && submitter.hasAttribute("data-confirm") ? submitter : form.hasAttribute("data-confirm") ? form : null;
    if (source && form.dataset.confirmed !== "1") {
      e.preventDefault();
      ask(source).then((ok) => {
        if (!ok) return;
        form.dataset.confirmed = "1";
        if (submitter && form.requestSubmit) form.requestSubmit(submitter); else form.submit();
        setTimeout(() => { delete form.dataset.confirmed; }, 0);
      });
      return;
    }

    // GET filter forms: drop empty fields for clean, shareable URLs.
    if ((form.method || "get").toLowerCase() === "get") {
      $$("input, select", form).forEach((field) => {
        if (field.name && !field.value) field.disabled = true;
      });
      setTimeout(() => $$("input, select", form).forEach((f) => { f.disabled = false; }), 0);
    } else if (!e.defaultPrevented) {
      // Prevent double submits of POST forms.
      $$("button[type=submit], button:not([type])", form).forEach((b) => {
        if (b.dataset.keepEnabled === undefined) setTimeout(() => { b.disabled = true; }, 0);
      });
    }
  });

  // ── Auto submit ──────────────────────────────────────────────────────
  $$("[data-autosubmit]").forEach((el) => el.addEventListener("change", () => {
    const form = el.form || el.closest("form");
    if (form) { if (form.requestSubmit) form.requestSubmit(); else form.submit(); }
  }));

  // ── Bulk selection ───────────────────────────────────────────────────
  $$("[data-bulk]").forEach((root) => {
    const bar = $("[data-bulk-bar]", root);
    const count = $("[data-bulk-count]", root);
    const all = $("[data-check-all]", root);
    const boxes = () => $$("input[data-row-check]:not(:disabled)", root);
    const refresh = () => {
      const selected = boxes().filter((b) => b.checked).length;
      if (bar) bar.classList.toggle("is-visible", selected > 0);
      if (count) count.textContent = selected.toLocaleString("fa-IR");
      if (all) { all.checked = selected > 0 && selected === boxes().length; all.indeterminate = selected > 0 && selected < boxes().length; }
    };
    if (all) all.addEventListener("change", () => { boxes().forEach((b) => { b.checked = all.checked; }); refresh(); });
    root.addEventListener("change", (e) => { if (e.target.matches && e.target.matches("input[data-row-check]")) refresh(); });
    refresh();
  });

  // ── Repeatable rows ──────────────────────────────────────────────────
  $$("[data-repeat]").forEach((root) => {
    const list = $("[data-repeat-list]", root);
    const template = $("template", root);
    const add = $("[data-repeat-add]", root);
    if (!list || !template || !add) return;
    const reindex = () => {
      $$("[data-repeat-item]", list).forEach((row, index) => {
        $$("[name]", row).forEach((field) => { field.name = field.name.replace(/\[\d+\]/, "[" + index + "]"); });
      });
    };
    add.addEventListener("click", () => {
      const fragment = template.content.cloneNode(true);
      list.appendChild(fragment);
      reindex();
      const last = list.lastElementChild && $("input, textarea, select", list.lastElementChild);
      if (last) last.focus();
    });
    list.addEventListener("click", (e) => {
      const remove = e.target.closest && e.target.closest("[data-repeat-remove]");
      if (!remove) return;
      remove.closest("[data-repeat-item]").remove();
      reindex();
    });
  });

  // ── Slug from name ───────────────────────────────────────────────────
  const slugify = (text) => text.trim().toLowerCase()
    .replace(/[\u200c\s_]+/g, "-")
    .replace(/[^\u0600-\u06FFa-z0-9-]/g, "")
    .replace(/-+/g, "-").replace(/^-|-$/g, "");
  $$("[data-slug-source]").forEach((source) => {
    const target = $(source.dataset.slugSource);
    if (!target) return;
    let touched = target.value.length > 0;
    target.addEventListener("input", () => { touched = target.value.length > 0; });
    source.addEventListener("input", () => { if (!touched) target.value = slugify(source.value); });
  });

  // ── Character counters ───────────────────────────────────────────────
  $$("[data-counter]").forEach((field) => {
    const max = Number(field.dataset.counter) || 160;
    const badge = document.createElement("span");
    badge.className = "a-counter";
    const label = field.id && $("label[for='" + field.id + "']");
    (label || field.parentElement).appendChild(badge);
    const update = () => {
      const n = field.value.length;
      badge.textContent = n.toLocaleString("fa-IR") + " / " + max.toLocaleString("fa-IR");
      badge.classList.toggle("is-over", n > max);
    };
    field.addEventListener("input", update);
    update();
  });

  // ── Copy ─────────────────────────────────────────────────────────────
  $$("[data-copy]").forEach((button) => button.addEventListener("click", async () => {
    try {
      const value = button.dataset.copy || "";
      const source = value.startsWith("#") ? document.querySelector(value) : null;
      await navigator.clipboard.writeText(source ? (source.value || source.textContent || "") : value);
      if (window.FratakModal && window.FratakModal.toast) window.FratakModal.toast("کپی شد", "success");
    } catch (_) { /* ignore */ }
  }));

  // ── Conditional blocks ───────────────────────────────────────────────
  $$("[data-show-when]").forEach((block) => {
    const [selector, expected] = block.dataset.showWhen.split("=");
    const field = $(selector);
    if (!field) return;
    const values = (expected || "").split("|");
    const update = () => {
      const value = field.type === "checkbox" ? (field.checked ? "true" : "false") : field.value;
      const visible = expected === undefined ? Boolean(value) : values.includes(value);
      block.hidden = !visible;
      $$("input, select, textarea", block).forEach((f) => { if (f.dataset.requiredWhenShown !== undefined) f.required = visible; });
    };
    field.addEventListener("change", update);
    update();
  });

  // ── Image preview ────────────────────────────────────────────────────
  $$("[data-file-preview]").forEach((input) => {
    const target = $(input.dataset.filePreview);
    if (!target) return;
    input.addEventListener("change", () => {
      target.innerHTML = "";
      Array.from(input.files || []).slice(0, 12).forEach((file) => {
        if (!file.type.startsWith("image/")) return;
        const img = document.createElement("img");
        img.className = "a-thumb a-thumb-lg";
        img.alt = "";
        img.src = URL.createObjectURL(file);
        target.appendChild(img);
      });
    });
  });

  // ── Live fragments ───────────────────────────────────────────────────
  $$("[data-live]").forEach((root) => {
    const url = root.dataset.live;
    const seconds = Math.max(5, Number(root.dataset.liveInterval) || 15);
    let timer = null;
    const load = async () => {
      if (document.hidden) return;
      try {
        const response = await fetch(url, { headers: { "X-Requested-With": "fetch" }, credentials: "same-origin", cache: "no-store" });
        if (!response.ok) return;
        root.innerHTML = await response.text();
        const stamp = $("[data-live-stamp]");
        if (stamp) stamp.textContent = new Date().toLocaleTimeString("fa-IR");
      } catch (_) { /* offline – try again later */ }
    };
    timer = setInterval(load, seconds * 1000);
    document.addEventListener("visibilitychange", () => { if (!document.hidden) load(); });
    window.addEventListener("beforeunload", () => clearInterval(timer));
  });

  // ── Search-result (Google) preview of the SEO fields ─────────────────
  $$("[data-seo-preview]").forEach((box) => {
    const title = $(box.dataset.title);
    const desc = $(box.dataset.desc);
    const titleFallback = box.dataset.titleFallback ? $(box.dataset.titleFallback) : null;
    const descFallback = box.dataset.descFallback ? $(box.dataset.descFallback) : null;
    const outTitle = $("[data-seo-preview-title]", box);
    const outDesc = $("[data-seo-preview-desc]", box);
    const cut = (text, max) => (text.length > max ? text.slice(0, max - 1) + "…" : text);
    const update = () => {
      const t = (title && title.value.trim()) || (titleFallback && titleFallback.value.trim()) || "عنوان صفحه";
      const d = (desc && desc.value.trim()) || (descFallback && descFallback.value.trim()) || "توضیحات متا در نتایج جست‌وجو اینجا نمایش داده می‌شود.";
      outTitle.textContent = cut(t, 65);
      outDesc.textContent = cut(d, 165);
    };
    [title, desc, titleFallback, descFallback].forEach((f) => f && f.addEventListener("input", update));
    update();
  });

  // ویرایشگر متن (textarea[data-html-editor]) حالا در editor.js با ویرایشگر دیداری Jodit ساخته می‌شود.

  // ── Colour picker ↔ text field ───────────────────────────────────────
  $$("input[type=color][data-color-for]").forEach((picker) => {
    const text = $(picker.dataset.colorFor);
    if (!text) return;
    picker.addEventListener("input", () => { text.value = picker.value; text.dispatchEvent(new Event("input", { bubbles: true })); });
    text.addEventListener("input", () => { if (/^#[0-9a-f]{6}$/i.test(text.value)) picker.value = text.value; });
  });

  // ── برچسب‌ها و پیش‌نمایش زندهٔ پیام (قالب‌های پیام، پیام‌های آماده) ─────────
  // ساختار: کانتینر [data-placeholder-editor data-preview-url] شامل یک فرم با فیلدهای [data-chip-field]،
  // دکمه‌های .a-chip[data-insert] و جاهای پیش‌نمایش [data-preview="title|body|sms|source|unknown"].
  // کلیک روی برچسب → درج در محل نشانگر آخرین فیلد انتخاب‌شده (اگر هنوز فیلدی انتخاب نشده، انتهای متن اصلی).
  const insertAtCursor = (field, text) => {
    const start = field.selectionStart ?? field.value.length;
    const end = field.selectionEnd ?? field.value.length;
    const before = field.value.slice(0, start);
    // اگر قبل از برچسب حرف چسبیده باشد، یک فاصله اضافه می‌شود: «سلام{FirstName}» → «سلام {FirstName}»
    const pad = before && !/\s$/.test(before) ? " " : "";
    field.value = before + pad + text + field.value.slice(end);
    const caret = start + pad.length + text.length;
    field.focus();
    field.setSelectionRange(caret, caret);
    field.dispatchEvent(new Event("input", { bubbles: true }));
  };

  $$("[data-placeholder-editor]").forEach((editor) => {
    const fields = $$("[data-chip-field]", editor);
    const form = $("form", editor);
    if (!fields.length || !form) return;
    let lastField = fields.find((f) => f.tagName === "TEXTAREA") || fields[0];
    let lastHadFocus = false;
    fields.forEach((f) => f.addEventListener("focus", () => { lastField = f; lastHadFocus = true; }));

    $$(".a-chip[data-insert]", editor).forEach((chip) => {
      // mousedown پیش‌فرض باعث از دست رفتن محل نشانگر می‌شود؛ جلویش را می‌گیریم.
      chip.addEventListener("mousedown", (e) => e.preventDefault());
      chip.addEventListener("click", () => {
        if (!lastHadFocus) lastField.setSelectionRange(lastField.value.length, lastField.value.length);
        insertAtCursor(lastField, chip.dataset.insert);
        lastHadFocus = true;
      });
    });

    const url = editor.dataset.previewUrl;
    const set = (name, value) => { const el = $(`[data-preview="${name}"]`, editor); if (el) el.textContent = value || ""; return el; };
    let timer = null;
    let seq = 0;
    const refresh = async () => {
      const mine = ++seq;
      try {
        const response = await fetch(url, { method: "POST", body: new FormData(form), credentials: "same-origin", headers: { "X-Requested-With": "fetch" } });
        if (!response.ok || mine !== seq) return;
        const data = await response.json();
        set("title", data.title);
        set("body", data.body);
        set("source", data.source ? "داده‌های پیش‌نمایش: " + data.source : "");
        const smsWrap = $("[data-preview-sms-wrap]", editor);
        set("sms", data.sms);
        if (smsWrap) smsWrap.hidden = !data.sms;
        const unknown = $('[data-preview="unknown"]', editor);
        if (unknown) {
          const keys = data.unknown || [];
          unknown.hidden = keys.length === 0;
          unknown.textContent = keys.length ? "این برچسب‌ها شناخته نشدند و در پیام خالی می‌مانند: " + keys.map((k) => "{" + k + "}").join("، ") : "";
        }
      } catch (_) { /* بدون اتصال – بعداً دوباره */ }
    };
    if (url) {
      form.addEventListener("input", () => { clearTimeout(timer); timer = setTimeout(refresh, 450); });
      refresh();
    }
  });

  // ── شمارندهٔ پیامک: تعداد نویسه و تعداد پیامک (فارسی: ۷۰ نویسه، چندبخشی: ۶۷) ──
  $$("[data-sms-counter]").forEach((area) => {
    const out = area.parentElement && $("[data-sms-count]", area.parentElement);
    if (!out) return;
    const update = () => {
      const n = area.value.length;
      const parts = n === 0 ? 0 : n <= 70 ? 1 : Math.ceil(n / 67);
      out.textContent = n ? `${n.toLocaleString("fa-IR")} نویسه · حدود ${parts.toLocaleString("fa-IR")} پیامک (پیش از جایگزینی برچسب‌ها)` : "";
    };
    area.addEventListener("input", update);
    update();
  });

  // ── پیام آماده: درج متن انتخاب‌شده در فرم (ViewComponent «ReadyMessagePicker») ──
  $$("select[data-ready-pick]").forEach((select) => {
    select.addEventListener("change", () => {
      const option = select.selectedOptions[0];
      if (!option || !option.value) return;
      const body = select.dataset.bodyTarget && $(select.dataset.bodyTarget);
      if (body) {
        const text = option.dataset.body || "";
        body.value = body.value.trim() ? body.value.replace(/\s+$/, "") + "\n" + text : text;
        body.dispatchEvent(new Event("input", { bubbles: true }));
        body.focus();
      }
      // عنوان و لینک فقط اگر خالی باشند پر می‌شوند تا نوشتهٔ کارمند پاک نشود.
      [["titleTarget", "title"], ["linkTarget", "link"]].forEach(([target, key]) => {
        const field = select.dataset[target] && $(select.dataset[target]);
        if (field && !field.value.trim() && option.dataset[key]) {
          field.value = option.dataset[key];
          field.dispatchEvent(new Event("input", { bubbles: true }));
        }
      });
      // شمارش استفاده (برای مرتب‌سازی پرکاربردها) – خطا مهم نیست.
      const token = $('input[name="__RequestVerificationToken"]');
      if (token) {
        const data = new FormData();
        data.append("__RequestVerificationToken", token.value);
        fetch(`/admin/ready-messages?handler=Used&id=${encodeURIComponent(option.value)}`, { method: "POST", body: data, credentials: "same-origin" }).catch(() => {});
      }
      select.value = "";
    });
  });

  // ── Leave-page warning for edited long forms ─────────────────────────
  $$("form[data-dirty-guard]").forEach((form) => {
    let dirty = false;
    form.addEventListener("input", () => { dirty = true; });
    form.addEventListener("submit", () => { dirty = false; });
    window.addEventListener("beforeunload", (e) => { if (dirty) { e.preventDefault(); e.returnValue = ""; } });
  });
})();
