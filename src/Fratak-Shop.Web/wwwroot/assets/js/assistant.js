/*
 * ویجت «دستیار هوشمند فروشگاه» (assets/js/assistant.js) – دور ۲۳، بازنویسی کامل در دور ۲۴.
 * دکمهٔ ربات پایین صفحه ← پنل گفت‌وگو؛ همهٔ صفحه‌های فروشگاه (دسکتاپ و موبایل).
 *
 * چه چیزهایی دارد (دور ۲۴):
 *  • هویت روشن: آیکون ربات + برچسب «نام · دستیار هوشمند» و سربرگ «دستیار هوشمند (پاسخ خودکار)».
 *  • جلب توجه بی‌مزاحمت: موج کوتاه دور دکمه بعد از بارگذاری و یک حباب خوش‌آمد در هر نشست مرورگر (قابل خاموش کردن از تنظیمات هوشمند).
 *  • اعلان هر پاسخ: اگر پنل بسته باشد ← نشان عدد + پیش‌نمایش پاسخ کنار دکمه؛ اگر برگه پنهان باشد ← «(۱)» در عنوان برگه.
 *  • انیمیشن تایپ: سه نقطه هنگام انتظار و نوشته شدن تدریجی پاسخ (با «کاهش حرکت» سیستم‌عامل خاموش می‌شود).
 *  • پرسش‌های پیشنهادی هر صفحه (کالا، دسته، سبد، تسویه…) که با رفتن به صفحهٔ دیگر تازه می‌شوند: GET /api/assistant/suggest.
 *  • راهنمای زنده: سرور قدم‌هایی مثل «برو به /cart»، «دکمهٔ ادامهٔ خرید را نشان بده» می‌فرستد؛ قدم‌ها در sessionStorage می‌مانند و
 *    بعد از رفتن به صفحهٔ بعد ادامه پیدا می‌کنند. فقط مسیر داخلی و فقط عنصرهای [data-assistant] فهرست سفید. پرداخت را همیشه خود مشتری می‌زند.
 *  • حافظهٔ گفت‌وگو: آخرین جست‌وجو (برای «داریم؟») و آخرین کارت‌ها (برای «اولی رو اضافه کن»).
 *  • سلیقهٔ لحن مهمان («منو علی صدا کن») در localStorage؛ برای کاربر واردشده روی سرور.
 *  • گفت‌وگو با کارشناس: نوار «در حال گفت‌وگو با کارشناس» و گرفتن پیام‌های کارشناس هر ۵ ثانیه (فقط وقتی برگه دیده می‌شود).
 *
 * سبکی و امنیت:
 *  • تا مشتری پنل را باز نکند فقط یک درخواست سبک (پیشنهادهای صفحه) در هر نشست برای حباب خوش‌آمد فرستاده می‌شود.
 *  • همهٔ متن‌ها با textContent نوشته می‌شوند (هیچ HTML از سرور درج نمی‌شود)؛ لینک‌ها فقط مسیر داخلی (/…).
 *  • رمز عبور هرگز پر یا خوانده نمی‌شود؛ فقط کادر موبایل (رقم) پر می‌شود.
 *
 * دور ۲۹:
 *  • جای دکمه: پیش‌فرض را مدیر انتخاب می‌کند (پایین/وسط، راست/چپ)؛ مشتری می‌تواند دکمه را بکشد و هر جا بگذارد (در همین مرورگر می‌ماند).
 *    کشیدن با موس و انگشت (Pointer Events)؛ حرکت کمتر از ۶ پیکسل = کلیک عادی. دکمهٔ «جای پیش‌فرض» در پایین پنجره برمی‌گرداند.
 *  • هویت از پنل مدیریت: نام، زیرعنوان، لوگو (فقط مسیر داخلی) و رنگ ← GET /api/assistant/look (کش ۵ دقیقه + localStorage تا صفحهٔ بعد بی‌پرش باشد).
 *  • پیام صوتی: دکمهٔ میکروفون کنار «ارسال» (تشخیص گفتار خود مرورگر، فارسی، رایگان)؛ متن شنیده‌شده خودکار فرستاده می‌شود.
 *  • پاسخ صوتی: دکمهٔ بلندگو در سربرگ؛ پاسخ متنی با صدای فارسی خود مرورگر هم خوانده می‌شود. اگر مشتری با صدا پرسیده باشد، همان پاسخ خودکار خوانده می‌شود.
 *    هر کدام فقط وقتی مدیر روشنش کرده و مرورگر پشتیبانی می‌کند دیده می‌شود.
 *
 * دور ۳۰ (#۲) – «پاسخ صوتی کار نمی‌کرد»؛ سه علت پیدا و رفع شد:
 *  ۱) بیشتر مرورگرها (کروم دسکتاپ، فایرفاکس، بسیاری از گوشی‌ها) صدای فارسی ندارند و کد قبلی بی‌صدا برمی‌گشت.
 *     حالا اگر صدای فارسی خود مرورگر بود از همان استفاده می‌شود (اج و اندروید صدای طبیعی دارند)، وگرنه موتور آفلاین و رایگان
 *     eSpeak-NG (WebAssembly، فقط فارسی/انگلیسی، ۸۳۰ کیلوبایت فشرده) «فقط بار اول نیاز» دانلود می‌شود و متن را می‌خواند.
 *  ۲) کروم و سافاری پخش صدا بدون «اشارهٔ کاربر» را رد می‌کنند؛ حالا با اولین کلیک/Enter داخل پنل، پخش‌کننده «باز» می‌شود
 *     (صدای خالی کوتاه + resume موتور گفتار) و پاسخ‌های بعدی بدون مانع پخش می‌شوند.
 *  ۳) تنظیمات ظاهر/صدا تا پایان نشست مرورگر کش می‌ماند؛ اگر مدیر پاسخ صوتی را روشن می‌کرد، مشتری تا نشست بعد نمی‌دید.
 *     حالا در هر بار باز شدن صفحه (بی‌صدا و در پس‌زمینه) تازه گرفته می‌شود.
 * API: Web/Infrastructure/Assistant/AssistantEndpoints.cs
 */
(function () {
  "use strict";
  if (window.__fsAssistant || !window.fetch || !document.body) return;
  window.__fsAssistant = true;

  var LOG_KEY = "fs.assist.log";
  var OPEN_KEY = "fs.assist.open";
  var LAST_KEY = "fs.assist.last";
  var QUERY_KEY = "fs.assist.query";
  var STEPS_KEY = "fs.assist.steps";
  var HANDOFF_KEY = "fs.assist.handoff";
  var AFTER_KEY = "fs.assist.after";
  var BUBBLE_KEY = "fs.assist.bubble";
  var SID_KEY = "fs.assist.sid";
  var STYLE_KEY = "fs.assist.style";
  var NAME_KEY = "fs.assist.name";
  var MAX_LOG = 40;
  var GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  var FLOW = /^finder(?::[a-z0-9-]{0,100})?(?::\d)?(?::[a-z]{2,8})?$/;
  var TARGETS = ["add-to-cart", "cart-summary", "checkout-button", "address", "order-summary", "pay-button", "login-phone", "login-password", "otp-phone", "order-cancel", "order-return", "order-invoice"];
  var FILLABLE = ["login-phone", "otp-phone"];
  var STEP_TTL = 3 * 60 * 1000;
  var reduced = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  // ── ابزارهای کوچک ───────────────────────────────────────────
  function store(kind) {
    try { return kind === "local" ? window.localStorage : window.sessionStorage; } catch (e) { return null; }
  }
  function read(key, fallback, kind) {
    var s = store(kind || "session");
    try { var v = s && s.getItem(key); return v ? JSON.parse(v) : fallback; } catch (e) { return fallback; }
  }
  function write(key, value, kind) {
    var s = store(kind || "session");
    try { if (s) { if (value == null) s.removeItem(key); else s.setItem(key, JSON.stringify(value)); } } catch (e) { /* حافظهٔ پر یا حالت خصوصی */ }
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
    return typeof url === "string" && url.length <= 300 && url.charAt(0) === "/" && url.charAt(1) !== "/" && url.charAt(1) !== "\\" ? url : null;
  }
  function currentProduct() {
    var node = document.querySelector("[data-product-view]");
    var id = node && node.getAttribute("data-product-view");
    return id && GUID.test(id) ? id : null;
  }
  function pagePath() {
    return (location.pathname + location.search).slice(0, 300);
  }
  function isMobile() {
    return !(window.matchMedia && window.matchMedia("(min-width: 641px)").matches);
  }
  function icon(d, size) {
    var ns = "http://www.w3.org/2000/svg";
    var svg = document.createElementNS(ns, "svg");
    svg.setAttribute("viewBox", "0 0 24 24");
    svg.setAttribute("aria-hidden", "true");
    if (size) { svg.setAttribute("width", size); svg.setAttribute("height", size); }
    var path = document.createElementNS(ns, "path");
    path.setAttribute("d", d);
    svg.appendChild(path);
    return svg;
  }
  // آیکون ربات (سر با آنتن، دو چشم و دهان) تا معلوم باشد چت‌بات است، نه پشتیبان انسانی.
  var ROBOT = "M12 2a1.5 1.5 0 0 1 .75 2.8V6H17a3 3 0 0 1 3 3v1h.5a1.5 1.5 0 0 1 1.5 1.5v3a1.5 1.5 0 0 1-1.5 1.5H20v1a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3v-1h-.5A1.5 1.5 0 0 1 2 14.5v-3A1.5 1.5 0 0 1 3.5 10H4V9a3 3 0 0 1 3-3h4.25V4.8A1.5 1.5 0 0 1 12 2zM8.75 10.5a1.75 1.75 0 1 0 0 3.5 1.75 1.75 0 0 0 0-3.5zm6.5 0a1.75 1.75 0 1 0 0 3.5 1.75 1.75 0 0 0 0-3.5zM9 16.25a.75.75 0 0 0 0 1.5h6a.75.75 0 0 0 0-1.5H9z";

  var botName = (function () {
    var n = read(NAME_KEY, null, "local");
    return typeof n === "string" && n.length > 0 && n.length <= 20 ? n : "آوا";
  })();

  // ── ساخت ویجت ───────────────────────────────────────────────
  var launcher = el("div", "fs-as-launch");
  var button = el("button", "fs-as-btn");
  button.type = "button";
  button.setAttribute("aria-haspopup", "dialog");
  button.setAttribute("aria-expanded", "false");
  var buttonIcon = icon(ROBOT);
  button.appendChild(buttonIcon);
  var badge = el("span", "fs-as-badge");
  badge.hidden = true;
  button.appendChild(badge);
  var tag = el("button", "fs-as-tag");
  tag.type = "button";
  tag.setAttribute("tabindex", "-1");
  var bubble = el("div", "fs-as-bubble");
  bubble.hidden = true;
  bubble.setAttribute("role", "status");
  var bubbleText = el("span", "fs-as-bubble-text");
  var bubbleClose = el("button", "fs-as-bubble-x", "×");
  bubbleClose.type = "button";
  bubbleClose.setAttribute("aria-label", "بستن پیام");
  bubble.appendChild(bubbleText);
  bubble.appendChild(bubbleClose);
  launcher.appendChild(bubble);
  launcher.appendChild(tag);
  launcher.appendChild(button);

  var panel = el("section", "fs-as-panel");
  panel.hidden = true;
  panel.setAttribute("role", "dialog");
  var head = el("div", "fs-as-head");
  var avatar = el("span", "fs-as-avatar");
  var avatarIcon = icon(ROBOT, 22);
  avatar.appendChild(avatarIcon);
  var title = el("b");
  var titleName = el("span");
  var subtitle = el("small", null, "دستیار هوشمند فروشگاه · پاسخ خودکار");
  title.appendChild(titleName);
  title.appendChild(subtitle);
  var close = el("button", "fs-as-close", "×");
  close.type = "button";
  close.setAttribute("aria-label", "بستن");
  // دور ۲۹: بلندگو (پاسخ صوتی) – فقط وقتی مدیر روشن کرده و مرورگر صدای فارسی دارد.
  var SPEAKER_ON = "M3 9v6h4l5 5V4L7 9H3zm13.5 3A4.5 4.5 0 0 0 14 7.97v8.05A4.48 4.48 0 0 0 16.5 12zM14 3.23v2.06a7 7 0 0 1 0 13.42v2.06A9 9 0 0 0 14 3.23z";
  var SPEAKER_OFF = "M16.5 12A4.5 4.5 0 0 0 14 7.97v2.21l2.45 2.45c.03-.2.05-.41.05-.63zM19 12c0 .94-.2 1.82-.54 2.64l1.51 1.51A8.8 8.8 0 0 0 21 12a9 9 0 0 0-7-8.77v2.06A7 7 0 0 1 19 12zM4.27 3 3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06a9 9 0 0 0 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4 9.91 6.09 12 8.18V4z";
  var speak = el("button", "fs-as-speak");
  speak.type = "button";
  speak.hidden = true;
  head.appendChild(avatar);
  head.appendChild(title);
  head.appendChild(speak);
  head.appendChild(close);
  var banner = el("div", "fs-as-banner");
  banner.hidden = true;
  var bannerText = el("span", null, "در حال گفت‌وگو با کارشناس پشتیبانی…");
  var bannerEnd = el("button", null, "پایان گفت‌وگو");
  bannerEnd.type = "button";
  banner.appendChild(bannerText);
  banner.appendChild(bannerEnd);
  var guideBar = el("div", "fs-as-guide");
  guideBar.hidden = true;
  var guideText = el("span", null, "راهنمای قدم‌به‌قدم در حال اجراست…");
  var guideStop = el("button", null, "توقف");
  guideStop.type = "button";
  guideBar.appendChild(guideText);
  guideBar.appendChild(guideStop);
  var log = el("div", "fs-as-log");
  log.setAttribute("role", "log");
  log.setAttribute("aria-live", "polite");
  var chipsBar = el("div", "fs-as-suggest");
  var form = el("form", "fs-as-form");
  var input = el("input");
  input.type = "text";
  input.maxLength = 300;
  input.autocomplete = "off";
  input.placeholder = "سؤالتان را بنویسید…";
  input.setAttribute("aria-label", "پیام شما");
  var send = el("button", null, "ارسال");
  send.type = "submit";
  // دور ۲۹: میکروفون (پیام صوتی) – فقط وقتی مدیر روشن کرده و مرورگر تشخیص گفتار دارد.
  var MIC = "M12 15a3 3 0 0 0 3-3V6a3 3 0 0 0-6 0v6a3 3 0 0 0 3 3zm5-3a5 5 0 0 1-10 0H5a7 7 0 0 0 6 6.92V21h2v-2.08A7 7 0 0 0 19 12h-2z";
  var mic = el("button", "fs-as-mic");
  mic.type = "button";
  mic.hidden = true;
  mic.setAttribute("aria-label", "پیام صوتی");
  mic.title = "پیام صوتی (صحبت کنید)";
  mic.appendChild(icon(MIC, 20));
  form.appendChild(input);
  form.appendChild(mic);
  form.appendChild(send);
  var note = el("div", "fs-as-note", "دستیار خودکار؛ برای صحبت با کارشناس بنویسید «کارشناس».");
  var resetSpot = el("button", "fs-as-reset", "برگرداندن دکمه به جای پیش‌فرض");
  resetSpot.type = "button";
  resetSpot.hidden = true;
  note.appendChild(resetSpot);
  panel.appendChild(head);
  panel.appendChild(banner);
  panel.appendChild(guideBar);
  panel.appendChild(log);
  panel.appendChild(chipsBar);
  panel.appendChild(form);
  panel.appendChild(note);
  document.body.appendChild(launcher);
  document.body.appendChild(panel);

  function applyName(name) {
    if (typeof name === "string" && name.length > 0 && name.length <= 20) {
      botName = name;
      write(NAME_KEY, name, "local");
    }
    tag.textContent = botName + " · دستیار هوشمند";
    titleName.textContent = botName;
    button.setAttribute("aria-label", botName + "، دستیار هوشمند فروشگاه (چت‌بات)");
    panel.setAttribute("aria-label", "گفت‌وگو با " + botName + "، دستیار هوشمند فروشگاه");
  }
  applyName(botName);

  // ── دور ۲۹: ظاهر از پنل مدیریت ───────────────────────────────
  var LOOK_KEY = "fs.assist.look";
  var SPOT_KEY = "fs.assist.spot";
  var SPEAK_KEY = "fs.assist.speak";
  var POSITIONS = ["br", "bl", "mr", "ml"];
  var Recognition = window.SpeechRecognition || window.webkitSpeechRecognition || null;
  var synth = window.speechSynthesis || null;
  var look = read(LOOK_KEY, null, "local") || {};
  var logoImg = null;

  function setLogo(url) {
    var safe = safeUrl(url);
    if (!safe) {
      if (logoImg) { remove(logoImg.button); remove(logoImg.avatar); logoImg = null; }
      buttonIcon.style.display = "";
      avatarIcon.style.display = "";
      return;
    }
    if (logoImg && logoImg.src === safe) return;
    if (logoImg) { remove(logoImg.button); remove(logoImg.avatar); }
    function img(cls) {
      var node = el("img", cls);
      node.alt = "";
      node.decoding = "async";
      node.src = safe;
      node.addEventListener("error", function () { setLogo(null); }, { once: true }); // لوگوی خراب ← همان آیکون ربات
      return node;
    }
    logoImg = { src: safe, button: img("fs-as-logo"), avatar: img("fs-as-logo") };
    buttonIcon.style.display = "none";
    avatarIcon.style.display = "none";
    button.insertBefore(logoImg.button, badge);
    avatar.appendChild(logoImg.avatar);
  }

  function applyLook(l) {
    look = l && typeof l === "object" ? l : {};
    var pos = POSITIONS.indexOf(look.position) >= 0 ? look.position : "br";
    [launcher, panel].forEach(function (node) {
      POSITIONS.forEach(function (p) { node.classList.remove("fs-as-pos-" + p); });
      node.classList.add("fs-as-pos-" + pos);
    });
    // رنگ روی ریشهٔ صفحه تا راهنمای شناور (fs-as-tip) هم همان رنگ را بگیرد؛ فقط هگز ۶ رقمی (از CSSOM، نه HTML).
    var root = document.documentElement;
    if (typeof look.color === "string" && /^#[0-9a-f]{6}$/i.test(look.color)) root.style.setProperty("--fs-as-color", look.color);
    else root.style.removeProperty("--fs-as-color");
    if (typeof look.subtitle === "string" && look.subtitle.length > 0 && look.subtitle.length <= 40) subtitle.textContent = look.subtitle;
    if (typeof look.name === "string") applyName(look.name);
    setLogo(look.logo);
    mic.hidden = !(look.voiceIn !== false && Recognition);
    updateSpeak();
    if (look.draggable === false) write(SPOT_KEY, null, "local");
    placeSpot();
  }

  // دور ۳۰: هر بار باز شدن صفحه تازه گرفته می‌شود (پاسخ کوچک و کش‌شده در سرور است)؛ نسخهٔ localStorage فقط برای نمایش بی‌پرش اولیه است.
  function loadLook() {
    write("fs.assist.look.fresh", null); // کلید قدیمی دور ۲۹
    fetch("/api/assistant/look", { credentials: "same-origin", cache: "no-cache", headers: { Accept: "application/json" } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) {
        if (!data || typeof data !== "object") return;
        write(LOOK_KEY, data, "local");
        applyLook(data);
      })
      .catch(function () { /* بی‌اهمیت؛ ظاهر قبلی می‌ماند */ });
  }

  // ── دور ۲۹: کشیدن دکمه ──────────────────────────────────────
  // جای انتخابی به‌صورت «نسبت» ذخیره می‌شود (۰ تا ۱ از عرض/ارتفاع آزاد) تا با چرخاندن گوشی یا تغییر اندازهٔ پنجره هم داخل صفحه بماند.
  var drag = null;
  var suppressClick = false;
  function placeSpot() {
    var spot = look.draggable === false ? null : read(SPOT_KEY, null, "local");
    var valid = spot && typeof spot.x === "number" && typeof spot.y === "number" && spot.x >= 0 && spot.x <= 1 && spot.y >= 0 && spot.y <= 1;
    resetSpot.hidden = !valid;
    if (!valid) {
      launcher.classList.remove("fs-as-free");
      launcher.style.removeProperty("left");
      launcher.style.removeProperty("top");
      panel.classList.remove("fs-as-side-l", "fs-as-side-r");
      return;
    }
    var w = launcher.offsetWidth || 58;
    var h = launcher.offsetHeight || 58;
    var left = Math.round(spot.x * Math.max(0, window.innerWidth - w));
    var top = Math.round(spot.y * Math.max(0, window.innerHeight - h));
    launcher.classList.add("fs-as-free");
    launcher.style.setProperty("left", left + "px");
    launcher.style.setProperty("top", top + "px");
    // پنجرهٔ گفت‌وگو (دسکتاپ) همان سمتی باز می‌شود که دکمه هست.
    var leftSide = left + w / 2 < window.innerWidth / 2;
    panel.classList.toggle("fs-as-side-l", leftSide);
    panel.classList.toggle("fs-as-side-r", !leftSide);
  }
  button.addEventListener("pointerdown", function (event) {
    if (look.draggable === false || (event.pointerType === "mouse" && event.button !== 0)) return;
    var rect = launcher.getBoundingClientRect();
    drag = { id: event.pointerId, sx: event.clientX, sy: event.clientY, ox: event.clientX - rect.left, oy: event.clientY - rect.top, moved: false };
  });
  button.addEventListener("pointermove", function (event) {
    if (!drag || event.pointerId !== drag.id) return;
    if (!drag.moved && Math.abs(event.clientX - drag.sx) + Math.abs(event.clientY - drag.sy) < 6) return;
    if (!drag.moved) {
      drag.moved = true;
      try { button.setPointerCapture(event.pointerId); } catch (e) { /* مرورگر قدیمی */ }
      launcher.classList.add("fs-as-free", "fs-as-dragging");
      bubble.hidden = true;
    }
    event.preventDefault();
    var w = launcher.offsetWidth, h = launcher.offsetHeight;
    var left = Math.min(Math.max(0, event.clientX - drag.ox), window.innerWidth - w);
    var top = Math.min(Math.max(0, event.clientY - drag.oy), window.innerHeight - h);
    launcher.style.setProperty("left", left + "px");
    launcher.style.setProperty("top", top + "px");
  });
  function endDrag(event) {
    if (!drag || event.pointerId !== drag.id) return;
    var moved = drag.moved;
    drag = null;
    launcher.classList.remove("fs-as-dragging");
    if (!moved) return;
    suppressClick = true; // کلیکی که بعد از رها کردن می‌آید پنجره را باز نکند
    window.setTimeout(function () { suppressClick = false; }, 400);
    var rect = launcher.getBoundingClientRect();
    var freeW = Math.max(1, window.innerWidth - rect.width);
    var freeH = Math.max(1, window.innerHeight - rect.height);
    write(SPOT_KEY, { x: Math.min(1, Math.max(0, rect.left / freeW)), y: Math.min(1, Math.max(0, rect.top / freeH)) }, "local");
    placeSpot();
  }
  button.addEventListener("pointerup", endDrag);
  button.addEventListener("pointercancel", endDrag);
  resetSpot.addEventListener("click", function () { write(SPOT_KEY, null, "local"); placeSpot(); });
  var resizeTimer = 0;
  window.addEventListener("resize", function () { window.clearTimeout(resizeTimer); resizeTimer = window.setTimeout(placeSpot, 150); });

  // ── دور ۲۹ + ۳۰: پاسخ صوتی ──────────────────────────────────
  // ترتیب: صدای فارسی خود مرورگر (اگر هست) ← موتور آفلاین eSpeak-NG (WebAssembly). هر دو رایگان و بدون سرویس بیرونی.
  var ESPEAK_DIR = "/assets/vendor/espeak/";
  var ESPEAK_VER = "?v=102fa1"; // نسخهٔ فایل‌ها (کش یک‌ساله؛ با تغییر فایل این عدد عوض شود)
  var canEspeak = typeof WebAssembly === "object" && typeof DecompressionStream === "function" && typeof Response === "function"
    && typeof Blob === "function" && typeof URL === "function" && typeof URL.createObjectURL === "function" && typeof Audio === "function";
  var espeak = null;      // Promise<{ factory, module }> – فقط یک بار دانلود و کامپایل می‌شود
  var player = null;      // یک عنصر Audio برای همهٔ پاسخ‌ها (سافاری فقط عنصری را که با اشارهٔ کاربر باز شده پخش می‌کند)
  var playerUrl = null;
  var utterance = null;   // نگه داشتن ارجاع (باگ کروم: گفتاری که ارجاع ندارد وسط کار قطع می‌شود)
  var audioUnlocked = false;

  function nativeVoice() {
    if (!synth) return null;
    var voices = [];
    try { voices = synth.getVoices() || []; } catch (e) { voices = []; }
    for (var i = 0; i < voices.length; i++) {
      var v = voices[i];
      if (/^fa([-_]|$)/i.test(v.lang || "") || /persian|farsi|فارسی/i.test(v.name || "")) return v;
    }
    return null;
  }
  function voiceAvailable() { return canEspeak || !!nativeVoice(); }
  function speakOn() { return read(SPEAK_KEY, false, "local") === true; }

  function updateSpeak() {
    var allowed = look.voiceOut === true && (canEspeak || !!synth);
    speak.hidden = !allowed;
    if (!allowed) return;
    var ready = voiceAvailable();
    while (speak.firstChild) speak.removeChild(speak.firstChild);
    var on = speakOn() && ready;
    speak.appendChild(icon(on ? SPEAKER_ON : SPEAKER_OFF, 20));
    speak.setAttribute("aria-pressed", on ? "true" : "false");
    speak.disabled = !ready;
    var label = !ready ? "مرورگر شما پخش صدای فارسی را پشتیبانی نمی‌کند" : on ? "خاموش کردن پاسخ صوتی" : "روشن کردن پاسخ صوتی";
    speak.setAttribute("aria-label", label);
    speak.title = label;
  }
  if (synth && typeof synth.addEventListener === "function") synth.addEventListener("voiceschanged", updateSpeak);

  // متن قابل خواندن: بدون نشانی اینترنتی، شکلک و نمادهای فهرست؛ حداکثر ~۴۰۰ نویسه تا آخرین جملهٔ کامل.
  function speakable(text) {
    var t = String(text || "")
      .replace(/https?:\/\/\S+/g, " ")
      .replace(/[\u2190-\u21FF\u2600-\u27BF\u2B00-\u2BFF]|[\uD83C-\uDBFF][\uDC00-\uDFFF]/g, " ")
      .replace(/[*_#`~•|<>\[\]{}]/g, " ")
      .replace(/\s+/g, " ").trim();
    if (t.length <= 400) return t;
    var cut = t.slice(0, 400);
    var end = Math.max(cut.lastIndexOf("."), cut.lastIndexOf("؟"), cut.lastIndexOf("!"), cut.lastIndexOf("؛"));
    return (end > 120 ? cut.slice(0, end + 1) : cut).trim();
  }

  // یک فایل WAV بی‌صدای خیلی کوتاه (برای باز کردن قفل پخش در اولین اشارهٔ کاربر؛ data: در CSP مجاز نیست، پس Blob).
  function silentWav() {
    var samples = 800, buf = new ArrayBuffer(44 + samples * 2), d = new DataView(buf);
    function str(o, s) { for (var i = 0; i < s.length; i++) d.setUint8(o + i, s.charCodeAt(i)); }
    str(0, "RIFF"); d.setUint32(4, 36 + samples * 2, true); str(8, "WAVE"); str(12, "fmt ");
    d.setUint32(16, 16, true); d.setUint16(20, 1, true); d.setUint16(22, 1, true); d.setUint32(24, 16000, true);
    d.setUint32(28, 32000, true); d.setUint16(32, 2, true); d.setUint16(34, 16, true); str(36, "data"); d.setUint32(40, samples * 2, true);
    return new Blob([buf], { type: "audio/wav" });
  }
  function audioEl() {
    if (!player && typeof Audio === "function") { player = new Audio(); player.preload = "auto"; }
    return player;
  }
  function playBlob(blob) {
    var a = audioEl();
    if (!a) return;
    if (playerUrl) { try { URL.revokeObjectURL(playerUrl); } catch (e) { /* بی‌اهمیت */ } }
    playerUrl = URL.createObjectURL(blob);
    a.src = playerUrl;
    var p = a.play();
    if (p && typeof p.catch === "function") p.catch(function () { audioUnlocked = false; });
  }
  function stopSpeaking() {
    if (player) { try { player.pause(); } catch (e) { /* بی‌اهمیت */ } }
    if (synth) { try { synth.cancel(); } catch (e) { /* بی‌اهمیت */ } }
  }

  // اولین اشارهٔ کاربر داخل پنل (کلیک، لمس، Enter): پخش‌کننده باز، موتور گفتار بیدار و اگر لازم است موتور آفلاین پیش‌بارگذاری می‌شود.
  function unlockAudio() {
    if (look.voiceOut !== true) return;
    if (!audioUnlocked) {
      audioUnlocked = true;
      if (canEspeak) {
        var a = audioEl();
        if (a) {
          var url = URL.createObjectURL(silentWav());
          a.src = url;
          var p = a.play();
          if (p && typeof p.catch === "function") p.catch(function () { audioUnlocked = false; });
        }
      }
      if (synth) {
        try { synth.resume(); synth.speak(new SpeechSynthesisUtterance(" ")); } catch (e) { /* بی‌اهمیت */ }
      }
    }
    if (canEspeak && !nativeVoice() && (speakOn() || voiceAsked)) loadEspeak().catch(function () { /* بعداً دوباره */ });
  }

  function loadEspeak() {
    if (!espeak) {
      espeak = Promise.all([
        import(ESPEAK_DIR + "espeak-ng.js" + ESPEAK_VER).then(function (m) { return m["default"]; }),
        fetch(ESPEAK_DIR + "espeak-fa.wasm.gz" + ESPEAK_VER, { credentials: "same-origin" })
          .then(function (r) {
            if (!r.ok || !r.body) throw new Error("espeak " + r.status);
            return new Response(r.body.pipeThrough(new DecompressionStream("gzip"))).arrayBuffer();
          })
          .then(function (bytes) { return WebAssembly.compile(bytes); }),
      ]).then(function (parts) { return { factory: parts[0], module: parts[1] }; });
      espeak.catch(function () { espeak = null; });
    }
    return espeak;
  }

  // خواندن با eSpeak-NG: هر بار یک نمونهٔ تازه از همان ماژول کامپایل‌شده (حدود ۱۰۰ میلی‌ثانیه) و پخش WAV حاصل.
  function espeakSay(text) {
    return loadEspeak().then(function (engine) {
      return engine.factory({
        arguments: ["-v", "fa", "-s", "160", "-w", "/out.wav", text],
        print: function () { /* خروجی متنی لازم نیست */ },
        printErr: function () { /* خروجی متنی لازم نیست */ },
        locateFile: function (path) { return ESPEAK_DIR + path; },
        instantiateWasm: function (imports, done) {
          WebAssembly.instantiate(engine.module, imports).then(function (instance) { done(instance, engine.module); });
          return {};
        },
      });
    }).then(function (instance) {
      playBlob(new Blob([instance.FS.readFile("/out.wav")], { type: "audio/wav" }));
    });
  }

  speak.addEventListener("click", function () {
    var turnOn = !speakOn();
    write(SPEAK_KEY, turnOn, "local");
    unlockAudio();
    if (!turnOn) stopSpeaking();
    updateSpeak();
    // بازخورد فوری: مشتری همان لحظه می‌شنود که صدا کار می‌کند.
    if (turnOn) sayAloud("پاسخ صوتی روشن شد.");
  });
  panel.addEventListener("pointerdown", unlockAudio, true);
  panel.addEventListener("keydown", function (event) { if (event.key === "Enter") unlockAudio(); }, true);

  var voiceAsked = false;
  function sayAloud(text) {
    if (look.voiceOut !== true) return;
    var clean = speakable(text);
    if (!clean) return;
    stopSpeaking();
    var voice = nativeVoice();
    if (voice && synth) {
      try {
        utterance = new SpeechSynthesisUtterance(clean);
        utterance.voice = voice;
        utterance.lang = voice.lang || "fa-IR";
        utterance.rate = 1;
        // خطای موتور مرورگر (نه قطع عمدی) ← همان متن با موتور آفلاین.
        utterance.onerror = function (event) {
          if (event && (event.error === "interrupted" || event.error === "canceled")) return;
          if (canEspeak) espeakSay(clean).catch(function () { /* بی‌صدا */ });
        };
        synth.resume();
        synth.speak(utterance);
        return;
      } catch (e) { /* ← موتور آفلاین */ }
    }
    if (canEspeak) espeakSay(clean).catch(function () { /* شبکه/مرورگر؛ متن روی صفحه هست */ });
  }

  // ── دور ۲۹: پیام صوتی ───────────────────────────────────────
  var listening = null;
  mic.addEventListener("click", function () {
    if (!Recognition) return;
    if (listening) { try { listening.stop(); } catch (e) { /* تمام شده */ } return; }
    var rec;
    try { rec = new Recognition(); } catch (e) { return; }
    rec.lang = "fa-IR";
    rec.interimResults = true;
    rec.continuous = false;
    rec.maxAlternatives = 1;
    listening = rec;
    var finalText = "";
    var placeholder = input.placeholder;
    input.placeholder = "در حال شنیدن… صحبت کنید";
    mic.classList.add("fs-as-listening");
    mic.setAttribute("aria-pressed", "true");
    var timer = window.setTimeout(function () { try { rec.stop(); } catch (e) { /* تمام شده */ } }, 12000);
    rec.onresult = function (e) {
      var interim = "";
      for (var i = e.resultIndex; i < e.results.length; i++) {
        var part = e.results[i][0] ? e.results[i][0].transcript : "";
        if (e.results[i].isFinal) finalText += part; else interim += part;
      }
      input.value = (finalText + interim).trim().slice(0, 300);
    };
    rec.onerror = function (e) {
      if (e.error === "not-allowed" || e.error === "service-not-allowed") input.placeholder = "اجازهٔ میکروفون داده نشده است";
      else if (e.error === "no-speech") input.placeholder = "صدایی شنیده نشد؛ دوباره امتحان کنید";
    };
    rec.onend = function () {
      window.clearTimeout(timer);
      listening = null;
      mic.classList.remove("fs-as-listening");
      mic.setAttribute("aria-pressed", "false");
      window.setTimeout(function () { input.placeholder = placeholder; }, 2500);
      var text = finalText.trim();
      if (text) {
        input.value = "";
        voiceAsked = true;
        ask(text);
      }
    };
    try { rec.start(); } catch (e) { listening = null; mic.classList.remove("fs-as-listening"); }
  });

  applyLook(look);
  loadLook();

  var history = read(LOG_KEY, []);
  if (!Array.isArray(history)) history = [];
  var busy = false;
  var tokenPromise = null;
  var unread = 0;
  var baseTitle = document.title;

  // ── اعلان پاسخ وقتی پنل بسته یا برگه پنهان است ───────────────
  function notify(text) {
    if (!panel.hidden && !document.hidden) return;
    if (panel.hidden) {
      unread = Math.min(unread + 1, 9);
      badge.textContent = unread.toLocaleString("fa-IR");
      badge.hidden = false;
      showBubble(text);
      button.classList.remove("fs-as-ring");
      void button.offsetWidth;
      button.classList.add("fs-as-ring");
    }
    if (document.hidden) document.title = "(" + Math.max(unread, 1).toLocaleString("fa-IR") + ") پیام تازه · " + baseTitle;
  }
  function clearUnread() {
    unread = 0;
    badge.hidden = true;
    document.title = baseTitle;
  }
  document.addEventListener("visibilitychange", function () {
    if (!document.hidden && !panel.hidden) clearUnread();
  });
  function showBubble(text) {
    if (!text) return;
    bubbleText.textContent = text.length > 120 ? text.slice(0, 118) + "…" : text;
    bubble.hidden = false;
  }
  bubbleClose.addEventListener("click", function (event) { event.stopPropagation(); bubble.hidden = true; });
  bubble.addEventListener("click", function () { open(); });
  tag.addEventListener("click", function () { open(); });

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
      // دور ۲۵: برچسب کوتاه مشاور خرید روی کارت (مثلاً «بیشترین رضایت»).
      if (typeof c.note === "string" && c.note && c.note.length <= 40) card.appendChild(el("span", "fs-as-note", c.note));
      var name = el("a", "fs-as-name", c.name || "");
      if (url) name.href = url;
      card.appendChild(name);
      var price = el("div", "fs-as-price", money(c.finalPrice));
      if (c.discount > 0) {
        price.appendChild(el("span", "fs-as-old", Math.round(c.price).toLocaleString("fa-IR")));
        card.appendChild(el("span", "fs-as-off", Number(c.discount).toLocaleString("fa-IR") + "٪ تخفیف"));
      }
      card.appendChild(price);
      if (c.inStock) {
        var addBtn = el("button", "fs-as-add", "افزودن به سبد");
        addBtn.type = "button";
        addBtn.addEventListener("click", function () { ask("افزودن «" + (c.name || "") + "» به سبد", c.id); });
        card.appendChild(addBtn);
      } else {
        card.appendChild(el("span", "fs-as-out", "ناموجود"));
      }
      box.appendChild(card);
    });
    return box;
  }

  function chip(a) {
    if (!a || !a.label) return null;
    var url = safeUrl(a.url);
    if (url) {
      var link = el("a", "fs-as-chip", a.label);
      link.href = url;
      return link;
    }
    if (a.say) {
      var b = el("button", "fs-as-chip", a.label);
      b.type = "button";
      var flow = typeof a.flow === "string" && FLOW.test(a.flow) ? a.flow : null;
      b.addEventListener("click", function () { ask(a.say, null, flow); });
      return b;
    }
    return null;
  }

  function renderActions(actions) {
    var box = el("div", "fs-as-actions");
    actions.slice(0, 7).forEach(function (a) {
      var node = chip(a);
      if (node) box.appendChild(node);
    });
    return box;
  }

  function bubbleNode(entry) {
    var cls = "fs-as-msg " + (entry.me ? "fs-as-me" : entry.staff ? "fs-as-staff" : "fs-as-bot");
    var node = el("div", cls);
    if (entry.staff) node.appendChild(el("b", "fs-as-author", entry.author || "کارشناس"));
    node.appendChild(el("span", null, entry.text || ""));
    return node;
  }

  function scrollDown() { log.scrollTop = log.scrollHeight; }

  // ── دور ۲۵: نظر مشتری دربارهٔ پاسخ (👍/👎) ───────────────────
  // فقط برای پاسخ‌های ربات که شناسه دارند؛ بعد از رأی، دکمه‌ها جای خود را به «ممنون» می‌دهند و رأی در تاریخچه می‌ماند.
  function renderFeedback(entry) {
    if (!entry || entry.me || entry.staff || !GUID.test(entry.id || "")) return null;
    var box = el("div", "fs-as-fb");
    function thanks(helpful) {
      while (box.firstChild) box.removeChild(box.firstChild);
      box.appendChild(el("span", null, helpful ? "ممنون از نظرتان 🌱" : "ممنون؛ برای بهتر شدن جواب‌ها بررسی می‌شود."));
    }
    if (typeof entry.fb === "boolean") { thanks(entry.fb); return box; }
    box.appendChild(el("span", null, "جواب مفید بود؟"));
    [[true, "👍", "بله، مفید بود"], [false, "👎", "نه، مفید نبود"]].forEach(function (item) {
      var b = el("button", null, item[1]);
      b.type = "button";
      b.setAttribute("aria-label", item[2]);
      b.addEventListener("click", function () {
        entry.fb = item[0];
        write(LOG_KEY, history);
        thanks(item[0]);
        postJson("/api/assistant/feedback", { id: entry.id, session: sessionId(), helpful: item[0] }, false).catch(function () { });
      });
      box.appendChild(b);
    });
    return box;
  }

  function render(entry) {
    var node = bubbleNode(entry);
    log.appendChild(node);
    if (entry.cards && entry.cards.length) log.appendChild(renderCards(entry.cards));
    if (entry.actions && entry.actions.length) log.appendChild(renderActions(entry.actions));
    var fb = renderFeedback(entry);
    if (fb) log.appendChild(fb);
    scrollDown();
    return node;
  }

  // پاسخ ربات با تایپ تدریجی (حداکثر حدود ۱ ثانیه)؛ کارت‌ها و دکمه‌ها بعد از پایان تایپ.
  function typeOut(entry) {
    var node = bubbleNode(entry);
    var span = node.lastChild;
    var full = entry.text || "";
    log.appendChild(node);
    function finish() {
      span.textContent = full;
      if (entry.cards && entry.cards.length) log.appendChild(renderCards(entry.cards));
      if (entry.actions && entry.actions.length) log.appendChild(renderActions(entry.actions));
      var fb = renderFeedback(entry);
      if (fb) log.appendChild(fb);
      scrollDown();
    }
    if (reduced || panel.hidden || full.length < 2) { finish(); return; }
    var step = Math.max(2, Math.ceil(full.length / 45));
    var i = 0;
    span.textContent = "";
    var timer = window.setInterval(function () {
      i += step;
      if (i >= full.length) { window.clearInterval(timer); finish(); return; }
      span.textContent = full.slice(0, i);
      scrollDown();
    }, 22);
  }

  function typing() {
    var node = el("div", "fs-as-msg fs-as-bot fs-as-typing");
    node.setAttribute("aria-label", botName + " در حال نوشتن است");
    for (var i = 0; i < 3; i++) node.appendChild(el("i"));
    log.appendChild(node);
    scrollDown();
    return node;
  }

  function remember(entry) {
    history.push(entry);
    if (history.length > MAX_LOG) history = history.slice(-MAX_LOG);
    write(LOG_KEY, history);
  }

  function addBot(entry) {
    remember(entry);
    if (rendered) typeOut(entry);
    notify(entry.text);
    // دور ۲۹: پاسخ صوتی (اگر مشتری روشن کرده یا همین الان با صدا پرسیده).
    if (entry.text && (speakOn() || voiceAsked)) sayAloud(entry.text);
    voiceAsked = false;
  }

  function addMine(text) {
    var entry = { me: true, text: text };
    remember(entry);
    if (rendered) render(entry);
  }

  function remove(node) { if (node && node.parentNode) node.parentNode.removeChild(node); }

  // ── پرسش‌های پیشنهادی صفحه ──────────────────────────────────
  var suggestion = null;
  var suggestPromise = null;
  function loadSuggest() {
    if (suggestPromise) return suggestPromise;
    var style = read(STYLE_KEY, null, "local") || {};
    var q = "?page=" + encodeURIComponent(pagePath());
    var product = currentProduct();
    if (product) q += "&product=" + product;
    if (typeof style.callMe === "string" && style.callMe) q += "&callMe=" + encodeURIComponent(style.callMe.slice(0, 30));
    if (typeof style.tone === "number") q += "&tone=" + style.tone;
    if (typeof style.register === "number") q += "&register=" + style.register;
    suggestPromise = fetch("/api/assistant/suggest" + q, { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) {
        if (data && typeof data.name === "string") {
          suggestion = data;
          applyName(data.name);
        }
        return suggestion;
      })
      .catch(function () { return null; });
    return suggestPromise;
  }
  function renderSuggest() {
    while (chipsBar.firstChild) chipsBar.removeChild(chipsBar.firstChild);
    var chips = suggestion && Array.isArray(suggestion.chips) ? suggestion.chips : [];
    chips.slice(0, 6).forEach(function (a) {
      var node = chip(a);
      if (node) chipsBar.appendChild(node);
    });
    chipsBar.hidden = chipsBar.childNodes.length === 0;
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

  function postJson(url, body, retried) {
    return token(retried).then(function (value) {
      return fetch(url, {
        method: "POST",
        credentials: "same-origin",
        headers: { "Content-Type": "application/json", Accept: "application/json", RequestVerificationToken: value },
        body: JSON.stringify(body),
      });
    }).then(function (response) {
      if (response.status === 400 && !retried) return postJson(url, body, true);
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
          remove(waitNode);
          addBot({ text: data.text || "متأسفانه جواب مطمئنی پیدا نکردم؛ پرسش شما ثبت شد." });
        })
        .catch(function () { remove(waitNode); });
    }, 1500);
  }

  function ask(text, actionId, flow) {
    text = (text || "").trim().slice(0, 300);
    if (!text || busy) return;
    if (!rendered) open();
    busy = true;
    send.disabled = true;
    addMine(text);
    var waitNode = typing();
    var started = Date.now();
    var body = { text: text, session: sessionId(), last: read(LAST_KEY, []), page: pagePath() };
    var product = currentProduct();
    if (product) body.productId = product;
    if (actionId && GUID.test(actionId)) body.action = actionId;
    if (flow && FLOW.test(flow)) body.flow = flow;
    var lastQuery = read(QUERY_KEY, null);
    if (typeof lastQuery === "string" && lastQuery) body.lastQuery = lastQuery.slice(0, 100);
    var style = read(STYLE_KEY, null, "local");
    if (style && typeof style === "object") body.style = { callMe: typeof style.callMe === "string" ? style.callMe.slice(0, 30) : null, tone: Number(style.tone) || 0, register: Number(style.register) || 0 };
    postJson("/api/assistant/message", body, false)
      .then(function (data) {
        // کمی مکث طبیعی تا سه‌نقطه دیده شود (حداکثر ۶۰۰ میلی‌ثانیه، فقط اگر پاسخ خیلی سریع بود).
        var wait = reduced ? 0 : Math.max(0, 600 - (Date.now() - started));
        return new Promise(function (resolve) { window.setTimeout(function () { resolve(data); }, wait); });
      })
      .then(function (data) {
        remove(waitNode);
        var cards = Array.isArray(data.cards) ? data.cards : [];
        var actions = Array.isArray(data.actions) ? data.actions : [];
        if (cards.length) write(LAST_KEY, cards.map(function (c) { return c.id; }).filter(function (id) { return GUID.test(id || ""); }).slice(0, 6));
        if (typeof data.query === "string" && data.query) write(QUERY_KEY, data.query.slice(0, 100));
        if (data.style && typeof data.style === "object") write(STYLE_KEY, { callMe: data.style.callMe || null, tone: Number(data.style.tone) || 0, register: Number(data.style.register) || 0 }, "local");
        addBot({ text: data.text || "", cards: cards, actions: actions, id: GUID.test(data.messageId || "") ? data.messageId : null });
        if (data.lookup) poll(data.lookup, render({ text: "لطفاً چند لحظه صبر کنید…" }), 0);
        if (data.cartChanged) document.dispatchEvent(new CustomEvent("fs:cart-changed"));
        if (data.handoff) startHandoff();
        if (Array.isArray(data.steps) && data.steps.length) startGuide(data.steps);
      })
      .catch(function () {
        remove(waitNode);
        addBot({ text: "اتصال برقرار نشد؛ لطفاً دوباره امتحان کنید." });
      })
      .then(function () {
        busy = false;
        send.disabled = false;
        if (!panel.hidden && !isMobile()) input.focus();
      });
  }

  // ── راهنمای زنده (قدم‌ها) ────────────────────────────────────
  var spotlight = null;
  var guideTimer = 0;
  function validStep(s) {
    if (!s || typeof s.kind !== "string") return false;
    if (s.kind === "go") return !!safeUrl(s.url);
    if (s.kind === "point" || s.kind === "focus") return TARGETS.indexOf(s.target) >= 0;
    if (s.kind === "fill") return FILLABLE.indexOf(s.target) >= 0 && /^[0-9]{4,11}$/.test(s.value || "");
    return false;
  }
  function saveSteps(steps) {
    write(STEPS_KEY, steps && steps.length ? { at: Date.now(), steps: steps } : null);
    guideBar.hidden = !(steps && steps.length);
  }
  function startGuide(steps) {
    var clean = steps.filter(validStep).slice(0, 6);
    if (!clean.length) return;
    // روی موبایل پنل تمام‌صفحه است؛ بسته می‌شود تا مشتری خود صفحه و دکمهٔ روشن‌شده را ببیند.
    if (isMobile() && clean.some(function (s) { return s.kind !== "go"; })) window.setTimeout(hide, 900);
    saveSteps(clean);
    window.clearTimeout(guideTimer);
    guideTimer = window.setTimeout(runGuide, reduced ? 200 : 1100);
  }
  function stopGuide() {
    window.clearTimeout(guideTimer);
    saveSteps(null);
    clearSpot();
  }
  function samePage(url) {
    var a = document.createElement("a");
    a.href = url;
    return a.pathname === location.pathname && (a.search === "" || a.search === location.search);
  }
  function clearSpot() {
    if (spotlight) {
      spotlight.el.classList.remove("fs-as-spot");
      remove(spotlight.tip);
      spotlight = null;
    }
  }
  function spot(target, text, focus) {
    var node = document.querySelector('[data-assistant="' + target + '"]');
    if (!node || !node.getClientRects().length) return false;
    clearSpot();
    node.classList.add("fs-as-spot");
    node.scrollIntoView({ behavior: reduced ? "auto" : "smooth", block: "center" });
    var tip = el("div", "fs-as-tip", text || "");
    tip.setAttribute("role", "status");
    document.body.appendChild(tip);
    function place() {
      var r = node.getBoundingClientRect();
      var top = r.bottom + 10;
      if (top + 60 > window.innerHeight) top = Math.max(8, r.top - 56);
      tip.style.top = Math.round(top) + "px";
      tip.style.left = Math.round(Math.min(Math.max(8, r.left), window.innerWidth - 268)) + "px";
    }
    place();
    window.setTimeout(place, 450);
    spotlight = { el: node, tip: tip };
    if (focus && typeof node.focus === "function") window.setTimeout(function () { try { node.focus({ preventScroll: true }); } catch (e) { node.focus(); } }, 500);
    return true;
  }
  function runGuide() {
    var saved = read(STEPS_KEY, null);
    if (!saved || !Array.isArray(saved.steps) || !saved.steps.length || Date.now() - (saved.at || 0) > STEP_TTL) { saveSteps(null); return; }
    var steps = saved.steps.filter(validStep);
    var s = steps.shift();
    if (!s) { saveSteps(null); return; }
    if (s.kind === "go") {
      if (!samePage(s.url)) {
        saveSteps(steps);
        location.href = safeUrl(s.url);
        return;
      }
      saveSteps(steps);
      guideTimer = window.setTimeout(runGuide, 50);
      return;
    }
    saveSteps(steps);
    var delay = 2600;
    if (s.kind === "fill") {
      var field = document.querySelector('[data-assistant="' + s.target + '"]');
      if (field && "value" in field && field.type !== "password") {
        field.value = s.value;
        field.dispatchEvent(new Event("input", { bubbles: true }));
        field.dispatchEvent(new Event("change", { bubbles: true }));
      }
      delay = 150;
    } else if (!spot(s.target, s.text, s.kind === "focus")) {
      delay = 100;
    }
    if (steps.length) guideTimer = window.setTimeout(runGuide, delay);
    else guideTimer = window.setTimeout(function () { saveSteps(null); }, delay);
  }
  guideStop.addEventListener("click", function () { stopGuide(); addBot({ text: "باشه، راهنما متوقف شد." }); });
  document.addEventListener("click", function (event) {
    if (spotlight && (event.target === spotlight.el || spotlight.el.contains(event.target))) clearSpot();
  }, true);

  // ── گفت‌وگو با کارشناس ──────────────────────────────────────
  var handoffTimer = 0;
  function startHandoff() {
    write(HANDOFF_KEY, true, "local");
    if (read(AFTER_KEY, null, "local") == null) write(AFTER_KEY, Date.now() - 60000, "local");
    banner.hidden = false;
    scheduleHandoff(3000);
  }
  function endHandoff(message) {
    window.clearTimeout(handoffTimer);
    write(HANDOFF_KEY, null, "local");
    write(AFTER_KEY, null, "local");
    banner.hidden = true;
    if (message) addBot({ text: message });
  }
  function scheduleHandoff(ms) {
    window.clearTimeout(handoffTimer);
    handoffTimer = window.setTimeout(pollHandoff, ms);
  }
  function pollHandoff() {
    if (!read(HANDOFF_KEY, false, "local")) return;
    if (document.hidden) { scheduleHandoff(15000); return; }
    var after = Number(read(AFTER_KEY, 0, "local")) || 0;
    fetch("/api/assistant/handoff?session=" + sessionId() + "&after=" + after, { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) {
        if (!data) { scheduleHandoff(15000); return; }
        var messages = Array.isArray(data.messages) ? data.messages : [];
        messages.forEach(function (m) {
          if (typeof m.at === "number" && m.at > after) after = m.at;
          if (m.fromStaff && m.text) {
            var entry = { staff: true, author: m.author || "کارشناس", text: String(m.text) };
            remember(entry);
            if (rendered) render(entry);
            notify((m.author || "کارشناس") + ": " + m.text);
          }
        });
        write(AFTER_KEY, after, "local");
        if (!data.open) { endHandoff("گفت‌وگو با کارشناس تمام شد؛ اگر سؤال دیگری دارید، من در خدمتم."); return; }
        scheduleHandoff(panel.hidden ? 10000 : 5000);
      })
      .catch(function () { scheduleHandoff(15000); });
  }
  bannerEnd.addEventListener("click", function () {
    postJson("/api/assistant/handoff/close", { session: sessionId() }, false)
      .catch(function () { return null; })
      .then(function () { endHandoff("گفت‌وگو با کارشناس بسته شد؛ از این به بعد دوباره من جواب می‌دهم."); });
  });
  document.addEventListener("visibilitychange", function () {
    if (!document.hidden && read(HANDOFF_KEY, false, "local")) scheduleHandoff(500);
  });

  // ── باز و بسته کردن ─────────────────────────────────────────
  var rendered = false;
  function open() {
    panel.hidden = false;
    bubble.hidden = true;
    document.body.classList.add("fs-as-open");
    button.setAttribute("aria-expanded", "true");
    write(OPEN_KEY, true);
    write(BUBBLE_KEY, true);
    clearUnread();
    if (!rendered) {
      rendered = true;
      history.forEach(render);
      loadSuggest().then(function () {
        renderSuggest();
        if (!history.length) {
          var intro = suggestion && suggestion.intro ? suggestion.intro : "سلام! من " + botName + " هستم، دستیار هوشمند فروشگاه. چه کمکی از دستم برمی‌آید؟";
          addBot({ text: intro });
        }
      });
    }
    scrollDown();
    if (!isMobile()) input.focus();
  }
  function hide() {
    if (synth) synth.cancel();
    panel.hidden = true;
    document.body.classList.remove("fs-as-open");
    button.setAttribute("aria-expanded", "false");
    write(OPEN_KEY, false);
  }

  button.addEventListener("click", function (event) {
    if (suppressClick) { suppressClick = false; event.preventDefault(); return; } // پایان کشیدن، نه کلیک
    if (panel.hidden) open(); else hide();
  });
  close.addEventListener("click", function () { hide(); button.focus(); });
  panel.addEventListener("keydown", function (event) { if (event.key === "Escape") { hide(); button.focus(); } });
  form.addEventListener("submit", function (event) {
    event.preventDefault();
    var value = input.value;
    input.value = "";
    ask(value);
  });

  // دور ۲۴: دکمه‌های صفحه که مستقیم از دستیار می‌پرسند. مثال (صفحهٔ دسته):
  // <button data-assistant-ask="کمکم کن انتخاب کنم" data-assistant-flow="finder:headphone">  ← کالایاب همان دسته باز می‌شود.
  document.addEventListener("click", function (event) {
    var trigger = event.target.closest ? event.target.closest("[data-assistant-ask]") : null;
    if (!trigger) return;
    event.preventDefault();
    if (panel.hidden) open();
    ask(trigger.getAttribute("data-assistant-ask"), null, trigger.getAttribute("data-assistant-flow"));
  });

  // ── شروع ────────────────────────────────────────────────────
  if (read(HANDOFF_KEY, false, "local")) { banner.hidden = false; scheduleHandoff(1500); }
  // قدم‌های راهنمای ناتمام صفحهٔ قبل (مثلاً بعد از «برو به سبد») ادامه پیدا می‌کنند.
  var pending = read(STEPS_KEY, null);
  if (pending && Array.isArray(pending.steps) && pending.steps.length) {
    guideBar.hidden = false;
    guideTimer = window.setTimeout(runGuide, 700);
  }
  // فقط روی دسکتاپ پنل بازمانده دوباره باز می‌شود (روی موبایل تمام‌صفحه است و نباید جلوی صفحه را بگیرد).
  if (read(OPEN_KEY, false) && !isMobile()) open();
  else {
    // جلب توجه: موج کوتاه دور دکمه؛ و یک بار در هر نشست، حباب خوش‌آمد کوتاه (اگر در تنظیمات روشن باشد).
    window.setTimeout(function () { if (panel.hidden) button.classList.add("fs-as-ring"); }, 2500);
    if (!read(BUBBLE_KEY, false)) {
      window.setTimeout(function () {
        if (!panel.hidden || read(BUBBLE_KEY, false)) return;
        loadSuggest().then(function (data) {
          if (!data || !data.greeting || !panel.hidden) return;
          write(BUBBLE_KEY, true);
          showBubble("سلام! من " + botName + " هستم، دستیار هوشمند فروشگاه. سؤالی دارید؟");
        });
      }, 9000);
    }
  }
})();
