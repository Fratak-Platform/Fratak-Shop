/*
 * بررسی زندهٔ سئو در فرم‌های ویرایش پنل (دور سئوی خودکار، موارد ۱ تا ۱۸).
 * محل بارگذاری: _AdminLayout.cshtml؛ فقط وقتی کار می‌کند که <meta name="fratak-smart"> (مجوز محتوا/سئو) و بخش
 * [data-seo-live] (در _SeoFields.cshtml) داخل فرمی با data-smart-context باشد.
 *
 * چه می‌کند؟ با هر تایپ (۱ ثانیه بعد از توقف) مقدارهای فعلی فرم به /admin/seo/smart?handler=Live فرستاده می‌شود و
 * نمره، چک‌لیست مشکل‌ها، تعداد واژه و تراکم کلمهٔ کلیدی، طول عنوان/توضیح، تیترها، پیشنهاد لینک داخلی، عبارت‌های ماشینی،
 * خطر جریمه و «کم‌محتوا» نمایش داده می‌شود. هیچ چیز ذخیره نمی‌شود.
 * نگهبان انتشار: اگر فرم در حال انتشار/فعال شدن است و مورد مهمی (blockers) باقی مانده، با پنجرهٔ FratakModal یادآوری می‌شود؛
 * کاربر می‌تواند «با این حال ذخیره کن» را بزند (قفل نمی‌کند).
 * امنیت: توکن ضدجعل (meta csrf-token)؛ همهٔ متن‌ها با textContent نوشته می‌شوند (هرگز HTML خام درج نمی‌شود).
 */
(function () {
  "use strict";

  const meta = document.querySelector('meta[name="fratak-smart"]');
  const panel = document.querySelector("[data-seo-live]");
  if (!meta || !panel) return;
  const host = panel.closest("[data-smart-context]") || document.querySelector("[data-smart-context]");
  if (!host) return;

  let context;
  try {
    context = JSON.parse(host.getAttribute("data-smart-context") || "null");
  } catch (error) {
    context = null;
  }
  if (!context || !context.kind) return;

  const BASE = meta.getAttribute("content") || "/admin/seo/smart";
  const $ = (selector, root) => (selector ? (root || document).querySelector(selector) : null);
  const fa = (value) => String(value).replace(/\d/g, (d) => "۰۱۲۳۴۵۶۷۸۹"[d]);
  const csrf = () => ($('meta[name="csrf-token"]') || {}).content || "";
  const form = panel.closest("form") || host.closest("form") || (host.tagName === "FORM" ? host : null);
  const scoreBadge = $("[data-seo-live-score]", panel);
  const status = $("[data-seo-live-status]", panel);
  const body = $("[data-seo-live-body]", panel);

  let timer = 0;
  let inFlight = false;
  let queued = false;
  let last = null;
  let confirmed = false;

  function field(key) {
    const selector = context[key];
    return selector ? $(selector, host) || $(selector) : null;
  }

  function valueOf(node) {
    if (!node) return null;
    if (node.__fratakEditor) return node.__fratakEditor.value;
    return node.value;
  }

  function guid(value) {
    return value && /^[0-9a-f-]{36}$/i.test(value) ? value : null;
  }

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
  }

  function collect() {
    return {
      kind: context.kind,
      id: context.id,
      page: valueOf(field("page")),
      name: valueOf(field("name")),
      englishName: valueOf(field("english")),
      slug: valueOf(field("slug")),
      shortText: valueOf(field("summary")),
      body: valueOf(field("body")),
      categoryId: guid(valueOf(field("category"))),
      brandId: guid(valueOf(field("brand"))),
      warrantyTitle: valueOf(field("warrantyTitle")),
      warrantyMonths: valueOf(field("warrantyMonths")) || null,
      seoTitle: valueOf($(panel.getAttribute("data-title"))),
      metaDescription: valueOf($(panel.getAttribute("data-desc"))),
      focusKeyword: valueOf($(panel.getAttribute("data-focus"))),
    };
  }

  async function run() {
    if (inFlight) {
      queued = true;
      return;
    }
    inFlight = true;
    status.textContent = "در حال بررسی…";
    const data = collect();
    const payload = new FormData();
    Object.keys(data).forEach((key) => {
      if (data[key] !== null && data[key] !== undefined) payload.append(key, data[key]);
    });
    try {
      const response = await fetch(BASE + "?handler=Live", {
        method: "POST",
        credentials: "same-origin",
        headers: { RequestVerificationToken: csrf(), "X-Requested-With": "fetch" },
        body: payload,
      });
      const json = await response.json().catch(() => null);
      render(json || { ok: false, message: "پاسخ نامعتبر از سرور." });
    } catch (error) {
      status.textContent = "ارتباط برقرار نشد؛ با تایپ بعدی دوباره امتحان می‌شود.";
    } finally {
      inFlight = false;
      if (queued) {
        queued = false;
        schedule();
      }
    }
  }

  function schedule() {
    clearTimeout(timer);
    timer = setTimeout(run, 1000);
  }

  function list(title, items, className) {
    const wrap = el("div", "mt-2");
    wrap.appendChild(el("div", "text-xs font-bold", title));
    const ul = el("ul", "text-xs a-stack mt-1");
    items.forEach((item) => {
      const li = el("li", className || null);
      if (item instanceof Node) li.appendChild(item);
      else li.textContent = item;
      ul.appendChild(li);
    });
    wrap.appendChild(ul);
    return wrap;
  }

  function render(result) {
    body.replaceChildren();
    if (!result.ok) {
      last = null;
      scoreBadge.textContent = "…";
      scoreBadge.className = "a-badge a-badge-gray";
      status.textContent = result.message || "بررسی ممکن نشد.";
      return;
    }
    last = result;
    confirmed = false;
    scoreBadge.textContent = fa(result.score) + " از ۱۰۰";
    scoreBadge.className = "a-badge " + (result.score >= 80 ? "a-badge-green" : result.score >= 50 ? "a-badge-yellow" : "a-badge-red");
    status.textContent = "به‌روز شد · " + fa(result.passed || 0) + " مورد درست";

    const facts = el("div", "a-chips");
    const titleBudget = 60;
    const add = (text, bad) => {
      const chip = el("span", "a-chip" + (bad ? " text-red-600" : ""), text);
      facts.appendChild(chip);
    };
    add(fa(result.words) + " واژه" + (result.thin ? " (کم‌محتوا)" : ""), result.thin);
    add("کلمهٔ کلیدی: " + fa(result.keywordCount) + " بار · تراکم " + fa(result.density) + "٪", result.density > 3);
    add("عنوان: " + fa(result.titleLength) + " از " + fa(titleBudget) + " نویسه", result.titleLength > titleBudget || result.titleLength < 30);
    add("توضیح: " + fa(result.metaLength) + " از ۱۶۰ نویسه", result.metaLength > 160 || (result.metaLength > 0 && result.metaLength < 120));
    add("طبیعی بودن متن: " + fa(result.naturalScore) + "٪", result.naturalScore < 60);
    body.appendChild(facts);

    if (result.blockers && result.blockers.length) {
      const box = el("div", "a-alert a-alert-warn mt-2");
      box.appendChild(list("پیش از انتشار درست کنید", result.blockers));
      body.appendChild(box);
    }
    if (result.checks && result.checks.length) {
      body.appendChild(list("چک‌لیست", result.checks.slice(0, 12).map((check) => (check.level === "bad" ? "✗ " : "• ") + check.message)));
    }
    if (result.headingIssues && result.headingIssues.length) {
      body.appendChild(list("تیترها", result.headingIssues));
    }
    if (result.headings && result.headings.length) {
      const outline = result.headings.slice(0, 15).map((heading) => {
        const row = el("span", null, "H" + fa(heading.level) + " · " + heading.text);
        row.style.paddingInlineStart = Math.max(0, heading.level - 2) * 12 + "px";
        return row;
      });
      body.appendChild(list("ساختار تیترها", outline));
    }
    if (result.links && result.links.length) {
      body.appendChild(list("پیشنهاد لینک داخلی (هنگام نمایش صفحه خودکار هم ساخته می‌شود)", result.links.map((link) => {
        const row = el("span");
        row.append("«" + link.phrase + "» ← ");
        const code = el("span", "a-ltr a-mono", link.url);
        row.appendChild(code);
        return row;
      })));
    }
    if (result.patterns && result.patterns.length) {
      body.appendChild(list("عبارت‌های کلیشه‌ای/ماشینی", result.patterns.map((pattern) => "«" + pattern.phrase + "» – " + pattern.hint)));
    }
    if (result.risks && result.risks.length) {
      body.appendChild(list("خطر جریمه", result.risks.map((risk) => (risk.blocking ? "✗ " : "• ") + risk.message)));
    }
  }

  /** آیا این ذخیره صفحه را عمومی می‌کند؟ (چک‌باکس انتشار/فعال روشن یا دکمه‌ای با data-publish) */
  function publishing(submitter) {
    if (submitter && (submitter.hasAttribute("data-publish") || /publish/i.test(submitter.name + " " + submitter.value))) return true;
    return Array.from(form.querySelectorAll('input[type="checkbox"]'))
      .some((box) => box.checked && /(IsPublished|IsActive)$/.test(box.name || ""));
  }

  if (form) {
    form.addEventListener("submit", (event) => {
      if (confirmed || !last || !last.blockers || !last.blockers.length || !publishing(event.submitter)) return;
      if (!window.FratakModal || !window.FratakModal.confirm) return;
      event.preventDefault();
      const submitter = event.submitter;
      window.FratakModal.confirm({
        title: "پیش از انتشار",
        message: "این موارد هنوز درست نشده‌اند:\n• " + last.blockers.join("\n• ") + "\n\nبا این حال ذخیره و منتشر شود؟",
        okText: "با این حال ذخیره کن",
        cancelText: "برمی‌گردم درستش کنم",
      }).then((ok) => {
        if (!ok) return;
        confirmed = true;
        if (form.requestSubmit) form.requestSubmit(submitter || undefined);
        else form.submit();
      });
    });
  }

  host.addEventListener("input", schedule);
  host.addEventListener("change", schedule);
  panel.hidden = false;
  schedule();
})();
