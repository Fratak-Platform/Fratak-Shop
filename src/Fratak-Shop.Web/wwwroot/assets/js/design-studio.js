/*
  استودیوی طراحی (دور ۲۱) – ویرایشگر گرافیکی بنر، کاور مقاله و پست با Fabric.js 7.
  صفحه: /admin/design/editor/{id}  (Areas/Admin/Pages/Design/Editor.cshtml)

  چه می‌کند؟
  - بوم به اندازهٔ واقعی طرح (مثلاً ۱۹۲۰×۶۴۰) ساخته می‌شود و فقط با CSS کوچک نمایش داده می‌شود؛ خروجی PNG همیشه اندازهٔ دقیق جایگاه است.
  - کشیدن، اندازه، چرخش، ویرایش متن (دوبار کلیک)، رنگ، قلم، حاشیه، سایه، لایه (جلو/عقب)، تکثیر، قفل، حذف.
  - برگرداندن/انجام دوباره (۳۰ قدم)، کلیدهای میانبر، چسبیدن به وسط بوم هنگام کشیدن.
  - شکل‌ها و «نمونه‌های آماده» (نشان تخفیف، دکمهٔ خرید، برچسب قیمت، نوار جدید، برچسب بالای تیتر).
  - افزودن عکس: عکس کالاهای فروشگاه، آپلود، جست‌وجوی اینترنت (عکس انتخاب‌شده اول روی سرور ذخیره می‌شود؛ بوم هرگز عکس بیرونی ندارد
    تا خروجی PNG مسدود نشود).
  - اسلایدر اصلی: دو نسخهٔ دسکتاپ و موبایل در زبانه‌های جدا؛ هنگام ذخیره PNG هر دو ساخته می‌شود.
  - ذخیره: JSON بوم + PNG ← سرور (WebP). انتشار بنر / گذاشتن کاور روی مقاله بعد از ذخیره.

  نقش اشیا (fsRole): bg، deco، image، kicker، headline، subline، price، old-price، badge-bg، badge، cta-bg، cta – همان نام‌هایی که قالب‌ساز سرور
  (Core/Services/Design/DesignTemplates.cs) می‌گذارد؛ «نسخهٔ موبایل از روی قالب» متن‌ها را با همین نقش‌ها منتقل می‌کند.
*/
(function () {
  "use strict";
  const root = document.querySelector("[data-design-studio]");
  if (!root || !window.fabric) return;

  const F = window.fabric;
  const ImageClass = F.FabricImage || F.Image;
  const $ = (s, el) => (el || root).querySelector(s);
  const $$ = (s, el) => Array.from((el || root).querySelectorAll(s));
  const csrf = () => (document.querySelector('meta[name="csrf-token"]') || {}).content || "";
  const base = root.dataset.base;
  const FONT = "Vazirmatn";
  const EXTRA = ["fsRole", "selectable", "evented", "lockMovementX", "lockMovementY", "lockScalingX", "lockScalingY", "lockRotation", "hasControls"];
  const HISTORY_MAX = 30;
  // Fabric 7 مبدأ پیش‌فرض را «وسط» کرده؛ همهٔ محاسبه‌های این فایل و قالب‌های سرور با گوشهٔ بالا-چپ است.
  const O = { originX: "left", originY: "top" };
  const mk = (Klass, a, b) => (b === undefined ? new Klass(Object.assign({}, O, a)) : new Klass(a, Object.assign({}, O, b)));

  const toast = (msg, type) => (window.FratakModal && window.FratakModal.toast ? window.FratakModal.toast(msg, type || "success") : console.log(msg));
  const confirmBox = (message, title) =>
    window.FratakModal && window.FratakModal.confirm ? window.FratakModal.confirm({ title: title || "تأیید", message, okText: "بله", cancelText: "انصراف" }) : Promise.resolve(true);

  // ───────── وضعیت ─────────
  const state = {
    variant: "desktop",
    variants: {}, // desktop/mobile → { width, height, json }
    history: [],
    future: [],
    loading: false,
    dirty: false,
    webRound: 0,
  };

  const canvasEl = $("[data-ds-canvas]");
  const canvas = new F.Canvas(canvasEl, { preserveObjectStacking: true, enableRetinaScaling: false, stopContextMenu: true, fireRightClick: false });

  // ───────── ابزارهای کمکی ─────────
  const colorCtx = document.createElement("canvas").getContext("2d");
  function toHex(color, fallback) {
    if (typeof color !== "string" || !color) return fallback || "#000000";
    colorCtx.fillStyle = "#000000";
    colorCtx.fillStyle = color;
    const v = colorCtx.fillStyle;
    if (v.startsWith("#")) return v;
    const m = v.match(/\d+(\.\d+)?/g);
    if (!m || m.length < 3) return fallback || "#000000";
    return "#" + m.slice(0, 3).map((n) => Math.round(+n).toString(16).padStart(2, "0")).join("");
  }

  const W = () => canvas.getWidth();
  const H = () => canvas.getHeight();

  /** نمایش بوم در عرض موجود (اندازهٔ واقعی دست نمی‌خورد). */
  function fit() {
    const stage = $("[data-ds-stage]");
    const max = Math.max(200, stage.clientWidth - 32);
    const maxH = Math.max(240, window.innerHeight - 260);
    const scale = Math.min(1, max / W(), maxH / H());
    canvas.setDimensions({ width: Math.round(W() * scale) + "px", height: Math.round(H() * scale) + "px" }, { cssOnly: true });
    state.scale = scale;
  }
  window.addEventListener("resize", () => fit());

  /** JSON بوم برای ذخیره: نقش‌ها حفظ می‌شود و آدرس عکس‌های همین سایت نسبی می‌شود (جابه‌جایی دامنه چیزی را نمی‌شکند). */
  function serialize() {
    const data = canvas.toObject(EXTRA);
    const local = (o) => {
      if (o && typeof o.src === "string") {
        try {
          const u = new URL(o.src, location.href);
          if (u.origin === location.origin) o.src = u.pathname;
        } catch (e) { /* آدرس نامعتبر دست نمی‌خورد */ }
      }
      if (o && Array.isArray(o.objects)) o.objects.forEach(local);
    };
    (data.objects || []).forEach(local);
    data.fsWidth = W();
    data.fsHeight = H();
    return JSON.stringify(data);
  }

  async function load(json, width, height) {
    state.loading = true;
    try {
      canvas.clear();
      canvas.setDimensions({ width, height });
      const data = typeof json === "string" ? JSON.parse(json || "{}") : json || {};
      if (!data.objects) data.objects = [];
      await canvas.loadFromJSON(data);
      if (!canvas.backgroundColor && data.background) canvas.backgroundColor = data.background;
      canvas.getObjects().forEach(prepare);
      fit();
      canvas.requestRenderAll();
    } finally {
      state.loading = false;
    }
    syncBgInput();
  }

  /** پس‌زمینهٔ اصلی قفل است تا با کلیک اشتباه جابه‌جا نشود (رنگش از ابزار «پس‌زمینه» عوض می‌شود). */
  function prepare(o) {
    if (o.fsRole === "bg") {
      o.set({ selectable: false, evented: false });
    }
    if (o.type && o.type.toLowerCase() === "textbox") {
      o.set({ splitByGrapheme: false });
      if (!o.direction) o.set({ direction: "rtl" });
    }
  }

  // ───────── تاریخچه (برگرداندن/انجام دوباره) ─────────
  function snapshot() {
    if (state.loading) return;
    const json = serialize();
    if (state.history[state.history.length - 1] === json) return;
    state.history.push(json);
    if (state.history.length > HISTORY_MAX + 1) state.history.shift();
    state.future = [];
    state.dirty = true;
  }
  let snapTimer = 0;
  const snapshotSoon = () => {
    clearTimeout(snapTimer);
    snapTimer = setTimeout(snapshot, 350);
  };
  async function undo() {
    if (state.history.length < 2) return;
    state.future.push(state.history.pop());
    await load(state.history[state.history.length - 1], W(), H());
    state.dirty = true;
  }
  async function redo() {
    const next = state.future.pop();
    if (!next) return;
    state.history.push(next);
    await load(next, W(), H());
    state.dirty = true;
  }
  canvas.on("object:modified", snapshot);
  canvas.on("object:added", () => { if (!state.loading) snapshotSoon(); });
  canvas.on("object:removed", () => { if (!state.loading) snapshotSoon(); });
  canvas.on("text:changed", snapshotSoon);

  // ───────── چسبیدن به وسط بوم ─────────
  canvas.on("object:moving", (e) => {
    const o = e.target;
    if (!o) return;
    const tol = 8 / (state.scale || 1);
    const c = o.getCenterPoint();
    let x = c.x;
    let y = c.y;
    if (Math.abs(c.x - W() / 2) < tol) x = W() / 2;
    if (Math.abs(c.y - H() / 2) < tol) y = H() / 2;
    if (x !== c.x || y !== c.y) o.setPositionByOrigin(new F.Point(x, y), "center", "center");
  });

  // ───────── افزودن اشیا ─────────
  function text(value, opts) {
    return new F.Textbox(value, Object.assign({}, O, {
      fontFamily: FONT, fontSize: Math.round(Math.min(W(), H() * 1.6) * 0.05), fontWeight: 700, fill: "#0f172a",
      textAlign: "center", direction: "rtl", width: Math.round(W() * 0.4), lineHeight: 1.16, splitByGrapheme: false,
    }, opts || {}));
  }
  function center(o) {
    canvas.add(o);
    o.setPositionByOrigin(new F.Point(W() / 2, H() / 2), "center", "center");
    o.setCoords();
    return o;
  }
  /** چند شیء با هم (مثل دکمه = کادر + متن): جدا می‌مانند تا متن قابل ویرایش باشد، ولی اول با هم انتخاب می‌شوند. */
  function addTogether(items) {
    canvas.discardActiveObject();
    items.forEach((o) => canvas.add(o));
    const sel = new F.ActiveSelection(items, { canvas });
    sel.setPositionByOrigin(new F.Point(W() / 2, H() / 2), "center", "center");
    canvas.setActiveObject(sel);
    canvas.requestRenderAll();
  }
  const S = () => Math.min(W(), H() * 1.6);

  const factories = {
    headline: () => center(text("تیتر اصلی", { fontSize: Math.round(S() * 0.08), fontWeight: 900, width: Math.round(W() * 0.55), fsRole: "headline" })),
    text: () => center(text("متن کوتاه دربارهٔ پیشنهاد", { fontSize: Math.round(S() * 0.04), fontWeight: 400, fsRole: "subline" })),
    rect: () => center(mk(F.Rect, { width: W() * 0.25, height: H() * 0.25, fill: "#0ea5e9", strokeWidth: 0 })),
    rounded: () => center(mk(F.Rect, { width: W() * 0.25, height: H() * 0.25, rx: 24, ry: 24, fill: "#f97316", strokeWidth: 0 })),
    circle: () => center(mk(F.Circle, { radius: Math.min(W(), H()) * 0.15, fill: "#22c55e", strokeWidth: 0 })),
    triangle: () => center(mk(F.Triangle, { width: Math.min(W(), H()) * 0.3, height: Math.min(W(), H()) * 0.26, fill: "#a855f7", strokeWidth: 0 })),
    line: () => center(mk(F.Line, [0, 0, W() * 0.3, 0], { stroke: "#0f172a", strokeWidth: Math.max(2, Math.round(S() * 0.006)) })),
    badge: () => {
      const r = Math.round(S() * 0.09);
      const circle = mk(F.Circle, { radius: r, left: 0, top: 0, fill: "#dc2626", strokeWidth: 0, fsRole: "badge-bg", shadow: new F.Shadow({ color: "rgba(0,0,0,0.25)", blur: r * 0.25, offsetY: r * 0.08 }) });
      const t = text("۲۰٪\nتخفیف", { fontSize: Math.round(r * 0.42), fontWeight: 900, fill: "#ffffff", width: r * 1.5, fsRole: "badge" });
      t.set({ left: r - t.width / 2, top: r - t.height / 2 });
      addTogether([circle, t]);
    },
    cta: () => {
      const h = Math.round(S() * 0.075);
      const w = Math.round(h * 3.6);
      const bg = mk(F.Rect, { width: w, height: h, rx: h / 2, ry: h / 2, fill: "#0f172a", strokeWidth: 0, fsRole: "cta-bg" });
      const t = text("خرید کنید", { fontSize: Math.round(h * 0.4), fontWeight: 800, fill: "#ffffff", width: w, fsRole: "cta" });
      t.set({ top: (h - t.height) / 2, left: 0 });
      addTogether([bg, t]);
    },
    price: () => {
      const fs = Math.round(S() * 0.045);
      const bg = mk(F.Rect, { width: fs * 8, height: fs * 2.6, rx: 14, ry: 14, fill: "#ffffff", strokeWidth: 0, shadow: new F.Shadow({ color: "rgba(0,0,0,0.15)", blur: 18, offsetY: 6 }) });
      const old = text("۱٬۴۹۰٬۰۰۰", { fontSize: Math.round(fs * 0.6), fontWeight: 400, fill: "#94a3b8", linethrough: true, width: fs * 8, fsRole: "old-price", top: fs * 0.25, left: 0 });
      const now = text("۱٬۲۹۰٬۰۰۰ تومان", { fontSize: fs, fontWeight: 900, fill: "#dc2626", width: fs * 8, fsRole: "price", top: fs * 1.05, left: 0 });
      addTogether([bg, old, now]);
    },
    ribbon: () => {
      const h = Math.round(S() * 0.06);
      const bg = mk(F.Rect, { width: h * 5, height: h, fill: "#facc15", strokeWidth: 0 });
      const t = text("جدید", { fontSize: Math.round(h * 0.55), fontWeight: 900, fill: "#0f172a", width: h * 5, top: h * 0.15, left: 0 });
      addTogether([bg, t]);
      const sel = canvas.getActiveObject();
      if (sel) { sel.rotate(-12); sel.setCoords(); canvas.requestRenderAll(); }
    },
    kicker: () => {
      const h = Math.round(S() * 0.05);
      const bg = mk(F.Rect, { width: h * 5.5, height: h, rx: h / 2, ry: h / 2, fill: "rgba(255,255,255,0.22)", strokeWidth: 0 });
      const t = text("پیشنهاد ویژه", { fontSize: Math.round(h * 0.5), fontWeight: 700, fill: "#ffffff", width: h * 5.5, top: h * 0.2, left: 0, fsRole: "kicker" });
      addTogether([bg, t]);
    },
  };

  async function addImage(url, width, height) {
    try {
      const img = await ImageClass.fromURL(url, { crossOrigin: "anonymous" });
      const iw = width || img.width || 1;
      const ih = height || img.height || 1;
      const scale = Math.min((W() * 0.5) / iw, (H() * 0.8) / ih, 1);
      img.set({ scaleX: scale, scaleY: scale, fsRole: "image" });
      center(img);
      canvas.setActiveObject(img);
      canvas.requestRenderAll();
    } catch (e) {
      toast("این عکس باز نشد.", "error");
    }
  }

  // ───────── پنل ویژگی‌ها ─────────
  const props = $("[data-ds-prop-body]");
  const empty = $("[data-ds-empty]");
  const isText = (o) => o && o.type && o.type.toLowerCase() === "textbox";
  const isRect = (o) => o && o.type && o.type.toLowerCase() === "rect";

  function active() {
    return canvas.getActiveObject();
  }
  function each(fn) {
    const o = active();
    if (!o) return;
    if (o.type && o.type.toLowerCase() === "activeselection") o.getObjects().forEach(fn);
    else fn(o);
  }

  function showProps() {
    const o = active();
    if (!o) {
      props.hidden = true;
      empty.hidden = false;
      return;
    }
    props.hidden = false;
    empty.hidden = true;
    const single = o.type && o.type.toLowerCase() !== "activeselection" ? o : null;
    $$("[data-ds-only='text']").forEach((el) => (el.hidden = !isText(single)));
    $$("[data-ds-only='rect']").forEach((el) => (el.hidden = !isRect(single)));
    const pick = single || o.getObjects()[0];
    const set = (name, value) => {
      const el = $(`[data-ds-prop='${name}']`);
      if (el) el.value = value;
    };
    if (isText(pick)) {
      set("text", pick.text || "");
      set("fontSize", Math.round(pick.fontSize || 40));
      set("fontWeight", String(pick.fontWeight || 400));
    }
    set("fill", toHex(typeof pick.fill === "string" ? pick.fill : "", "#000000"));
    set("opacity", pick.opacity == null ? 1 : pick.opacity);
    set("rx", Math.round(pick.rx || 0));
    set("stroke", toHex(pick.stroke, "#000000"));
    set("strokeWidth", Math.round(pick.strokeWidth || 0));
    $("[data-ds-shadow]").checked = !!pick.shadow;
  }
  canvas.on("selection:created", showProps);
  canvas.on("selection:updated", showProps);
  canvas.on("selection:cleared", showProps);
  canvas.on("object:modified", showProps);

  $$("[data-ds-prop]").forEach((input) => {
    input.addEventListener("input", () => {
      const name = input.dataset.dsProp;
      let value = input.value;
      if (["fontSize", "strokeWidth", "rx", "opacity"].includes(name)) value = parseFloat(value) || 0;
      if (name === "fontWeight") value = parseInt(value, 10) || 400;
      each((o) => {
        if ((name === "text" || name === "fontSize" || name === "fontWeight") && !isText(o)) return;
        if (name === "rx") {
          if (!isRect(o)) return;
          o.set({ rx: value, ry: value });
        } else if (name === "fill" && o.type && o.type.toLowerCase() === "line") {
          o.set({ stroke: value });
        } else {
          o.set(name, value);
        }
        if (isText(o)) o.initDimensions && o.initDimensions();
        o.setCoords();
      });
      canvas.requestRenderAll();
      snapshotSoon();
    });
  });
  $("[data-ds-shadow]").addEventListener("change", (e) => {
    const on = e.target.checked;
    each((o) => o.set({ shadow: on ? new F.Shadow({ color: "rgba(0,0,0,0.25)", blur: Math.round(S() * 0.02), offsetY: Math.round(S() * 0.008) }) : null }));
    canvas.requestRenderAll();
    snapshot();
  });
  $$("[data-ds-align]").forEach((b) => b.addEventListener("click", () => {
    each((o) => isText(o) && o.set({ textAlign: b.dataset.dsAlign }));
    canvas.requestRenderAll();
    snapshot();
  }));

  async function command(name) {
    const o = active();
    if (!o) return;
    switch (name) {
      case "front":
        each((x) => canvas.bringObjectForward(x));
        break;
      case "back":
        each((x) => {
          canvas.sendObjectBackwards(x);
          // هیچ شیئی زیر پس‌زمینهٔ اصلی نمی‌رود.
          const bg = canvas.getObjects().find((b) => b.fsRole === "bg");
          if (bg && canvas.getObjects().indexOf(x) < canvas.getObjects().indexOf(bg)) canvas.moveObjectTo(x, canvas.getObjects().indexOf(bg) + 1);
        });
        break;
      case "center-h":
        o.setPositionByOrigin(new F.Point(W() / 2, o.getCenterPoint().y), "center", "center");
        o.setCoords();
        break;
      case "center-v":
        o.setPositionByOrigin(new F.Point(o.getCenterPoint().x, H() / 2), "center", "center");
        o.setCoords();
        break;
      case "duplicate": {
        const copy = await o.clone(EXTRA);
        canvas.discardActiveObject();
        copy.set({ left: (o.left || 0) + 20, top: (o.top || 0) + 20 });
        if (copy.type && copy.type.toLowerCase() === "activeselection") {
          copy.canvas = canvas;
          copy.forEachObject((x) => canvas.add(x));
          copy.setCoords();
        } else {
          canvas.add(copy);
        }
        canvas.setActiveObject(copy);
        break;
      }
      case "lock":
        each((x) => {
          const locked = !x.lockMovementX;
          x.set({ lockMovementX: locked, lockMovementY: locked, lockScalingX: locked, lockScalingY: locked, lockRotation: locked, hasControls: !locked });
        });
        toast("قفل شیء تغییر کرد.");
        break;
      case "delete":
        each((x) => canvas.remove(x));
        canvas.discardActiveObject();
        break;
    }
    canvas.requestRenderAll();
    snapshot();
  }
  $$("[data-ds-cmd]").forEach((b) => b.addEventListener("click", () => command(b.dataset.dsCmd)));
  $$("[data-ds-add]").forEach((b) => b.addEventListener("click", () => {
    const make = factories[b.dataset.dsAdd];
    if (!make) return;
    make();
    canvas.requestRenderAll();
    snapshot();
  }));

  // ───────── پس‌زمینه ─────────
  function bgObject() {
    return canvas.getObjects().find((o) => o.fsRole === "bg");
  }
  function syncBgInput() {
    const bg = bgObject();
    const value = bg ? (typeof bg.fill === "string" ? bg.fill : bg.fill && bg.fill.colorStops ? bg.fill.colorStops[0].color : "#ffffff") : canvas.backgroundColor;
    $("[data-ds-bg]").value = toHex(typeof value === "string" ? value : "#ffffff", "#ffffff");
  }
  $("[data-ds-bg]").addEventListener("input", (e) => {
    const bg = bgObject();
    if (bg) bg.set({ fill: e.target.value });
    else canvas.backgroundColor = e.target.value;
    canvas.requestRenderAll();
    snapshotSoon();
  });

  // ───────── کلیدهای میانبر ─────────
  document.addEventListener("keydown", (e) => {
    const tag = (e.target && e.target.tagName) || "";
    if (/INPUT|TEXTAREA|SELECT/.test(tag) || e.target.isContentEditable) return;
    const o = active();
    if (o && o.isEditing) return;
    const ctrl = e.ctrlKey || e.metaKey;
    if (ctrl && e.key.toLowerCase() === "z") { e.preventDefault(); e.shiftKey ? redo() : undo(); return; }
    if (ctrl && e.key.toLowerCase() === "y") { e.preventDefault(); redo(); return; }
    if (ctrl && e.key.toLowerCase() === "s") { e.preventDefault(); save(); return; }
    if (!o) return;
    if (ctrl && e.key.toLowerCase() === "d") { e.preventDefault(); command("duplicate"); return; }
    if (e.key === "Delete" || e.key === "Backspace") { e.preventDefault(); command("delete"); return; }
    const step = e.shiftKey ? 10 : 1;
    const moves = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
    if (moves[e.key]) {
      e.preventDefault();
      o.set({ left: o.left + moves[e.key][0], top: o.top + moves[e.key][1] });
      o.setCoords();
      canvas.requestRenderAll();
      snapshotSoon();
    }
  });
  $("[data-ds-undo]").addEventListener("click", undo);
  $("[data-ds-redo]").addEventListener("click", redo);

  // ───────── نسخه‌ها (دسکتاپ/موبایل) ─────────
  function keepCurrent() {
    const v = state.variants[state.variant];
    if (v) v.json = serialize();
  }
  async function switchVariant(name) {
    if (name === state.variant || !state.variants[name] || state.loading) return;
    keepCurrent();
    state.variant = name;
    const v = state.variants[name];
    await load(v.json, v.width, v.height);
    state.history = [serialize()];
    state.future = [];
    $$("[data-ds-variant]").forEach((b) => b.classList.toggle("is-active", b.dataset.dsVariant === name));
    const size = $("[data-ds-size]");
    if (size) size.textContent = `${v.width}×${v.height}`;
  }
  $$("[data-ds-variant]").forEach((b) => b.addEventListener("click", () => switchVariant(b.dataset.dsVariant)));

  /** قالب در اندازهٔ نسخهٔ فعلی؛ متن‌های فعلی با نقش یکسان روی قالب تازه می‌نشینند. */
  async function applyTemplate(keepTexts) {
    const layout = $("[data-ds-layout]").value;
    const palette = $("[data-ds-palette]").value;
    const url = `${base}?handler=Template&layout=${encodeURIComponent(layout)}&palette=${encodeURIComponent(palette)}&mobile=${state.variant === "mobile"}`;
    const res = await fetch(url, { headers: { Accept: "application/json" } });
    if (!res.ok) { toast("قالب دریافت نشد.", "error"); return; }
    const data = await res.json();
    const sourceJson = keepTexts || serialize();
    const texts = {};
    const images = [];
    (JSON.parse(sourceJson).objects || []).forEach((o) => {
      if (o.fsRole && typeof o.text === "string" && !texts[o.fsRole]) texts[o.fsRole] = o.text;
      if (o.fsRole === "image" && o.src) images.push(o);
    });
    (data.objects || []).forEach((o) => {
      if (o.fsRole && texts[o.fsRole] != null && typeof o.text === "string") o.text = texts[o.fsRole];
    });
    const v = state.variants[state.variant];
    await load(data, v.width, v.height);
    // عکس‌های طرح قبلی (اگر قالب جای عکس نداشت) کنار حفظ می‌شوند تا کار مدیر گم نشود.
    for (const img of images.slice(0, 3)) {
      if (!(data.objects || []).some((o) => o.fsRole === "image")) await addImage(img.src, img.width, img.height);
    }
    snapshot();
  }
  $("[data-ds-template]").addEventListener("click", async () => {
    if (await confirmBox("طرح فعلی با قالب انتخابی جایگزین شود؟ متن‌های تیتر و دکمه حفظ می‌شوند.", "اعمال قالب")) applyTemplate();
  });
  const copyBtn = $("[data-ds-copy-desktop]");
  if (copyBtn) copyBtn.addEventListener("click", async () => {
    if (state.variant !== "mobile") await switchVariant("mobile");
    if (!(await confirmBox("نسخهٔ موبایل با قالب همین چیدمان و متن‌های نسخهٔ دسکتاپ از نو ساخته شود؟", "نسخهٔ موبایل"))) return;
    applyTemplate(state.variants.desktop.json);
  });

  // ───────── پنجره‌ها ─────────
  function open(name) {
    const m = $(`[data-ds-modal='${name}']`);
    if (m) m.hidden = false;
    if (name === "images" && !$("[data-ds-results='shop']").children.length) searchShop();
  }
  function close(m) { m.hidden = true; }
  $$("[data-ds-open]").forEach((b) => b.addEventListener("click", () => open(b.dataset.dsOpen)));
  $$("[data-ds-modal]").forEach((m) => {
    m.addEventListener("click", (e) => { if (e.target === m || e.target.closest("[data-ds-close]")) close(m); });
  });
  document.addEventListener("keydown", (e) => { if (e.key === "Escape") $$("[data-ds-modal]").forEach(close); });

  $$("[data-ds-img-tab]").forEach((b) => b.addEventListener("click", () => {
    $$("[data-ds-img-tab]").forEach((x) => x.classList.toggle("is-active", x === b));
    $$("[data-ds-img-pane]").forEach((p) => (p.hidden = p.dataset.dsImgPane !== b.dataset.dsImgTab));
  }));

  function resultButton(url, title) {
    const b = document.createElement("button");
    b.type = "button";
    const img = document.createElement("img");
    img.src = url;
    img.alt = title || "";
    img.loading = "lazy";
    img.referrerPolicy = "no-referrer";
    const span = document.createElement("span");
    span.textContent = title || "";
    b.append(img, span);
    return b;
  }

  async function searchShop() {
    const q = $("[data-ds-q='shop']").value.trim();
    const box = $("[data-ds-results='shop']");
    box.textContent = "در حال جست‌وجو…";
    const res = await fetch(`${base}?handler=Images&q=${encodeURIComponent(q)}`, { headers: { Accept: "application/json" } });
    const list = res.ok ? await res.json() : [];
    box.textContent = list.length ? "" : "عکسی پیدا نشد.";
    list.forEach((item) => {
      const b = resultButton(item.url, item.title);
      b.addEventListener("click", async () => { await addImage(item.url); snapshot(); close($("[data-ds-modal='images']")); });
      box.append(b);
    });
  }

  async function searchWeb(more) {
    const q = $("[data-ds-q='web']").value.trim();
    if (q.length < 2) { toast("دست‌کم دو حرف بنویسید.", "error"); return; }
    state.webRound = more ? state.webRound + 1 : 0;
    const box = $("[data-ds-results='web']");
    if (!more) box.textContent = "در حال جست‌وجو…";
    const res = await fetch(`${base}?handler=Web&q=${encodeURIComponent(q)}&round=${state.webRound}`, { headers: { Accept: "application/json" } });
    const list = res.ok ? await res.json() : [];
    if (!more) box.textContent = "";
    if (!list.length && !more) box.textContent = "نتیجه‌ای پیدا نشد؛ عبارت دیگری امتحان کنید.";
    $("[data-ds-more]").hidden = !list.length || state.webRound >= 20;
    list.forEach((item) => {
      const b = resultButton(item.url, item.title);
      b.addEventListener("click", async () => {
        b.classList.add("is-busy");
        const form = new FormData();
        form.append("url", item.url);
        const r = await fetch(`${base}?handler=Import`, { method: "POST", body: form, headers: { RequestVerificationToken: csrf(), Accept: "application/json" } });
        const data = await r.json().catch(() => ({}));
        b.classList.remove("is-busy");
        if (!r.ok) { toast(data.error || "عکس دریافت نشد.", "error"); return; }
        await addImage(data.url, data.width, data.height);
        snapshot();
        close($("[data-ds-modal='images']"));
      });
      box.append(b);
    });
  }
  $("[data-ds-search='shop']").addEventListener("click", searchShop);
  $("[data-ds-search='web']").addEventListener("click", () => searchWeb(false));
  $("[data-ds-more]").addEventListener("click", () => searchWeb(true));
  $$("[data-ds-q]").forEach((input) => input.addEventListener("keydown", (e) => {
    if (e.key !== "Enter") return;
    e.preventDefault();
    input.dataset.dsQ === "web" ? searchWeb(false) : searchShop();
  }));
  $("[data-ds-upload]").addEventListener("change", async (e) => {
    const file = e.target.files && e.target.files[0];
    if (!file) return;
    const form = new FormData();
    form.append("file", file);
    const r = await fetch(`${base}?handler=Upload`, { method: "POST", body: form, headers: { RequestVerificationToken: csrf(), Accept: "application/json" } });
    const data = await r.json().catch(() => ({}));
    e.target.value = "";
    if (!r.ok) { toast(data.error || "آپلود نشد.", "error"); return; }
    await addImage(data.url, data.width, data.height);
    snapshot();
    close($("[data-ds-modal='images']"));
  });

  // ───────── خروجی، ذخیره، انتشار ─────────
  function blobOf(el) {
    return new Promise((resolve) => el.toBlob((b) => resolve(b), "image/png"));
  }
  /** PNG یک نسخه؛ نسخهٔ فعال از خود بوم و نسخهٔ دیگر روی بوم پنهان. */
  async function render(name) {
    if (name === state.variant) {
      canvas.discardActiveObject();
      canvas.renderAll();
      return blobOf(canvas.toCanvasElement(1));
    }
    const v = state.variants[name];
    const off = new F.StaticCanvas(null, { width: v.width, height: v.height, enableRetinaScaling: false });
    await off.loadFromJSON(JSON.parse(v.json));
    off.renderAll();
    const blob = await blobOf(off.toCanvasElement(1));
    off.dispose();
    return blob;
  }

  function headline() {
    const objs = canvas.getObjects();
    const h = objs.find((o) => o.fsRole === "headline" && isText(o)) || objs.find(isText);
    return h ? h.text : "";
  }

  /**
   * صبر تا تمام شدن بارگذاری بوم (برگرداندن/قالب/تعویض نسخه).
   * چرا: اگر «ذخیره» درست وسط بارگذاری زده شود، بوم لحظه‌ای خالی است و طرح خالی ذخیره می‌شد.
   */
  async function idle() {
    for (let i = 0; state.loading && i < 200; i++) await new Promise((r) => setTimeout(r, 25));
  }

  let saving = null;
  async function save(quiet) {
    if (saving) return saving;
    saving = (async () => {
      const btn = $("[data-ds-save]");
      btn.disabled = true;
      try {
        await idle();
        keepCurrent();
        const form = new FormData();
        form.append("title", $("[data-ds-title]").value.trim());
        form.append("json", state.variants.desktop.json);
        form.append("headline", state.variant === "desktop" ? headline() : "");
        form.append("image", await render("desktop"), "design.png");
        if (state.variants.mobile) {
          form.append("mobileJson", state.variants.mobile.json);
          form.append("mobileImage", await render("mobile"), "design-mobile.png");
        }
        const r = await fetch(`${base}?handler=Save`, { method: "POST", body: form, headers: { RequestVerificationToken: csrf(), Accept: "application/json" } });
        const data = await r.json().catch(() => ({}));
        if (!r.ok) { toast(data.error || "ذخیره نشد.", "error"); return false; }
        state.dirty = false;
        if (!quiet) toast(data.message || "ذخیره شد.");
        return true;
      } catch (e) {
        toast("ذخیره نشد؛ اتصال را بررسی کنید.", "error");
        return false;
      } finally {
        btn.disabled = false;
        saving = null;
      }
    })();
    return saving;
  }
  $("[data-ds-save]").addEventListener("click", () => save(false));

  $("[data-ds-download]").addEventListener("click", async () => {
    await idle();
    const blob = await render(state.variant);
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = ($("[data-ds-title]").value.trim() || "design") + (state.variant === "mobile" ? "-mobile" : "") + ".png";
    document.body.append(a);
    a.click();
    setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
  });

  async function post(handler, fields) {
    const form = new FormData();
    Object.keys(fields).forEach((k) => form.append(k, fields[k] == null ? "" : fields[k]));
    const r = await fetch(`${base}?handler=${handler}`, { method: "POST", body: form, headers: { RequestVerificationToken: csrf(), Accept: "application/json" } });
    const data = await r.json().catch(() => ({}));
    return { ok: r.ok, data };
  }

  const publishBtn = $("[data-ds-publish]");
  if (publishBtn) publishBtn.addEventListener("click", async () => {
    publishBtn.disabled = true;
    try {
      if (!(await save(true))) return;
      const { ok, data } = await post("Publish", {
        position: $("[data-ds-pub='position']").value,
        link: $("[data-ds-pub='link']").value.trim(),
        button: $("[data-ds-pub='button']").value.trim(),
      });
      if (!ok) { toast(data.error || "انتشار انجام نشد.", "error"); return; }
      toast(data.message || "بنر منتشر شد.");
      close($("[data-ds-modal='publish']"));
    } finally {
      publishBtn.disabled = false;
    }
  });

  const coverBtn = $("[data-ds-cover]");
  if (coverBtn) coverBtn.addEventListener("click", async () => {
    coverBtn.disabled = true;
    try {
      if (!(await save(true))) return;
      const { ok, data } = await post("Cover", { postId: root.dataset.post });
      if (!ok) { toast(data.error || "کاور ثبت نشد.", "error"); return; }
      toast(data.message || "کاور ثبت شد.");
      if (data.edit) setTimeout(() => { location.href = data.edit; }, 900);
    } finally {
      coverBtn.disabled = false;
    }
  });

  window.addEventListener("beforeunload", (e) => {
    if (!state.dirty) return;
    e.preventDefault();
    e.returnValue = "";
  });

  // ───────── شروع ─────────
  async function start() {
    try {
      // قلم باید پیش از اندازه‌گیری متن‌ها بار شده باشد (وگرنه شکست خط اشتباه می‌شود).
      if (document.fonts && document.fonts.load) {
        await Promise.all(["400", "700", "900"].map((w) => document.fonts.load(`${w} 32px ${FONT}`, "سلام").catch(() => null)));
      }
      const res = await fetch(root.dataset.docUrl, { headers: { Accept: "application/json" } });
      if (!res.ok) throw new Error("doc");
      const doc = await res.json();
      state.variants.desktop = { width: doc.width, height: doc.height, json: doc.json };
      if (doc.mobile) state.variants.mobile = { width: doc.mobile.width, height: doc.mobile.height, json: doc.mobile.json };
      await load(doc.json, doc.width, doc.height);
      state.history = [serialize()];
      state.dirty = false;
      showProps();
    } catch (e) {
      toast("طرح بار نشد؛ صفحه را دوباره باز کنید.", "error");
    }
  }
  start();
})();
