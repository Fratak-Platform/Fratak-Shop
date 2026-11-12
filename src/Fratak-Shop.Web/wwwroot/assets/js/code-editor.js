/*
  دور ۲۹ (#۱۱): ویرایشگر کد سبک برای «حالت کد» صفحه‌های کمپین (بدون کتابخانهٔ بیرونی؛ چند کیلوبایت).
  - شمارهٔ خط کنار هر ویرایشگر (هم‌اسکرول با متن).
  - Tab = دو فاصله (روی چند خط انتخاب‌شده: تورفتگی همه)، Shift+Tab = برداشتن تورفتگی، Enter = حفظ تورفتگی خط قبل.
  - Ctrl/⌘+S = ذخیره. سربرگ‌های HTML/CSS/JS و اندازهٔ پیش‌نمایش (دسکتاپ/موبایل).
  - «نمونهٔ آماده» فقط ویرایشگرهای خالی را پر می‌کند.
  - هشدار ترک صفحه وقتی تغییر ذخیره‌نشده هست.
*/
(function () {
  "use strict";
  var form = document.querySelector("[data-code-form]");
  if (!form) return;
  var INDENT = "  ";
  var dirty = false;

  function gutterFor(area) {
    var wrap = document.createElement("div");
    wrap.className = "fs-ce-wrap";
    var gutter = document.createElement("div");
    gutter.className = "fs-ce-gutter";
    gutter.setAttribute("aria-hidden", "true");
    area.parentNode.insertBefore(wrap, area);
    wrap.appendChild(gutter);
    wrap.appendChild(area);
    var count = 0;
    function render() {
      var lines = area.value.split("\n").length;
      if (lines !== count) {
        count = lines;
        var out = [];
        for (var i = 1; i <= lines; i++) out.push(i);
        gutter.textContent = out.join("\n");
      }
      gutter.scrollTop = area.scrollTop;
    }
    area.addEventListener("input", render);
    area.addEventListener("scroll", function () { gutter.scrollTop = area.scrollTop; });
    render();
    return render;
  }

  // جایگزینی متن انتخاب‌شده طوری که «برگرداندن» (Ctrl+Z) مرورگر کار کند.
  function insert(area, text) {
    if (!document.execCommand || !document.execCommand("insertText", false, text)) {
      area.setRangeText(text, area.selectionStart, area.selectionEnd, "end");
    }
    area.dispatchEvent(new Event("input", { bubbles: true }));
  }

  function lineStart(value, pos) {
    return value.lastIndexOf("\n", pos - 1) + 1;
  }

  function onKey(event) {
    var area = event.target;
    var value = area.value;
    var start = area.selectionStart;
    var end = area.selectionEnd;
    if ((event.ctrlKey || event.metaKey) && (event.key === "s" || event.key === "S")) {
      event.preventDefault();
      dirty = false;
      if (form.requestSubmit) form.requestSubmit(); else form.submit();
      return;
    }
    if (event.key === "Tab") {
      event.preventDefault();
      var from = lineStart(value, start);
      var multi = value.slice(start, end).indexOf("\n") >= 0;
      if (!multi && !event.shiftKey) {
        insert(area, INDENT);
        return;
      }
      // چند خط (یا Shift+Tab): کل خط‌های انتخاب‌شده جابه‌جا می‌شوند.
      var block = value.slice(from, end);
      var changed = event.shiftKey
        ? block.split("\n").map(function (l) { return l.indexOf(INDENT) === 0 ? l.slice(INDENT.length) : l.replace(/^\s/, ""); }).join("\n")
        : block.split("\n").map(function (l) { return INDENT + l; }).join("\n");
      area.setSelectionRange(from, end);
      insert(area, changed);
      area.setSelectionRange(from, from + changed.length);
      return;
    }
    if (event.key === "Enter" && !event.shiftKey && !event.ctrlKey && !event.metaKey && !event.isComposing) {
      var current = value.slice(lineStart(value, start), start);
      var indent = (current.match(/^[ \t]*/) || [""])[0];
      // بعد از «{» یا تگ باز، یک پله بیشتر.
      if (/[{(\[]\s*$/.test(current) || /<[a-zA-Z][^>/]*>\s*$/.test(current)) indent += INDENT;
      event.preventDefault();
      insert(area, "\n" + indent);
    }
  }

  var areas = form.querySelectorAll("[data-code-editor]");
  var renders = [];
  areas.forEach(function (area) {
    renders.push(gutterFor(area));
    area.addEventListener("keydown", onKey);
    area.addEventListener("input", function () { dirty = true; });
  });

  // سربرگ‌ها
  form.querySelectorAll("[data-code-tab]").forEach(function (tab) {
    tab.addEventListener("click", function () {
      var key = tab.getAttribute("data-code-tab");
      form.querySelectorAll("[data-code-tab]").forEach(function (t) { t.classList.toggle("is-active", t === tab); });
      form.querySelectorAll("[data-code-pane]").forEach(function (pane) {
        pane.hidden = pane.getAttribute("data-code-pane") !== key;
        if (!pane.hidden) {
          var area = pane.querySelector("textarea");
          if (area) area.focus();
        }
      });
      renders.forEach(function (r) { r(); });
    });
  });

  // اندازهٔ پیش‌نمایش
  var preview = form.querySelector("[data-code-preview]");
  form.querySelectorAll("[data-code-device]").forEach(function (btn) {
    btn.addEventListener("click", function () {
      form.querySelectorAll("[data-code-device]").forEach(function (b) { b.classList.toggle("is-active", b === btn); });
      if (preview) preview.style.width = btn.getAttribute("data-code-device");
    });
  });

  // نمونهٔ آماده
  var sample = form.querySelector("[data-code-sample]");
  if (sample) {
    sample.addEventListener("click", function () {
      ["html", "css", "js"].forEach(function (key) {
        var area = form.querySelector('textarea[name="' + key + '"]');
        var tpl = document.querySelector("[data-code-sample-" + key + "]");
        if (area && tpl && !area.value.trim()) {
          area.value = tpl.content ? tpl.content.textContent : tpl.textContent;
          area.dispatchEvent(new Event("input", { bubbles: true }));
        }
      });
      sample.remove();
    });
  }

  form.addEventListener("submit", function () { dirty = false; });
  window.addEventListener("beforeunload", function (event) {
    if (!dirty) return;
    event.preventDefault();
    event.returnValue = "";
  });
})();
