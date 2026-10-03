/* =============================================================================
 * Fratak-Shop admin script
 * Sidebar (responsive), image previews, SEO live counters, «سئوی هوشمند» and
 * «هوشمندسازی نوشته‌ها» buttons, confirm dialogs and tiny chart helpers.
 * ========================================================================== */
(function () {
    "use strict";

    const token = document.querySelector('meta[name="csrf-token"]')?.content ?? "";

    // ----------------------------------------------------------------- sidebar
    window.toggleAdminSidebar = function () {
        document.getElementById("adminSidebar")?.classList.toggle("hidden");
        document.getElementById("adminSidebarOverlay")?.classList.toggle("hidden");
    };

    // ---------------------------------------------------------- confirm submit
    document.addEventListener("submit", event => {
        const form = event.target;
        const message = form.dataset.confirm;
        if (message && !window.confirm(message)) {
            event.preventDefault();
        }
    });

    // ------------------------------------------------------------ image preview
    document.addEventListener("change", event => {
        const input = event.target;
        if (!input.matches('input[type="file"][data-preview]')) {
            return;
        }

        const target = document.querySelector(input.dataset.preview);
        if (!target || !input.files?.length) {
            return;
        }

        target.src = URL.createObjectURL(input.files[0]);
        target.classList.remove("hidden");
    });

    // ------------------------------------------------------------ SEO counters
    const counters = [
        { input: "[data-seo-title]", counter: "[data-seo-title-count]", ideal: 60 },
        { input: "[data-seo-description]", counter: "[data-seo-description-count]", ideal: 160 }
    ];

    function refreshCounters() {
        counters.forEach(item => {
            const input = document.querySelector(item.input);
            const counter = document.querySelector(item.counter);
            if (!input || !counter) {
                return;
            }

            const length = input.value.trim().length;
            counter.textContent = `${length} / ${item.ideal}`;
            counter.className = length === 0
                ? "text-zinc-400 text-[11px]"
                : length <= item.ideal
                    ? "text-emerald-600 text-[11px]"
                    : "text-rose-500 text-[11px]";
        });
    }

    document.addEventListener("input", event => {
        if (event.target.matches("[data-seo-title], [data-seo-description]")) {
            refreshCounters();
        }
    });

    document.addEventListener("DOMContentLoaded", refreshCounters);

    // ------------------------------------------------------- smart SEO engine
    async function postForm(url, data) {
        const response = await fetch(url, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": token
            },
            body: JSON.stringify(data)
        });

        return response.json();
    }

    document.addEventListener("click", async event => {
        // «سئوی هوشمند» — runs the engine and fills the form for review (no save).
        const smartSeo = event.target.closest("[data-smart-seo]");
        if (smartSeo) {
            event.preventDefault();
            const entityType = smartSeo.dataset.entityType;
            const entityId = smartSeo.dataset.entityId;
            smartSeo.setAttribute("disabled", "disabled");
            smartSeo.innerHTML = "<span>در حال تحلیل...</span>";

            const endpoint = smartSeo.dataset.endpoint ?? `${window.location.pathname}?handler=SmartSeo`;

            try {
                const payload = await postForm(endpoint, { entityType, entityId });
                if (!payload.success) {
                    window.fratakToast(payload.message, "error");
                    return;
                }

                const proposal = payload.proposal;
                const fill = (selector, value) => {
                    const input = document.querySelector(selector);
                    if (input && value) {
                        input.value = value;
                        input.classList.add("ring-2", "ring-sky-300");
                        setTimeout(() => input.classList.remove("ring-2", "ring-sky-300"), 2500);
                    }
                };

                fill("[data-seo-title]", proposal.title);
                fill("[data-seo-meta-description]", proposal.metaDescription);
                fill("[data-seo-meta-keywords]", proposal.metaKeywords);
                fill("[data-seo-focus-keyword]", proposal.focusKeyword);
                fill("[data-seo-canonical]", proposal.canonicalUrl);
                fill("[data-seo-breadcrumb]", proposal.breadcrumbTitle);
                fill("[data-seo-slug]", proposal.slug);
                fill("[data-seo-og-title]", proposal.ogTitle);
                fill("[data-seo-og-description]", proposal.ogDescription);
                fill("[data-seo-x-title]", proposal.xTitle);
                fill("[data-seo-x-description]", proposal.xDescription);
                fill("[data-seo-schema-type]", proposal.schemaType);
                fill("[data-seo-structured-data]", proposal.structuredDataJsonLd);
                fill("[data-seo-secondary-keywords]", proposal.secondaryKeywordsJson);
                fill("[data-seo-image-alt]", proposal.imageAlt);
                fill("[data-content-short-description]", proposal.enhancedShortDescription);
                fill("[data-content-description]", proposal.enhancedContent);

                const scoreHost = document.querySelector("[data-seo-score]");
                if (scoreHost) {
                    scoreHost.textContent = payload.score;
                    scoreHost.dataset.score = payload.score;
                }

                const findingsHost = document.querySelector("[data-seo-findings]");
                if (findingsHost && payload.findings) {
                    findingsHost.innerHTML = payload.findings
                        .map(finding => `<li class="flex items-start gap-x-2 border-b border-zinc-100 py-2 last:border-0">
                            <span class="badge ${finding.severityClass}">${finding.severityTitle}</span>
                            <span class="text-xs text-zinc-600"><strong class="text-zinc-800">${finding.title}:</strong> ${finding.description}</span>
                        </li>`)
                        .join("");
                }

                refreshCounters();
                window.fratakToast(payload.message, "success");
            } catch {
                window.fratakToast("تحلیل سئو با خطا مواجه شد.", "error");
            } finally {
                smartSeo.removeAttribute("disabled");
                smartSeo.innerHTML = "<span>سئوی هوشمند</span>";
            }
        }

        // «هوشمندسازی نوشته‌ها».
        const smartWrite = event.target.closest("[data-smart-write]");
        if (smartWrite) {
            event.preventDefault();
            const entityType = smartWrite.dataset.entityType;
            const entityId = smartWrite.dataset.entityId;
            smartWrite.setAttribute("disabled", "disabled");

            const endpoint = smartWrite.dataset.endpoint ?? `${window.location.pathname}?handler=Enhance`;

            try {
                const payload = await postForm(endpoint, { entityType, entityId });
                if (!payload.success) {
                    window.fratakToast(payload.message, "error");
                    return;
                }

                const shortInput = document.querySelector("[data-content-short-description]");
                const contentInput = document.querySelector("[data-content-description]");
                if (shortInput && payload.shortDescription) {
                    shortInput.value = payload.shortDescription;
                }
                if (contentInput && payload.content) {
                    contentInput.value = payload.content;
                }

                window.fratakToast(payload.message, "success");
            } catch {
                window.fratakToast("تولید متن با خطا مواجه شد.", "error");
            } finally {
                smartWrite.removeAttribute("disabled");
            }
        }
    });

    // --------------------------------------------------------------- settings tabs
    window.showSettingsTab = function (group) {
        document.querySelectorAll("[data-settings-panel]").forEach(panel => {
            panel.classList.toggle("hidden", panel.dataset.settingsPanel !== group);
        });

        document.querySelectorAll("[data-settings-tab]").forEach(tab => {
            tab.classList.toggle("chip-active", tab.dataset.settingsTab === group);
        });
    };
})();
