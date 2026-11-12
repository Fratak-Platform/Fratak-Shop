/* Swiper initialisation for the home page and product page.
 * Every slider is configured by its data-slider type and finds its own
 * navigation buttons inside itself, so a page may contain any number of them.
 *
 * دور ۲۹ (#۱۴) – ردیف‌های کالا (جدیدترین، پرفروش‌ترین، پرتخفیف‌ترین و …):
 *  • مشتری مثل قبل با انگشت/موس جابه‌جا می‌کند؛
 *  • وقتی کاری نمی‌کند هر چند ثانیه یک کالا جلو می‌رود و در آخر ردیف به اول برمی‌گردد (rewind)؛
 *  • لمس، کشیدن، کلیک روی دکمه‌ها یا بردن موس روی ردیف ← حرکت خودکار مکث می‌کند و چند ثانیه بعد از آخرین تعامل ادامه می‌دهد؛
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

  // تنظیم مشترک حرکت خودکار ردیف‌ها: rewind به‌جای loop تا کارت‌های تکراری ساخته نشود (DOM سبک‌تر، شمارش بازدید درست).
  const railMotion = () => (reduceMotion ? {} : {
    rewind: true,
    autoplay: { delay: RAIL_DELAY, disableOnInteraction: false, pauseOnMouseEnter: true },
  });

  const configs = {
    hero: (el) => ({
      slidesPerView: 1,
      spaceBetween: 5,
      loop: el.querySelectorAll(".swiper-slide").length > 1,
      autoplay: reduceMotion ? false : { delay: 5000, disableOnInteraction: false, pauseOnMouseEnter: true },
      pagination: { el: el.querySelector(".swiper-pagination"), clickable: true },
      navigation: nav(el),
    }),
    deals: (el) => Object.assign({
      navigation: nav(el),
      breakpoints: {
        0: { slidesPerView: 1.7, spaceBetween: 10 },
        400: { slidesPerView: 1.8, spaceBetween: 6 },
        600: { slidesPerView: 2.9, spaceBetween: 6 },
        900: { slidesPerView: 3.7, spaceBetween: 10 },
        1060: { slidesPerView: 4.9, spaceBetween: 25 },
      },
    }, railMotion()),
    products: (el) => Object.assign({ navigation: nav(el), breakpoints: productBreakpoints }, railMotion()),
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

  /** مکث حرکت خودکار بعد از تعامل مشتری و ادامه چند ثانیه بعد (اگر ردیف هنوز دیده می‌شود). */
  function pauseForUser(swiper) {
    if (!swiper.autoplay) return;
    swiper.autoplay.stop();
    clearTimeout(swiper.__fsResume);
    swiper.__fsResume = setTimeout(() => {
      if (swiper.__fsVisible && !document.hidden && swiper.autoplay) swiper.autoplay.start();
    }, RESUME_AFTER);
  }

  function watchRail(el, swiper) {
    if (!swiper.params.autoplay || !swiper.params.autoplay.delay) return;
    swiper.__fsVisible = false;
    swiper.autoplay.stop();
    rails.push(swiper);
    swiper.on("touchStart", () => pauseForUser(swiper));
    swiper.on("navigationNext", () => pauseForUser(swiper));
    swiper.on("navigationPrev", () => pauseForUser(swiper));
    // فوکوس صفحه‌کلید داخل ردیف هم «تعامل» است (کاربر صفحه‌کلید نباید کارت زیر دستش جابه‌جا شود).
    el.addEventListener("focusin", () => pauseForUser(swiper));
  }

  document.querySelectorAll("[data-slider]").forEach((el) => {
    const factory = configs[el.dataset.slider];
    if (!factory) return;
    const swiper = new Swiper(el, factory(el));
    if (el.dataset.slider === "products" || el.dataset.slider === "deals") watchRail(el, swiper);
  });

  // فقط ردیف‌های داخل صفحه حرکت می‌کنند.
  if (rails.length > 0) {
    if ("IntersectionObserver" in window) {
      const observer = new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
          const swiper = rails.find((s) => s.el === entry.target);
          if (!swiper) return;
          swiper.__fsVisible = entry.isIntersecting;
          if (entry.isIntersecting && !document.hidden) swiper.autoplay.start();
          else swiper.autoplay.stop();
        });
      }, { threshold: 0.35 });
      rails.forEach((s) => observer.observe(s.el));
    } else {
      rails.forEach((s) => { s.__fsVisible = true; s.autoplay.start(); });
    }
    document.addEventListener("visibilitychange", () => {
      rails.forEach((s) => {
        if (document.hidden) s.autoplay.stop();
        else if (s.__fsVisible) s.autoplay.start();
      });
    });
  }

  // Legacy class names (product page related-products slider).
  document.querySelectorAll(".productSlider1:not([data-slider]), .productSlider2:not([data-slider])").forEach((el) => {
    new Swiper(el, { navigation: nav(el), breakpoints: productBreakpoints });
  });
})();
