/*
 * «پیشنهاد هوشمند» و «سئوی هوشمند» پنل مدیریت (مرحلهٔ شانزدهم).
 * محل بارگذاری: _AdminLayout.cshtml (فقط وقتی <meta name="fratak-smart"> هست، یعنی کارمند مجوز محتوا/سئو دارد).
 *
 * ۱) پیشنهاد هوشمند: کنار هر فیلد متنی فارسی فرم‌های POST (عنوان محصول، توضیحات، متن مقاله، درباره ما …) دکمه‌ای اضافه
 *    می‌شود. با کلیک، متن فعلی به /admin/seo/smart?handler=Text فرستاده می‌شود و ۲ تا ۳ نسخهٔ بهتر (درست‌نویسی، سئو، جذاب،
 *    ساختاریافته) با توضیح تغییرات نشان داده می‌شود. «استفاده از این متن» فقط فیلد را پر می‌کند؛ ذخیره با خود کاربر است.
 *    فیلدهای لاتین/آدرس/نامک/کد، عددی، تاریخ و هر فیلد با data-smart="off" دکمه ندارند.
 *    نوع فیلد: data-smart-purpose="SeoTitle|MetaDescription|ProductName|ShortText|Body|Plain" یا خودکار (از data-smart-context).
 * ۲) سئوی هوشمند: دکمهٔ [data-smart-seo] در بخش سئوی هر فرم. مقدارهای فعلی فرم (حتی صفحهٔ ذخیره‌نشده) به
 *    ?handler=Draft فرستاده می‌شود؛ پاسخ: نمرهٔ قبل/بعد، ۲–۳ عنوان و توضیح، کلمهٔ کلیدی، نامک، متن تکمیلی و هشدارها.
 *
 * زمینهٔ فرم: <form data-smart-context='{"kind":"Product","id":"…","name":"#p-name","body":"#p-desc",…}'>
 * امنیت: درخواست‌ها با توکن ضدجعل (meta csrf-token) فرستاده می‌شوند؛ پیش‌نمایش HTML پیش از نمایش پاک‌سازی می‌شود.
 */
(function () {
  "use strict";

  const meta = document.querySelector('meta[name="fratak-smart"]');
  if (!meta) return;
  const BASE = meta.getAttribute("content") || "/admin/seo/smart";

  const $ = (selector, root) => (root || document).querySelector(selector);
  const $$ = (selector, root) => Array.from((root || document).querySelectorAll(selector));
  const csrf = () => ($('meta[name="csrf-token"]') || {}).content || "";
  const toast = (message, type) => (window.FratakModal && window.FratakModal.toast ? window.FratakModal.toast(message, type) : null);
  const fa = (value) => String(value).replace(/\d/g, (d) => "۰۱۲۳۴۵۶۷۸۹"[d]);

  // ── ابزارهای مشترک ─────────────────────────────────────────────────────
  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
  }

  function button(text, className, onClick) {
    const node = el("button", className || "a-btn a-btn-sm", text);
    node.type = "button";
    if (onClick) node.addEventListener("click", onClick);
    return node;
  }

  /** ارسال فرم به هندلر؛ پاسخ همیشه { ok, message, … } است (حتی برای خطای شبکه یا صفحهٔ «دسترسی ندارید»). */
  async function post(handler, data) {
    const body = new FormData();
    Object.keys(data).forEach((key) => {
      const value = data[key];
      if (value === null || value === undefined) return;
      if (Array.isArray(value)) value.forEach((item) => body.append(key, item));
      else body.append(key, value);
    });
    try {
      const response = await fetch(BASE + "?handler=" + handler, {
        method: "POST",
        credentials: "same-origin",
        headers: { RequestVerificationToken: csrf(), "X-Requested-With": "fetch" },
        body,
      });
      const json = await response.json().catch(() => null);
      if (json) return json;
      return { ok: false, message: response.status === 429 ? "درخواست‌ها زیاد است؛ کمی بعد امتحان کنید." : "پاسخ نامعتبر از سرور (شاید دسترسی ندارید یا از حساب خارج شده‌اید)." };
    } catch (error) {
      return { ok: false, message: "ارتباط با سرور برقرار نشد." };
    }
  }

  /** پاک‌سازی HTML پیش‌نمایش (اسکریپت، استایل، on* و javascript: حذف می‌شود). */
  function safeHtml(html) {
    const doc = new DOMParser().parseFromString("<div>" + (html || "") + "</div>", "text/html");
    $$("script,style,iframe,object,embed,link,meta,form,input,button,textarea,select,base", doc).forEach((node) => node.remove());
    $$("*", doc).forEach((node) => {
      Array.from(node.attributes).forEach((attr) => {
        const name = attr.name.toLowerCase();
        if (name.startsWith("on") || name === "style" || name === "srcdoc"
          || ((name === "href" || name === "src" || name === "xlink:href") && /^\s*(javascript|data|vbscript):/i.test(attr.value))) {
          node.removeAttribute(attr.name);
        }
      });
    });
    return document.importNode(doc.body.firstChild, true);
  }

  /** زمینهٔ فرم (نوع صفحه، شناسه و انتخابگر فیلدها). */
  function contextOf(field) {
    const host = (field && field.closest("[data-smart-context]")) || $("[data-smart-context]");
    if (!host) return null;
    try {
      const context = JSON.parse(host.getAttribute("data-smart-context") || "null");
      if (!context) return null;
      context.root = host;
      return context;
    } catch (error) {
      return null;
    }
  }

  function find(context, key) {
    const selector = context && context[key];
    if (!selector) return null;
    return $(selector, context.root) || $(selector);
  }

  function valueOf(field) {
    if (!field) return null;
    if (field.__fratakEditor) return field.__fratakEditor.value;
    return field.value;
  }

  function setValue(field, value) {
    if (!field) return;
    if (field.__fratakEditor) field.__fratakEditor.value = value;
    field.value = value;
    field.dispatchEvent(new Event("input", { bubbles: true }));
    field.dispatchEvent(new Event("change", { bubbles: true }));
  }

  function focusKeyword(context) {
    const field = (context && $("[data-focus-keyword]", context.root)) || $("[data-focus-keyword]");
    return field ? field.value : null;
  }

  function guid(value) {
    return value && /^[0-9a-f-]{36}$/i.test(value) ? value : null;
  }

  // ── پنجرهٔ مشترک ───────────────────────────────────────────────────────
  function modal(title) {
    const root = el("div", "a-palette a-smart");
    root.setAttribute("role", "presentation");
    const backdrop = el("div", "a-palette-backdrop");
    const box = el("div", "a-palette-box a-smart-box");
    box.setAttribute("role", "dialog");
    box.setAttribute("aria-modal", "true");
    const head = el("div", "a-palette-head");
    const heading = el("b", "flex-1", title);
    const close = button("✕", "a-icon-btn", () => destroy());
    close.setAttribute("aria-label", "بستن");
    head.append(heading, close);
    const body = el("div", "a-smart-body");
    const foot = el("div", "a-smart-foot");
    box.append(head, body, foot);
    root.append(backdrop, box);
    document.body.appendChild(root);
    backdrop.addEventListener("click", () => destroy());
    const onKey = (event) => { if (event.key === "Escape") destroy(); };
    document.addEventListener("keydown", onKey);
    let closed = false;
    function destroy() {
      if (closed) return;
      closed = true;
      document.removeEventListener("keydown", onKey);
      root.remove();
      if (api.onClose) api.onClose();
    }
    const api = { root, body, foot, heading, close: destroy, onClose: null };
    setTimeout(() => close.focus(), 0);
    return api;
  }

  function loading(container, text) {
    container.replaceChildren(el("div", "a-smart-loading", text));
  }

  function list(items, className) {
    const ul = el("ul", className || "a-smart-list");
    (items || []).forEach((item) => ul.appendChild(el("li", null, item)));
    return ul;
  }

  function section(title, ...children) {
    const wrap = el("section", "a-smart-section");
    wrap.appendChild(el("h4", "a-smart-title", title));
    children.filter(Boolean).forEach((child) => wrap.appendChild(child));
    return wrap;
  }

  function scoreBox(label, value, highlight) {
    const box = el("div", "a-smart-score" + (highlight ? " is-main" : ""));
    box.append(el("span", null, label), el("b", null, fa(value)));
    return box;
  }

  // ═══════════════════════════════════════════════════════════════════════
  //  ۱) پیشنهاد هوشمند (هر فیلد متنی)
  // ═══════════════════════════════════════════════════════════════════════
  const SKIP_NAME = /(slug|url|link|canonical|code|sku|gtin|mpn|phone|mobile|email|password|token|secret|apikey|api_key|color|icon|path|regex|selector|iban|sheba|card|tracking|barcode|username|domain|host|template|pattern|keyword|features|faq|attribute|option|search|query|css|script|html_head|pixel)/i;
  const GOALS = [
    ["Balanced", "متعادل"],
    ["Seo", "سئو"],
    ["Persuasive", "جذب مشتری"],
    ["Clarity", "وضوح و خوانایی"],
  ];

  function eligible(field) {
    if (field.dataset.smart === "off" || field.dataset.smartAttached) return false;
    if (field.dataset.smartPurpose || field.dataset.smartPurposeFrom) return true;
    if (!field.name || field.readOnly || field.disabled) return false;
    if (field.tagName === "INPUT" && !["text", ""].includes((field.getAttribute("type") || "").toLowerCase())) return false;
    if (!field.form || (field.form.getAttribute("method") || "get").toLowerCase() !== "post") return false;
    if (field.closest("table, .a-filters, .a-palette, [data-smart='off'], [data-repeat]")) return false;
    if (field.classList.contains("a-ltr") || field.getAttribute("dir") === "ltr") return false;
    if (field.hasAttribute("data-money") || field.hasAttribute("data-jalali") || field.hasAttribute("data-slug-target")) return false;
    const mode = (field.getAttribute("inputmode") || "").toLowerCase();
    if (mode === "numeric" || mode === "decimal" || mode === "tel" || mode === "email" || mode === "url") return false;
    const max = parseInt(field.getAttribute("maxlength") || "0", 10);
    if (max > 0 && max < 20) return false;
    if (SKIP_NAME.test(field.name) || SKIP_NAME.test(field.id || "")) return false;
    return true;
  }

  function purposeOf(field) {
    if (field.dataset.smartPurposeFrom) {
      const source = $(field.dataset.smartPurposeFrom);
      if (source && source.value) return source.value;
    }
    if (field.dataset.smartPurpose) return field.dataset.smartPurpose;
    if (field.hasAttribute("data-html-editor")) return "Body";
    const context = contextOf(field);
    if (context) {
      if (context.name && field.matches(context.name)) return context.kind === "Product" ? "ProductName" : "Plain";
      if (context.summary && field.matches(context.summary)) return "ShortText";
      if (context.body && field.matches(context.body)) return "Body";
      if (field.tagName === "TEXTAREA") return "ShortText";
    }
    return "Plain";
  }

  function labelOf(field) {
    const label = field.id ? $('label[for="' + CSS.escape(field.id) + '"]') : null;
    return label ? label.textContent.trim() : "";
  }

  function attach(field) {
    field.dataset.smartAttached = "1";
    const row = el("div", "a-smart-row");
    const trigger = button("پیشنهاد هوشمند", "a-smart-trigger", () => openText(field));
    trigger.setAttribute("title", "بازنویسی بهتر این متن (درست‌نویسی، سئو، جذابیت)");
    row.appendChild(trigger);
    // ویرایشگر دیداری: دکمه بالای ویرایشگر؛ بقیه: درست زیر فیلد.
    if (field.hasAttribute("data-html-editor")) field.parentNode.insertBefore(row, field);
    else field.insertAdjacentElement("afterend", row);
  }

  function openText(field) {
    const context = contextOf(field);
    const purpose = purposeOf(field);
    const label = labelOf(field);
    const original = valueOf(field) || "";
    const dialog = modal("پیشنهاد هوشمند" + (label ? " · " + label : ""));
    let goal = "Balanced";
    let current = null;

    const tabs = el("div", "a-smart-tabs");
    GOALS.forEach(([value, text]) => {
      const tab = button(text, value === goal ? "is-active" : "", () => {
        goal = value;
        $$("button", tabs).forEach((b) => b.classList.toggle("is-active", b === tab));
        run();
      });
      tabs.appendChild(tab);
    });
    const content = el("div", "a-smart-content");
    dialog.body.append(el("p", "a-muted text-xs", "هدف بازنویسی را انتخاب کنید؛ هیچ تغییری تا «استفاده از این متن» و ذخیرهٔ فرم اعمال نمی‌شود."), tabs, content);

    const none = button("هیچ‌کدام مناسب نیست", "a-btn a-btn-sm a-btn-light", () => {
      if (current && current.variants && current.variants.length) {
        const rules = [].concat(...current.variants.map((v) => v.rules || []));
        post("Feedback", { purpose, variant: "*", accepted: "false", rules, input: original, outputLength: 0 });
      }
      dialog.close();
    });
    dialog.foot.append(el("span", "a-muted text-xs flex-1", "بعد از استفاده، فرم را ذخیره کنید."), none, button("بستن", "a-btn a-btn-sm a-btn-light", () => dialog.close()));

    async function run() {
      loading(content, "در حال بررسی متن…");
      const data = {
        purpose, goal, text: original,
        isHtml: field.hasAttribute("data-html-editor") ? "true" : "false",
        kind: context ? context.kind : null,
        id: context ? context.id : null,
        subject: context ? valueOf(find(context, "name")) : null,
        englishName: context ? valueOf(find(context, "english")) : null,
        keyword: focusKeyword(context),
        categoryId: context ? guid(valueOf(find(context, "category"))) : null,
        brandId: context ? guid(valueOf(find(context, "brand"))) : null,
      };
      const result = await post("Text", data);
      current = result;
      renderText(result);
    }

    function renderText(result) {
      content.replaceChildren();
      if (!result.ok) {
        content.appendChild(el("div", "a-alert a-alert-warn", result.message || "پیشنهادی پیدا نشد."));
        return;
      }
      if (result.before) {
        const summary = el("div", "a-smart-scores");
        summary.appendChild(scoreBox("نمرهٔ متن فعلی", result.before.score));
        if (result.variants && result.variants[0]) summary.appendChild(scoreBox("بهترین پیشنهاد", Math.max(...result.variants.map((v) => v.score)), true));
        content.appendChild(summary);
        if (result.before.issues && result.before.issues.length) {
          const details = el("details", "a-smart-details");
          details.appendChild(el("summary", null, "ایرادهای متن فعلی (" + fa(result.before.issues.length) + ")"));
          details.appendChild(list(result.before.issues));
          content.appendChild(details);
        }
      }
      (result.warnings || []).forEach((w) => content.appendChild(el("div", "a-alert a-alert-info", w)));
      (result.variants || []).forEach((variant, index) => content.appendChild(variantCard(variant, index)));
    }

    function variantCard(variant, index) {
      const card = el("article", "a-smart-variant" + (index === 0 ? " is-first" : ""));
      const head = el("div", "a-smart-variant-head");
      head.append(el("b", "flex-1", variant.title), el("span", "a-chip", "نمره " + fa(variant.score)), el("span", "a-muted text-xs", fa(variant.length) + " نویسه"));
      card.appendChild(head);
      const preview = el("div", "a-smart-preview" + (variant.isHtml ? " is-html" : ""));
      if (variant.isHtml) preview.appendChild(safeHtml(variant.text));
      else preview.textContent = variant.text;
      card.appendChild(preview);
      if (variant.changes && variant.changes.length) card.appendChild(list(variant.changes, "a-smart-list is-changes"));
      if (variant.notes && variant.notes.length) card.appendChild(list(variant.notes, "a-smart-list is-notes"));
      const actions = el("div", "a-smart-actions");
      actions.appendChild(button("استفاده از این متن", "a-btn a-btn-sm", () => {
        setValue(field, variant.text);
        post("Feedback", { purpose, variant: variant.id, accepted: "true", rules: variant.rules || [], input: original, outputLength: variant.length });
        toast("متن جایگزین شد؛ برای ماندگاری، فرم را ذخیره کنید.", "success");
        dialog.close();
      }));
      const copy = button("کپی", "a-btn a-btn-sm a-btn-light", () => {
        if (navigator.clipboard) navigator.clipboard.writeText(variant.text).then(() => toast("کپی شد.", "success"));
      });
      actions.appendChild(copy);
      card.appendChild(actions);
      return card;
    }

    run();
  }

  function scan(root) {
    $$("input, textarea", root || document).forEach((field) => {
      try { if (eligible(field)) attach(field); } catch (error) { /* یک فیلد خراب نباید بقیه را از کار بیندازد */ }
    });
  }

  // ═══════════════════════════════════════════════════════════════════════
  //  ۲) سئوی هوشمند (بخش سئوی فرم‌ها)
  // ═══════════════════════════════════════════════════════════════════════
  function openSeo(trigger) {
    const context = contextOf(trigger);
    if (!context) {
      toast("این فرم برای سئوی هوشمند آماده نیست.", "error");
      return;
    }
    const scope = trigger.closest("form") || document;
    const fields = {
      title: $(trigger.dataset.title, scope) || $(trigger.dataset.title),
      desc: $(trigger.dataset.desc, scope) || $(trigger.dataset.desc),
      focus: $(trigger.dataset.focus, scope) || $(trigger.dataset.focus),
    };
    const dialog = modal("سئوی هوشمند");
    let variant = 0;
    let research = true;

    const toolbar = el("label", "a-check text-sm");
    const researchBox = el("input");
    researchBox.type = "checkbox";
    researchBox.checked = true;
    researchBox.addEventListener("change", () => { research = researchBox.checked; });
    toolbar.append(researchBox, document.createTextNode(" جست‌وجو در اینترنت (رقبا و پیشنهادهای جست‌وجو)"));
    const content = el("div", "a-smart-content");
    dialog.body.append(toolbar, content);

    const again = button("گزینه‌های دیگر", "a-btn a-btn-sm a-btn-light", () => { variant += 1; run(); });
    dialog.foot.append(el("span", "a-muted text-xs flex-1", "«استفاده» فقط فیلدها را پر می‌کند؛ بعد فرم را ذخیره کنید."), again, button("بستن", "a-btn a-btn-sm a-btn-light", () => dialog.close()));

    async function run() {
      loading(content, research ? "در حال تحقیق در نتایج جست‌وجو و جدول دانش سئو… (ممکن است چند ثانیه طول بکشد)" : "در حال ساخت پیشنهاد…");
      again.disabled = true;
      const data = {
        kind: context.kind,
        id: context.id,
        page: valueOf(find(context, "page")),
        name: valueOf(find(context, "name")),
        englishName: valueOf(find(context, "english")),
        slug: valueOf(find(context, "slug")),
        shortText: valueOf(find(context, "summary")),
        body: valueOf(find(context, "body")),
        categoryId: guid(valueOf(find(context, "category"))),
        brandId: guid(valueOf(find(context, "brand"))),
        warrantyTitle: valueOf(find(context, "warrantyTitle")),
        warrantyMonths: valueOf(find(context, "warrantyMonths")) || null,
        seoTitle: valueOf(fields.title),
        metaDescription: valueOf(fields.desc),
        focusKeyword: valueOf(fields.focus),
        research: research ? "true" : "false",
        variant,
      };
      const result = await post("Draft", data);
      again.disabled = false;
      renderSeo(result);
    }

    function useButton(text, apply) {
      return button(text || "استفاده", "a-btn a-btn-sm", (event) => {
        apply();
        event.currentTarget.textContent = "✓ اعمال شد";
        event.currentTarget.disabled = true;
      });
    }

    function options(items, field, budget) {
      const wrap = el("div", "a-smart-options");
      items.forEach((option) => {
        const row = el("div", "a-smart-option");
        const text = el("div", "flex-1");
        text.appendChild(el("div", null, option.text));
        const info = el("div", "a-muted text-xs", fa(option.full) + " از " + fa(budget) + " نویسه" + (option.note ? " · " + option.note : ""));
        if (option.full > budget) info.classList.add("text-red-600");
        text.appendChild(info);
        row.appendChild(text);
        if (field) row.appendChild(useButton("استفاده", () => setValue(field, option.text)));
        wrap.appendChild(row);
      });
      return wrap;
    }

    function renderSeo(result) {
      content.replaceChildren();
      if (!result.ok) {
        content.appendChild(el("div", "a-alert a-alert-warn", result.message || "ساخت پیشنهاد ممکن نشد."));
        return;
      }
      const scores = el("div", "a-smart-scores");
      scores.append(scoreBox("نمرهٔ فعلی", result.scoreBefore), scoreBox("بعد از پیشنهادها", result.scoreAfter, true));
      content.appendChild(scores);

      (result.warnings || []).forEach((w) => content.appendChild(el("div", "a-alert " + (w.severity >= 2 ? "a-alert-danger" : "a-alert-warn"), w.message)));

      if (result.keyword) {
        const chips = el("div", "a-smart-chips");
        const main = el("span", "a-chip is-main", result.keyword + (result.keywordIsNew ? " (پیشنهادی)" : ""));
        chips.appendChild(main);
        if (result.keywordIsNew && fields.focus) chips.appendChild(useButton("استفاده", () => setValue(fields.focus, result.keyword)));
        (result.keywords || []).filter((k) => k.phrase !== result.keyword).slice(0, 5).forEach((k) => {
          const chip = button(k.phrase, "a-chip", () => { if (fields.focus) { setValue(fields.focus, k.phrase); toast("کلمهٔ کلیدی گذاشته شد.", "success"); } });
          chip.title = (k.reasons || []).join("، ");
          chips.appendChild(chip);
        });
        content.appendChild(section("کلمهٔ کلیدی", chips));
      }

      if (result.titles && result.titles.length) content.appendChild(section("عنوان سئو", options(result.titles, fields.title, 60)));
      if (result.metas && result.metas.length) content.appendChild(section("توضیحات متا", options(result.metas.map((m) => Object.assign({}, m, { full: m.length })), fields.desc, 160)));

      const slugField = find(context, "slug");
      if (result.slug) {
        const row = el("div", "a-smart-option");
        const text = el("div", "flex-1");
        text.append(el("div", "a-ltr", result.slug), el("div", "a-muted text-xs", result.slugReason || ""));
        row.appendChild(text);
        if (slugField) row.appendChild(useButton("استفاده", () => setValue(slugField, result.slug)));
        content.appendChild(section("نامک (آدرس)", row));
      }

      const bodyField = find(context, "body");
      if (result.body) {
        const preview = el("div", "a-smart-preview is-html");
        preview.appendChild(safeHtml(result.body));
        const wrap = el("div");
        wrap.append(el("p", "a-muted text-xs", "متن فعلی دست‌نخورده در ابتدای این متن مانده و فقط بخش‌های تکمیلی (از مشخصات و شرایط واقعی فروشگاه) اضافه شده است."), preview);
        if (bodyField) wrap.appendChild(el("div", "a-smart-actions")).appendChild(useButton("جایگزینی متن صفحه", () => setValue(bodyField, result.body)));
        content.appendChild(section("متن تکمیلی (صفحهٔ کم‌محتوا)", wrap));
      }

      if (result.faq && result.faq.length) {
        const dl = el("dl", "a-smart-faq");
        result.faq.forEach((item) => { dl.append(el("dt", null, item.q), el("dd", null, item.a)); });
        content.appendChild(section("پرسش‌وپاسخ پیشنهادی", el("p", "a-muted text-xs", "پرسش‌وپاسخ با «پیشنهادهای سیستم» موتور سئو روی صفحه گذاشته می‌شود؛ این‌جا برای ایده گرفتن است."), dl));
      }

      if (result.todo && result.todo.length) content.appendChild(section("کارهای باقی‌مانده", list(result.todo)));

      const research = result.research || {};
      if (research.query || (research.suggestions && research.suggestions.length)) {
        const parts = [];
        if (research.query) parts.push(el("p", "text-sm", "عبارت تحقیق: «" + research.query + "»" + (research.pages ? " · تحلیل " + fa(research.pages) + " صفحهٔ برتر" : "")));
        if (research.hosts && research.hosts.length) parts.push(el("p", "a-muted text-xs a-ltr", research.hosts.slice(0, 6).join(" · ")));
        if (research.suggestions && research.suggestions.length) {
          const chips = el("div", "a-smart-chips");
          research.suggestions.slice(0, 12).forEach((s) => chips.appendChild(el("span", "a-chip", s)));
          parts.push(chips);
        }
        if (research.errors && research.errors.length) parts.push(el("p", "a-muted text-xs", "در دسترس نبود: " + research.errors.join("، ")));
        const details = el("details", "a-smart-details");
        details.appendChild(el("summary", null, "تحقیق و دلیل‌ها"));
        parts.forEach((p) => details.appendChild(p));
        if (result.reasons && result.reasons.length) details.appendChild(list(result.reasons));
        content.appendChild(details);
      } else if (result.reasons && result.reasons.length) {
        const details = el("details", "a-smart-details");
        details.appendChild(el("summary", null, "دلیل‌ها"));
        details.appendChild(list(result.reasons));
        content.appendChild(details);
      }
    }

    run();
  }

  function initSeo() {
    $$("[data-smart-seo-bar]").forEach((bar) => {
      if (contextOf(bar)) bar.hidden = false;
    });
    document.addEventListener("click", (event) => {
      const trigger = event.target.closest("[data-smart-seo]");
      if (trigger) openSeo(trigger);
    });
  }

  function init() {
    initSeo();
    scan(document);
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();

  window.FratakSmart = { scan, openText, openSeo };
})();
