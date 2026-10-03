var swiper = new Swiper(".heroSlider", {
  autoplay: true,
  autoplayTimeout: 5000,
  slidesPerView: 1,
  spaceBetween: 5,
  loop: true,
  pagination: {
    el: ".swiper-pagination",
    clickable: true,
  },
  navigation: {
    nextEl: ".swiper-button-next",
    prevEl: ".swiper-button-prev",
  },
});

var swiper = new Swiper(".offSlider", {
  centeredSlides: false,
  navigation: {
    nextEl: ".swiper-button-next",
    prevEl: ".swiper-button-prev",
  },
  breakpoints: {
    0: {
      slidesPerView: 1.7,
      spaceBetween: 10,
    },
    400: {
      slidesPerView: 1.8,
      spaceBetween: 6,
    },
    600: {
      slidesPerView: 2.9,
      spaceBetween: 6,
    },
    900: {
      slidesPerView: 3.7,
      spaceBetween: 10,
    },
    1060: {
      slidesPerView: 4.9,
      spaceBetween: 25,
    },
  },
});

var swiper = new Swiper(".productSlider1", {
  centeredSlides: false,
  navigation: {
    nextEl: ".next-btn-2",
    prevEl: ".prev-btn-2",
  },
  breakpoints: {
    0: {
      slidesPerView: 1.5,
      spaceBetween: 10,
    },
    600: {
      slidesPerView: 2.5,
      spaceBetween: 6,
    },
    900: {
      slidesPerView: 3.7,
      spaceBetween: 10,
    },
    1060: {
      slidesPerView: 4.8,
      spaceBetween: 20,
    },
  },
});

var swiper = new Swiper(".productSlider2", {
  centeredSlides: false,
  navigation: {
    nextEl: ".next-btn-3",
    prevEl: ".prev-btn-3",
  },
  breakpoints: {
    0: {
      slidesPerView: 1.5,
      spaceBetween: 10,
    },
    600: {
      slidesPerView: 2.5,
      spaceBetween: 6,
    },
    900: {
      slidesPerView: 3.7,
      spaceBetween: 10,
    },
    1060: {
      slidesPerView: 4.8,
      spaceBetween: 20,
    },
  },
});

var swiper = new Swiper(".partnerCompany", {
  rtl: false,
  breakpoints: {
    0: {
      slidesPerView: 3.1,
      spaceBetween: 4,
    },
    400: {
      slidesPerView: 4.5,
      spaceBetween: 6,
    },
    600: {
      slidesPerView: 5.7,
      spaceBetween: 8,
    },
    900: {
      slidesPerView: 6.6,
      spaceBetween: 16,
    },
    1060: {
      slidesPerView: 8.2,
      spaceBetween: 16,
    },
    1200: {
      slidesPerView: 9.7,
      spaceBetween: 10,
    },
  },
});