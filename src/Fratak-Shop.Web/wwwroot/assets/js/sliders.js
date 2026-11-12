/* Swiper initialisation for the home page and product page.
 * Every slider is configured by its data-slider type and finds its own
 * navigation buttons inside itself, so a page may contain any number of them.
 *
 * دور ۲۹ (#۱۴) و دور ۳۱ (#۳) – ردیف‌های کالا (تخفیف‌های زمان‌دار، جدیدترین، پرفروش‌ترین و …):
 *  • مشتری مثل قبل با انگشت/موس/دکمه‌ها جابه‌جا می‌کند؛ دکمه‌ها دقیقاً یک کارت جلو/عقب می‌روند، در آخر ردیف «بعدی» غیرفعال می‌شود
 *    و هرگز ردیف را ناگهان به اول برنمی‌گرداند (دور ۳۱: rewind دکمه‌ها حذف شد؛ همین باعث «پرش به اول» در ردیف تخفیف‌ها بود)؛
 *  • وقتی کاری نمی‌کند هر چند ثانیه یک کارت جلو می‌رود و فقط «حرکت خودکار» در آخر ردیف، بعد از یک مکث، آرام به اول برمی‌گردد؛
 *  • دور ۳۱: تا وقتی مشتری با ردیف کار دارد حرکت خودکار کاملاً می‌ایستد: موس روی ردیف، انگشت روی ردیف (موبایل)، کشیدن،
 *    کلیک دکمه‌ها، فوکوس صفحه‌کلید، چرخ موس افقی. چند ثانیه بعد از آخرین تعامل (و فقط اگر موس/انگشت روی ردیف نیست) ادامه می‌دهد؛
 *  • ردیفی که دیده نمی‌شود (بیرون از صفحه یا تب پنهان) حرکت نمی‌کند ← مصرف باتری/پردازنده کم؛
 *  • کاربری که «کاهش حرکت» را در سیستمش روشن کرده حرکت خودکار نمی‌بیند. */
(function () {
  "use strict";
  if (typeof Swiper === "undefined") return;

  const reduceMotion = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  // فاصلهٔ حرکت خودکار ردیف‌های کالا و مکث بعد از آخرین تعامل مشتری (میلی‌ثانیه).
  const RAIL_DELAY = 4500;
  const RESUME_AFTER = 8000;

  const nav = (el) => ({
    nextEl: el.querySelector(".swiper-button-next"),
    prevEl: el.querySelector(".swiper-button-prev"),
  });

  const productBreakpoints = {
    0: { slidesPerView: 1.5, spaceBetween: 10 },
    600: { slidesPerView: 2.5, spaceBetween: 6 },
    900: { slidesPerView: 3.7, spaceBetween: 10 },
    1060: { slidesPerView: 4.8, spaceBetween: 20 },
  };

  // دور ۳۱: حرکت خودکار ردیف‌ها با زمان‌سنج خودمان (نه autoplay خود Swiper) تا «ایست هنگام تعامل» قطعی باشد و دکمه‌ها rewind نشوند.
  // roundLengths پهنای کارت‌ها را عدد صحیح می‌کند (جابه‌جایی نیم‌پیکسلی و لرزش دکمه‌ها از بین می‌رود).
  const railBase = () => ({ speed: 450, roundLengths: true, watchOverflow: true, slidesPerGroup: 1 });

  const configs = {
    hero: (el) => ({
      slidesPerView: 1,
      spaceBetween: 5,
      loop: el.querySelectorAll(".swiper-slide").length > 1,
      speed: 600,
      pagination: { el: el.querySelector(".swiper-pagination"), clickable: true },
      navigation: nav(el),
    }),
    deals: (el) => Object.assign(railBase(), {
      navigation: nav(el),
      breakpoints: {
        0: { slidesPerView: 1.7, spaceBetween: 10 },
        400: { slidesPerView: 1.8, spaceBetween: 6 },
        600: { slidesPerView: 2.9, spaceBetween: 6 },
        900: { slidesPerView: 3.7, spaceBetween: 10 },
        1060: { slidesPerView: 4.9, spaceBetween: 25 },
      },
    }),
    products: (el) => Object.assign(railBase(), { navigation: nav(el), breakpoints: productBreakpoints }),
    brands: () => ({
      rtl: false,
      breakpoints: {
        0: { slidesPerView: 3.1, spaceBetween: 4 },
        400: { slidesPerView: 4.5, spaceBetween: 6 },
        600: { slidesPerView: 5.7, spaceBetween: 8 },
        900: { slidesPerView: 6.6, spaceBetween: 16 },
        1060: { slidesPerView: 8.2, spaceBetween: 16 },
        1200: { slidesPerView: 9.7, spaceBetween: 10 },
      },
    }),
  };

  // ردیف‌هایی که حرکت خودکار دارند؛ برای توقف/ادامه با دیده شدن در صفحه.
  const rails = [];

  /**
   * حرکت خودکار یک ردیف. وضعیت «نگه داشتن» از چند منبع جمع می‌شود و هر بار sync() تصمیم می‌گیرد:
   *  hover (موس روی ردیف)، touch (انگشت روی ردیف)، focus (فوکوس صفحه‌کلید داخل ردیف)، until (تا این زمان بعد از آخرین تعامل)،
   *  visible (ردیف در صفحه دیده می‌شود)، document.hidden (تب پنهان). فقط وقتی هیچ‌کدام مانع نیست زمان‌سنج کار می‌کند.
   */
  function railAutoplay(el, swiper, delay) {
    const state = { hover: false, touch: false, focus: false, until: 0, visible: false, timer: 0, resume: 0 };

    const blocked = () => state.hover || state.touch || state.focus || !state.visible || document.hidden || Date.now() < state.until;

    const stop = () => { clearTimeout(state.timer); state.timer = 0; };

    const step = () => {
      state.timer = 0;
      if (blocked() || swiper.destroyed) return;
      // در آخر ردیف فقط حرکت خودکار به اول برمی‌گردد (دکمه‌ها هرگز).
      if (swiper.isEnd && !swiper.params.loop) swiper.slideTo(0, 700);
      else swiper.slideNext(450);
      schedule();
    };

    function schedule() {
      stop();
      if (!blocked()) state.timer = setTimeout(step, delay);
    }

    const sync = () => {
      clearTimeout(state.resume);
      if (blocked()) {
        stop();
        // بعد از پایان مهلت «آخرین تعامل»، دوباره بررسی شود.
        const wait = state.until - Date.now();
        if (wait > 0 && !state.hover && !state.touch && !state.focus) state.resume = setTimeout(sync, wait + 50);
      } else if (!state.timer) {
        schedule();
      }
    };

    /** هر تعامل مشتری: حرکت خودکار تا RESUME_AFTER بعد از همین لحظه می‌ایستد. */
    const touched = () => { state.until = Date.now() + RESUME_AFTER; sync(); };

    // موس (فقط اشاره‌گر واقعی؛ لمس موبایل pointerType=touch است و جدا حساب می‌شود).
    el.addEventListener("pointerenter", (e) => { if (e.pointerType === "mouse" || e.pointerType === "pen") { state.hover = true; sync(); } });
    el.addEventListener("pointerleave", (e) => { if (e.pointerType === "mouse" || e.pointerType === "pen") { state.hover = false; touched(); } });
    // لمس (موبایل/تبلت): از لحظهٔ گذاشتن انگشت تا چند ثانیه بعد از برداشتن.
    el.addEventListener("touchstart", () => { state.touch = true; sync(); }, { passive: true });
    const release = () => { if (state.touch) { state.touch = false; touched(); } };
    el.addEventListener("touchend", release, { passive: true });
    el.addEventListener("touchcancel", release, { passive: true });
    el.addEventListener("pointerdown", touched);
    el.addEventListener("wheel", (e) => { if (Math.abs(e.deltaX) > Math.abs(e.deltaY)) touched(); }, { passive: true });
    // فوکوس صفحه‌کلید داخل ردیف (کارت زیر دست کاربر نباید جابه‌جا شود).
    el.addEventListener("focusin", () => { state.focus = true; sync(); });
    el.addEventListener("focusout", (e) => { if (!el.contains(e.relatedTarget)) { state.focus = false; touched(); } });
    // دکمه‌ها و کشیدن خود Swiper هم «تعامل» هستند.
    swiper.on("touchStart", touched);
    swiper.on("sliderMove", touched);
    swiper.on("navigationNext", touched);
    swiper.on("navigationPrev", touched);

    return {
      swiper,
      setVisible(visible) { state.visible = visible; sync(); },
      refresh: sync,
    };
  }

  function watchRail(el, swiper, delay) {
    if (reduceMotion) return;
    if (el.querySelectorAll(".swiper-slide").length < 2) return;
    rails.push(railAutoplay(el, swiper, delay));
  }

  document.querySelectorAll("[data-slider]").forEach((el) => {
    const factory = configs[el.dataset.slider];
    if (!factory) return;
    const swiper = new Swiper(el, factory(el));
    if (el.dataset.slider === "products" || el.dataset.slider === "deals") watchRail(el, swiper, RAIL_DELAY);
    // اسلایدر اصلی صفحه هم همان قاعده را دارد: هنگام موس/لمس/فوکوس می‌ایستد (هر ۵ ثانیه یک اسلاید).
    else if (el.dataset.slider === "hero") watchRail(el, swiper, 5000);
  });

  // فقط ردیف‌های داخل صفحه حرکت می‌کنند.
  if (rails.length > 0) {
    if ("IntersectionObserver" in window) {
      const observer = new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
          const rail = rails.find((r) => r.swiper.el === entry.target);
          if (rail) rail.setVisible(entry.isIntersecting);
        });
      }, { threshold: 0.35 });
      rails.forEach((r) => observer.observe(r.swiper.el));
    } else {
      rails.forEach((r) => r.setVisible(true));
    }
    document.addEventListener("visibilitychange", () => rails.forEach((r) => r.refresh()));
  }

  // Legacy class names (product page related-products slider).
  document.querySelectorAll(".productSlider1:not([data-slider]), .productSlider2:not([data-slider])").forEach((el) => {
    new Swiper(el, { navigation: nav(el), breakpoints: productBreakpoints });
  });
})();
