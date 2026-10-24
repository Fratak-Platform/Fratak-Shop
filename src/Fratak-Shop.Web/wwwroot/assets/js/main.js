/*
 * Template UI behaviour (menus, drawers, modals, galleries).
 * No inline event handlers are used anywhere (the Content-Security-Policy forbids
 * them): elements declare data-ui="name" [data-arg="..."] and are handled here by
 * one delegated click listener.
 */
(function () {
  "use strict";

  const byId = (id) => document.getElementById(id);
  const root = document.documentElement;

  // ── scroll lock (drawers / modals) ───────────────────────────────────
  let lockCount = 0;
  function lockScroll() {
    if (lockCount++ === 0) {
      const scrollbar = window.innerWidth - root.clientWidth;
      root.style.overflow = "hidden";
      if (scrollbar > 0) root.style.paddingLeft = scrollbar + "px";
    }
  }
  function unlockScroll() {
    if (lockCount === 0) return;
    if (--lockCount === 0) {
      root.style.overflow = "";
      root.style.paddingLeft = "";
    }
  }

  function show(id) {
    const el = byId(id);
    if (!el || !el.classList.contains("hidden")) return;
    el.classList.remove("hidden");
    lockScroll();
  }
  function hide(id) {
    const el = byId(id);
    if (!el || el.classList.contains("hidden")) return;
    el.classList.add("hidden");
    unlockScroll();
  }

  // ── mobile navigation ───────────────────────────────────────────────
  function openNav() {
    const nav = byId("navbarMobile");
    if (!nav) return;
    nav.classList.remove("-right-full");
    nav.classList.add("right-0");
    lockScroll();
  }
  function closeNav() {
    const nav = byId("navbarMobile");
    if (!nav || nav.classList.contains("-right-full")) return;
    nav.classList.add("-right-full");
    nav.classList.remove("right-0");
    unlockScroll();
  }

  // ── product gallery slides ──────────────────────────────────────────
  let slideIndex = 1;
  function showSlides(n) {
    const slides = document.getElementsByClassName("mySlides");
    const dots = document.getElementsByClassName("demo");
    if (!slides.length) return;
    if (n > slides.length) slideIndex = 1;
    if (n < 1) slideIndex = slides.length;
    for (let i = 0; i < slides.length; i++) slides[i].style.display = "none";
    for (let i = 0; i < dots.length; i++) dots[i].classList.remove("active");
    slides[slideIndex - 1].style.display = "block";
    if (dots[slideIndex - 1]) dots[slideIndex - 1].classList.add("active");
  }
  showSlides(slideIndex);

  const actions = {
    "nav-open": openNav,
    "nav-close": closeNav,
    "cart-open": () => show("cardHeader"),
    "cart-close": () => hide("cardHeader"),
    "modal-open": (arg) => show(arg),
    "modal-close": (arg) => hide(arg),
    "slide-next": () => showSlides((slideIndex += 1)),
    "slide-prev": () => showSlides((slideIndex -= 1)),
    "slide-go": (arg) => {
      showSlides((slideIndex = Number(arg) || 1));
      show("showImagesModal");
    },
    "back-to-top": () => window.scrollTo({ top: 0, behavior: "smooth" }),
    print: () => window.print(),
  };

  document.addEventListener("click", (event) => {
    const trigger = event.target.closest("[data-ui]");
    if (!trigger) return;
    const action = actions[trigger.getAttribute("data-ui")];
    if (!action) return;
    if (trigger.tagName === "A") event.preventDefault();
    action(trigger.getAttribute("data-arg"));
  });

  // Escape closes whatever is open.
  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    closeNav();
    hide("cardHeader");
    document.querySelectorAll("[data-modal]:not(.hidden)").forEach((modal) => hide(modal.id));
  });

  // Modals that must be open on load (e.g. the address form after a validation error).
  document.querySelectorAll("[data-modal][data-open='true']").forEach((modal) => {
    modal.classList.add("hidden");
    show(modal.id);
  });

  // ── quantity inputs ([data-action=increment|decrement]) ─────────────
  document.querySelectorAll('button[data-action="decrement"], button[data-action="increment"]').forEach((btn) => {
    btn.addEventListener("click", () => {
      const wrapper = btn.parentElement;
      const input = wrapper && wrapper.querySelector("input");
      if (!input) return;
      const step = btn.getAttribute("data-action") === "increment" ? 1 : -1;
      const max = Number(input.getAttribute("max")) || Infinity;
      const value = Math.min(max, Math.max(1, (Number(input.value) || 1) + step));
      input.value = value;
    });
  });

  // ── 4 digit verification code inputs ────────────────────────────────
  const inputs = Array.from(document.querySelectorAll(".inputVerify"));
  inputs.forEach((input, index) => {
    input.addEventListener("input", () => {
      input.value = input.value.replace(/\D/g, "").slice(-1);
      const next = inputs[index + 1];
      if (input.value && next) {
        next.removeAttribute("disabled");
        next.focus();
      }
    });
    input.addEventListener("keydown", (event) => {
      if (event.key !== "Backspace" || input.value) return;
      const prev = inputs[index - 1];
      if (!prev) return;
      for (let i = index; i < inputs.length; i++) {
        inputs[i].value = "";
        inputs[i].setAttribute("disabled", "disabled");
      }
      prev.focus();
    });
    input.addEventListener("paste", (event) => {
      const text = (event.clipboardData || window.clipboardData).getData("text").replace(/\D/g, "");
      if (!text) return;
      event.preventDefault();
      inputs.forEach((target, i) => {
        if (!text[i]) return;
        target.removeAttribute("disabled");
        target.value = text[i];
      });
      (inputs[Math.min(text.length, inputs.length) - 1] || input).focus();
    });
  });
  if (inputs.length) inputs[0].focus();
})();
