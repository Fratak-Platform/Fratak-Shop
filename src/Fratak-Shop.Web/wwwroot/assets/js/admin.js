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
    const boxes = () => $$("input[data-row-check]", root);
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
      await navigator.clipboard.writeText(button.dataset.copy);
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

  // ── Minimal HTML toolbar for content textareas (no inline scripts, CSP-safe) ──
  $$("textarea[data-html-editor]").forEach((area) => {
    const bar = document.createElement("div");
    bar.className = "a-editor-bar";
    const tools = [
      ["H2", "<h2>", "</h2>"], ["H3", "<h3>", "</h3>"], ["پاراگراف", "<p>", "</p>"], ["B", "<strong>", "</strong>"],
      ["I", "<em>", "</em>"], ["فهرست", "<ul>\n  <li>", "</li>\n</ul>"], ["آیتم", "<li>", "</li>"], ["نقل‌قول", "<blockquote>", "</blockquote>"],
    ];
    const wrap = (before, after) => {
      const start = area.selectionStart, end = area.selectionEnd;
      const selected = area.value.slice(start, end);
      area.setRangeText(before + selected + after, start, end, "select");
      area.focus();
      area.dispatchEvent(new Event("input", { bubbles: true }));
    };
    tools.forEach(([label, before, after]) => {
      const b = document.createElement("button");
      b.type = "button"; b.textContent = label;
      b.addEventListener("click", () => wrap(before, after));
      bar.appendChild(b);
    });
    const link = document.createElement("button");
    link.type = "button"; link.textContent = "لینک";
    link.addEventListener("click", () => {
      const url = window.prompt("آدرس لینک (مثلاً /product/... یا https://...)");
      if (url && /^(https?:\/\/|\/)/i.test(url)) wrap('<a href="' + url.replace(/"/g, "&quot;") + '">', "</a>");
    });
    bar.appendChild(link);
    const preview = document.createElement("button");
    preview.type = "button"; preview.textContent = "پیش‌نمایش";
    const pane = document.createElement("div");
    pane.className = "a-editor-preview";
    pane.hidden = true;
    preview.addEventListener("click", () => {
      pane.hidden = !pane.hidden;
      if (!pane.hidden) {
        // Rendered in an inert template-parsed fragment without scripts or handlers.
        const doc = new DOMParser().parseFromString(area.value, "text/html");
        doc.querySelectorAll("script, style, iframe, object, embed").forEach((n) => n.remove());
        doc.querySelectorAll("*").forEach((n) => Array.from(n.attributes).forEach((a) => { if (/^on/i.test(a.name) || /javascript:/i.test(a.value)) n.removeAttribute(a.name); }));
        pane.replaceChildren(...Array.from(doc.body.childNodes));
      }
    });
    bar.appendChild(preview);
    area.parentNode.insertBefore(bar, area);
    area.parentNode.insertBefore(pane, area.nextSibling);
  });

  // ── Leave-page warning for edited long forms ─────────────────────────
  $$("form[data-dirty-guard]").forEach((form) => {
    let dirty = false;
    form.addEventListener("input", () => { dirty = true; });
    form.addEventListener("submit", () => { dirty = false; });
    window.addEventListener("beforeunload", (e) => { if (dirty) { e.preventDefault(); e.returnValue = ""; } });
  });
})();
