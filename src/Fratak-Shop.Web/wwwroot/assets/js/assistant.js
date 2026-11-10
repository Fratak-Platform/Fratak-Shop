/*
 * دور ۲۳ – ویجت «دستیار فروشگاه» (assets/js/assistant.js)
 * دکمهٔ گرد پایین صفحه ← پنل گفت‌وگو. همهٔ صفحه‌های فروشگاه (دسکتاپ و موبایل).
 *
 * • سبک: تا مشتری پنل را باز نکند هیچ درخواستی به سرور نمی‌رود. صفحه‌های کش‌شده هم کار می‌کنند (توکن ضدجعل از ‎/api/antiforgery).
 * • امن: همهٔ متن‌ها با textContent نوشته می‌شوند (هیچ HTML از سرور درج نمی‌شود)؛ لینک‌ها فقط مسیر داخلی (/…) هستند.
 * • حافظه: گفت‌وگو در sessionStorage همین برگه می‌ماند (حداکثر ۳۰ پیام) تا با رفتن به صفحهٔ دیگر پاک نشود؛
 *   شناسهٔ تصادفی گفت‌وگو در localStorage (fs.assist.sid) – بدون اطلاعات شخصی.
 * • کالای همین صفحه از [data-product-view] خوانده می‌شود («قیمت این چنده؟»).
 * • جست‌وجوی اینترنت: اگر پاسخ «lookup» داشت، «لطفاً چند لحظه صبر کنید…» نشان داده و هر ۱٫۵ ثانیه (حداکثر ۱۲ بار) نتیجه پرسیده می‌شود.
 * API: POST /api/assistant/message و GET /api/assistant/lookup/{token} (Web/Infrastructure/Assistant/AssistantEndpoints.cs).
 */
(function () {
  "use strict";
  if (window.__fsAssistant || !window.fetch || !document.body) return;
  window.__fsAssistant = true;

  var LOG_KEY = "fs.assist.log";
  var OPEN_KEY = "fs.assist.open";
  var LAST_KEY = "fs.assist.last";
  var SID_KEY = "fs.assist.sid";
  var MAX_LOG = 30;
  var GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

  function store(kind) {
    try { return kind === "local" ? window.localStorage : window.sessionStorage; } catch (e) { return null; }
  }
  function read(key, fallback) {
    var s = store("session");
    try { var v = s && s.getItem(key); return v ? JSON.parse(v) : fallback; } catch (e) { return fallback; }
  }
  function write(key, value) {
    var s = store("session");
    try { if (s) s.setItem(key, JSON.stringify(value)); } catch (e) { /* حافظهٔ پر یا حالت خصوصی */ }
  }
  function sessionId() {
    var s = store("local");
    var id = null;
    try { id = s && s.getItem(SID_KEY); } catch (e) { id = null; }
    if (id && /^[a-z0-9]{8,32}$/i.test(id)) return id;
    var bytes = new Uint8Array(12);
    (window.crypto || window.msCrypto).getRandomValues(bytes);
    id = Array.prototype.map.call(bytes, function (b) { return ("0" + b.toString(16)).slice(-2); }).join("");
    try { if (s) s.setItem(SID_KEY, id); } catch (e) { /* بی‌اهمیت */ }
    return id;
  }
  function el(tag, cls, text) {
    var node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text != null) node.textContent = text;
    return node;
  }
  function money(value) {
    var n = Math.round(Number(value) || 0);
    return n.toLocaleString("fa-IR") + " تومان";
  }
  function safeUrl(url) {
    return typeof url === "string" && url.charAt(0) === "/" && url.charAt(1) !== "/" && url.charAt(1) !== "\\" ? url : null;
  }
  function currentProduct() {
    var node = document.querySelector("[data-product-view]");
    var id = node && node.getAttribute("data-product-view");
    return id && GUID.test(id) ? id : null;
  }

  // ── ساخت ویجت ───────────────────────────────────────────────
  var button = el("button", "fs-as-btn");
  button.type = "button";
  button.setAttribute("aria-label", "دستیار فروشگاه");
  button.setAttribute("aria-haspopup", "dialog");
  var svgNs = "http://www.w3.org/2000/svg";
  var svg = document.createElementNS(svgNs, "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("aria-hidden", "true");
  var path = document.createElementNS(svgNs, "path");
  path.setAttribute("d", "M12 3C6.5 3 2 6.8 2 11.5c0 2.4 1.2 4.6 3.1 6.1L4 21l4.2-1.9c1.2.4 2.5.6 3.8.6 5.5 0 10-3.8 10-8.5S17.5 3 12 3zm-4 9.8a1.3 1.3 0 110-2.6 1.3 1.3 0 010 2.6zm4 0a1.3 1.3 0 110-2.6 1.3 1.3 0 010 2.6zm4 0a1.3 1.3 0 110-2.6 1.3 1.3 0 010 2.6z");
  svg.appendChild(path);
  button.appendChild(svg);

  var panel = el("section", "fs-as-panel");
  panel.hidden = true;
  panel.setAttribute("role", "dialog");
  panel.setAttribute("aria-label", "دستیار فروشگاه");
  var head = el("div", "fs-as-head");
  var title = el("b", null, "دستیار فروشگاه");
  title.appendChild(el("small", null, "قیمت، موجودی، سبد خرید و پیگیری سفارش"));
  var close = el("button", "fs-as-close", "×");
  close.type = "button";
  close.setAttribute("aria-label", "بستن");
  head.appendChild(title);
  head.appendChild(close);
  var log = el("div", "fs-as-log");
  log.setAttribute("role", "log");
  log.setAttribute("aria-live", "polite");
  var form = el("form", "fs-as-form");
  var input = el("input");
  input.type = "text";
  input.maxLength = 300;
  input.autocomplete = "off";
  input.placeholder = "سؤالتان را بنویسید…";
  input.setAttribute("aria-label", "پیام شما");
  var send = el("button", null, "ارسال");
  send.type = "submit";
  form.appendChild(input);
  form.appendChild(send);
  var note = el("div", "fs-as-note", "دستیار خودکار فروشگاه – پاسخ‌ها از اطلاعات خود فروشگاه است.");
  panel.appendChild(head);
  panel.appendChild(log);
  panel.appendChild(form);
  panel.appendChild(note);
  document.body.appendChild(button);
  document.body.appendChild(panel);

  var history = read(LOG_KEY, []);
  if (!Array.isArray(history)) history = [];
  var busy = false;
  var tokenPromise = null;

  // ── نمایش پیام‌ها ───────────────────────────────────────────
  function renderCards(cards) {
    var box = el("div", "fs-as-cards");
    cards.slice(0, 4).forEach(function (c) {
      if (!c || !GUID.test(c.id || "")) return;
      var url = safeUrl(c.url);
      var card = el("div", "fs-as-card");
      if (c.image && safeUrl(c.image)) {
        var img = el("img");
        img.src = c.image;
        img.alt = c.name || "";
        img.loading = "lazy";
        img.width = 134;
        img.height = 100;
        card.appendChild(img);
      }
      var name = el("a", "fs-as-name", c.name || "");
      if (url) name.href = url;
      card.appendChild(name);
      var price = el("div", "fs-as-price", money(c.finalPrice));
      if (c.discount > 0) {
        price.appendChild(el("span", "fs-as-old", Math.round(c.price).toLocaleString("fa-IR")));
        card.appendChild(el("span", "fs-as-off", c.discount.toLocaleString("fa-IR") + "٪ تخفیف"));
      }
      card.appendChild(price);
      if (c.inStock) {
        var add = el("button", "fs-as-add", "افزودن به سبد");
        add.type = "button";
        add.addEventListener("click", function () { ask("افزودن «" + (c.name || "") + "» به سبد", c.id); });
        card.appendChild(add);
      } else {
        card.appendChild(el("span", "fs-as-out", "ناموجود"));
      }
      box.appendChild(card);
    });
    return box;
  }

  function renderActions(actions) {
    var box = el("div", "fs-as-actions");
    actions.slice(0, 6).forEach(function (a) {
      if (!a || !a.label) return;
      var url = safeUrl(a.url);
      if (url) {
        var link = el("a", "fs-as-chip", a.label);
        link.href = url;
        box.appendChild(link);
      } else if (a.say) {
        var chip = el("button", "fs-as-chip", a.label);
        chip.type = "button";
        chip.addEventListener("click", function () { ask(a.say); });
        box.appendChild(chip);
      }
    });
    return box;
  }

  function render(entry) {
    var node = el("div", "fs-as-msg " + (entry.me ? "fs-as-me" : "fs-as-bot") + (entry.wait ? " fs-as-wait" : ""), entry.text || "");
    log.appendChild(node);
    if (entry.cards && entry.cards.length) log.appendChild(renderCards(entry.cards));
    if (entry.actions && entry.actions.length) log.appendChild(renderActions(entry.actions));
    log.scrollTop = log.scrollHeight;
    return node;
  }

  function remember(entry) {
    history.push(entry);
    if (history.length > MAX_LOG) history = history.slice(-MAX_LOG);
    write(LOG_KEY, history);
  }

  function add(entry) {
    remember(entry);
    return render(entry);
  }

  function greet() {
    add({
      text: "سلام! من دستیار فروشگاه هستم. نام کالا را بنویسید یا دربارهٔ قیمت، موجودی، ارسال و سفارشتان بپرسید.",
      actions: [
        { label: "کالاهای پرفروش", say: "کالاهای پرفروش" },
        { label: "سبد خرید من", say: "سبد خرید من" },
        { label: "پیگیری سفارش", say: "سفارشم کجاست" },
      ],
    });
  }

  // ── ارتباط با سرور ──────────────────────────────────────────
  function token(fresh) {
    if (!tokenPromise || fresh) {
      tokenPromise = fetch("/api/antiforgery", { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } })
        .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error("token")); })
        .then(function (d) { return d.token; })
        .catch(function (e) { tokenPromise = null; throw e; });
    }
    return tokenPromise;
  }

  function post(body, retried) {
    return token(retried).then(function (value) {
      return fetch("/api/assistant/message", {
        method: "POST",
        credentials: "same-origin",
        headers: { "Content-Type": "application/json", Accept: "application/json", RequestVerificationToken: value },
        body: JSON.stringify(body),
      });
    }).then(function (response) {
      if (response.status === 400 && !retried) return post(body, true);
      if (response.status === 429) return { text: "پیام‌ها زیاد شد؛ لطفاً یک دقیقه بعد دوباره بپرسید." };
      return response.json().catch(function () { return {}; }).then(function (data) {
        if (!response.ok) return { text: data.error || "ببخشید، مشکلی پیش آمد. دوباره امتحان کنید." };
        return data;
      });
    });
  }

  function poll(tokenValue, waitNode, tries) {
    if (!/^[a-f0-9]{32}$/.test(tokenValue || "")) return;
    window.setTimeout(function () {
      fetch("/api/assistant/lookup/" + tokenValue, { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } })
        .then(function (r) { return r.ok ? r.json() : { done: true, text: null }; })
        .then(function (data) {
          if (!data.done && tries < 12) { poll(tokenValue, waitNode, tries + 1); return; }
          if (waitNode && waitNode.parentNode) waitNode.parentNode.removeChild(waitNode);
          add({ text: data.text || "متأسفانه جواب مطمئنی پیدا نکردم؛ پرسش شما ثبت شد." });
        })
        .catch(function () {
          if (waitNode && waitNode.parentNode) waitNode.parentNode.removeChild(waitNode);
        });
    }, 1500);
  }

  function ask(text, actionId) {
    text = (text || "").trim().slice(0, 300);
    if (!text || busy) return;
    busy = true;
    send.disabled = true;
    add({ me: true, text: text });
    var waitNode = render({ text: "…", wait: true });
    var body = { text: text, session: sessionId(), last: read(LAST_KEY, []) };
    var product = currentProduct();
    if (product) body.productId = product;
    if (actionId && GUID.test(actionId)) body.action = actionId;
    post(body, false)
      .then(function (data) {
        if (waitNode.parentNode) waitNode.parentNode.removeChild(waitNode);
        var cards = Array.isArray(data.cards) ? data.cards : [];
        var actions = Array.isArray(data.actions) ? data.actions : [];
        if (cards.length) write(LAST_KEY, cards.map(function (c) { return c.id; }).filter(function (id) { return GUID.test(id || ""); }).slice(0, 6));
        add({ text: data.text || "", cards: cards, actions: actions });
        if (data.lookup) poll(data.lookup, render({ text: "لطفاً چند لحظه صبر کنید…", wait: true }), 0);
        if (data.cartChanged) document.dispatchEvent(new CustomEvent("fs:cart-changed"));
      })
      .catch(function () {
        if (waitNode.parentNode) waitNode.parentNode.removeChild(waitNode);
        add({ text: "اتصال برقرار نشد؛ لطفاً دوباره امتحان کنید." });
      })
      .then(function () {
        busy = false;
        send.disabled = false;
        input.focus();
      });
  }

  // ── باز و بسته کردن ─────────────────────────────────────────
  var rendered = false;
  function open() {
    panel.hidden = false;
    document.body.classList.add("fs-as-open");
    button.setAttribute("aria-expanded", "true");
    write(OPEN_KEY, true);
    if (!rendered) {
      rendered = true;
      if (history.length) history.forEach(render); else greet();
    }
    log.scrollTop = log.scrollHeight;
    input.focus();
  }
  function hide() {
    panel.hidden = true;
    document.body.classList.remove("fs-as-open");
    button.setAttribute("aria-expanded", "false");
    write(OPEN_KEY, false);
    button.focus();
  }

  button.addEventListener("click", open);
  close.addEventListener("click", hide);
  panel.addEventListener("keydown", function (event) { if (event.key === "Escape") hide(); });
  form.addEventListener("submit", function (event) {
    event.preventDefault();
    var value = input.value;
    input.value = "";
    ask(value);
  });
  // فقط روی دسکتاپ پنل بازمانده دوباره باز می‌شود (روی موبایل تمام‌صفحه است و نباید جلوی صفحه را بگیرد).
  if (read(OPEN_KEY, false) && window.matchMedia && window.matchMedia("(min-width: 641px)").matches) open();
})();
