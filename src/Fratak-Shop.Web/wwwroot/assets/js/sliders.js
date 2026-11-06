/* Swiper initialisation for the home page and product page.
 * Every slider is configured by its data-slider type and finds its own
 * navigation buttons inside itself, so a page may contain any number of them. */
(function () {
  "use strict";
  if (typeof Swiper === "undefined") return;

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

  const configs = {
    hero: (el) => ({
      slidesPerView: 1,
      spaceBetween: 5,
      loop: el.querySelectorAll(".swiper-slide").length > 1,
      autoplay: { delay: 5000, disableOnInteraction: false, pauseOnMouseEnter: true },
      pagination: { el: el.querySelector(".swiper-pagination"), clickable: true },
      navigation: nav(el),
    }),
    deals: (el) => ({
      navigation: nav(el),
      breakpoints: {
        0: { slidesPerView: 1.7, spaceBetween: 10 },
        400: { slidesPerView: 1.8, spaceBetween: 6 },
        600: { slidesPerView: 2.9, spaceBetween: 6 },
        900: { slidesPerView: 3.7, spaceBetween: 10 },
        1060: { slidesPerView: 4.9, spaceBetween: 25 },
      },
    }),
    products: (el) => ({ navigation: nav(el), breakpoints: productBreakpoints }),
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

  document.querySelectorAll("[data-slider]").forEach((el) => {
    const factory = configs[el.dataset.slider];
    if (factory) new Swiper(el, factory(el));
  });

  // Legacy class names (product page related-products slider).
  document.querySelectorAll(".productSlider1:not([data-slider]), .productSlider2:not([data-slider])").forEach((el) => {
    new Swiper(el, { navigation: nav(el), breakpoints: productBreakpoints });
  });
})();
