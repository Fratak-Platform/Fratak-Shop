/* =============================================================================
 * Fratak-Shop storefront script
 * Cart drawer, add-to-cart, quantity controls, coupons and toasts.
 * The server stays the single source of truth: every action is a POST handler and
 * the answer is rendered by the server, then inserted in the page.
 * ========================================================================== */
(function () {
    "use strict";

    const token = document.querySelector('meta[name="csrf-token"]')?.content ?? "";

    // ------------------------------------------------------------------ helpers
    function post(url, data) {
        return fetch(url, {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded",
                "X-Requested-With": "XMLHttpRequest",
                "RequestVerificationToken": token
            },
            body: new URLSearchParams(data)
        });
    }

    window.fratakToast = function (message, type) {
        const host = document.getElementById("toast-host");
        if (!host) {
            return;
        }

        const colors = {
            success: "bg-emerald-600",
            error: "bg-rose-600",
            warning: "bg-amber-500",
            info: "bg-sky-600"
        };

        const element = document.createElement("div");
        element.className = `${colors[type] ?? colors.info} pointer-events-auto rounded-xl px-4 py-3 text-sm text-white shadow-box-md transition-opacity duration-500`;
        element.textContent = message;
        host.appendChild(element);

        setTimeout(() => {
            element.style.opacity = "0";
            setTimeout(() => element.remove(), 500);
        }, 3500);
    };

    function setCartCount(count) {
        document.querySelectorAll("[data-cart-count]").forEach(el => {
            el.textContent = toPersianDigits(count);
        });
    }

    function toPersianDigits(value) {
        return String(value).replace(/\d/g, d => "۰۱۲۳۴۵۶۷۸۹"[d]);
    }

    // --------------------------------------------------------------- cart drawer
    window.openCart = async function () {
        const drawer = document.getElementById("cardHeader");
        const overlay = document.getElementById("cardHeaderOverlay");
        if (!drawer) {
            return;
        }

        drawer.classList.remove("hidden");
        overlay?.classList.remove("hidden");
        await refreshCartDrawer();
    };

    window.closeCart = function () {
        document.getElementById("cardHeader")?.classList.add("hidden");
        document.getElementById("cardHeaderOverlay")?.classList.add("hidden");
    };

    window.refreshCartDrawer = async function () {
        const container = document.querySelector("[data-cart-drawer]");
        if (!container) {
            return;
        }

        try {
            const response = await fetch("/cart?handler=Mini", { headers: { "X-Requested-With": "XMLHttpRequest" } });
            if (!response.ok) {
                return;
            }

            const html = await response.text();
            container.innerHTML = html;

            const total = container.querySelector("[data-mini-total]")?.dataset.miniTotal;
            if (total) {
                const totalEl = document.querySelector("[data-cart-total]");
                if (totalEl) {
                    totalEl.textContent = total;
                }
            }
        } catch {
            // A failed refresh must never break the page; the user can still open /cart.
        }
    };

    // ------------------------------------------------------------- add to cart
    async function addToCart(productId, quantity, button) {
        button?.setAttribute("disabled", "disabled");

        try {
            const response = await post("/cart?handler=Add", { productId, quantity });
            const payload = await response.json();

            window.fratakToast(payload.message, payload.success ? "success" : "error");

            if (payload.success) {
                setCartCount(payload.count);
                await refreshCartDrawer();
            }
        } catch {
            window.fratakToast("ارتباط با سرور برقرار نشد.", "error");
        } finally {
            button?.removeAttribute("disabled");
        }
    }

    document.addEventListener("click", event => {
        const trigger = event.target.closest("[data-add-to-cart]");
        if (trigger) {
            event.preventDefault();
            addToCart(trigger.dataset.addToCart, trigger.dataset.quantity ?? 1, trigger);
            return;
        }

        // Quantity buttons of the cart page.
        const action = event.target.closest("[data-action]");
        if (action && action.dataset.target) {
            const input = document.querySelector(action.dataset.target);
            if (!input) {
                return;
            }

            const value = parseInt(input.value || "1", 10);
            const step = action.dataset.action === "increment" ? 1 : -1;
            const next = Math.max(1, value + step);
            input.value = next;

            const form = input.closest("form");
            form?.requestSubmit();
        }
    });

    // --------------------------------------------------------------- wish list
    document.addEventListener("click", async event => {
        const heart = event.target.closest("[data-favorite]");
        if (!heart) {
            return;
        }

        event.preventDefault();
        const response = await post("/profile/favorites?handler=Toggle", { productId: heart.dataset.favorite });
        const payload = await response.json();

        if (!payload.authenticated) {
            window.location.href = "/login?returnUrl=" + encodeURIComponent(window.location.pathname);
            return;
        }

        heart.classList.toggle("text-rose-500", payload.isFavorite);
        window.fratakToast(payload.message, payload.isFavorite ? "success" : "info");
    });

    // ------------------------------------------------------------ mobile menu
    if (typeof window.showNavbarMobile !== "function") {
        window.showNavbarMobile = function () {
            const menu = document.getElementById("navbarMobile");
            menu?.classList.remove("-right-full");
            menu?.classList.add("right-0");
        };
    }

    if (typeof window.hideNavbarMobile !== "function") {
        window.hideNavbarMobile = function () {
            const menu = document.getElementById("navbarMobile");
            menu?.classList.add("-right-full");
            menu?.classList.remove("right-0");
        };
    }

    document.addEventListener("keydown", event => {
        if (event.key === "Escape") {
            window.closeCart();
            window.hideNavbarMobile?.();
        }
    });
})();
