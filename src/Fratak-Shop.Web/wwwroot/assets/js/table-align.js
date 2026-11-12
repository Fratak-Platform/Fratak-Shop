/*
 * دور ۲۹ (#۲۲) – هم‌ترازی سرستون‌ها با ستون‌ها در همهٔ جدول‌های سایت و پنل مدیریت.
 *
 * مشکل: در خیلی از جدول‌ها سلول‌های بدنه کلاس چینش/فاصله دارند (مثلاً «num» چپ‌چین، «actions» چپ‌چین، px-3) ولی
 * سرستون همان کلاس را ندارد؛ پس عنوان ستون بالای جای دیگری می‌نشیند (کج دیده می‌شود).
 *
 * راه‌حل عمومی (بدون دست زدن به تک‌تک صفحه‌ها): برای هر جدول، چینش افقی (text-align) و فاصلهٔ چپ/راست (padding)
 * اولین ردیف بدنه روی سرستون همان ستون کپی می‌شود (با در نظر گرفتن colspan). جدول‌هایی که در موبایل کارت می‌شوند
 * (سرستون پنهان) یا خودشان «data-align="off"» دارند دست نمی‌خورند. فقط یک بار برای هر جدول و بعد از بار شدن صفحه
 * اجرا می‌شود؛ جدولی که بعداً با fetch ساخته شود با رویداد «fs:tables» دوباره بررسی می‌شود.
 *
 * مثال: سرستون «مبلغ» (راست‌چین) بالای سلول‌های «num» (چپ‌چین) ← سرستون هم چپ‌چین می‌شود.
 */
(function () {
  'use strict';

  // ستون‌های هر ردیف با احتساب colspan: [{ cell, start, span }]
  function columns(row) {
    var list = [];
    var index = 0;
    for (var i = 0; i < row.cells.length; i++) {
      var cell = row.cells[i];
      var span = Math.max(1, cell.colSpan || 1);
      list.push({ cell: cell, start: index, span: span });
      index += span;
    }
    return list;
  }

  function sample(table) {
    var body = table.tBodies && table.tBodies[0];
    if (!body) return null;
    for (var i = 0; i < body.rows.length && i < 5; i++) {
      var row = body.rows[i];
      // ردیف «خالی است» (یک سلول با colspan کل جدول) نمونهٔ خوبی نیست.
      if (row.cells.length > 1) return row;
    }
    return null;
  }

  function align(table) {
    if (!table || table.dataset.fsAligned === '1' || table.dataset.align === 'off' || !table.tHead) return;
    var head = table.tHead.rows[table.tHead.rows.length - 1];
    var row = sample(table);
    if (!head || !row) return;
    // سرستون پنهان (حالت کارت موبایل) ← کاری لازم نیست؛ بعداً با تغییر اندازه دوباره بررسی می‌شود.
    if (head.offsetParent === null) return;
    table.dataset.fsAligned = '1';

    var cells = columns(row);
    columns(head).forEach(function (h) {
      // فقط سرستونی که دقیقاً روی یک ستون است (سرستون چندستونی عمداً وسط‌چین است).
      if (h.span !== 1) return;
      var match = null;
      for (var i = 0; i < cells.length; i++) {
        if (cells[i].start === h.start && cells[i].span === 1) { match = cells[i].cell; break; }
      }
      if (!match) return;
      var th = window.getComputedStyle(h.cell);
      var td = window.getComputedStyle(match);
      if (th.textAlign !== td.textAlign && h.cell.style.textAlign === '') h.cell.style.textAlign = td.textAlign;
      if (th.paddingLeft !== td.paddingLeft && h.cell.style.paddingLeft === '') h.cell.style.paddingLeft = td.paddingLeft;
      if (th.paddingRight !== td.paddingRight && h.cell.style.paddingRight === '') h.cell.style.paddingRight = td.paddingRight;
      // سرستونی که محتوایش «flex» است (مثل جدول سفارش‌ها) با text-align جابه‌جا نمی‌شود؛ جهت flex هم هم‌سو می‌شود.
      var inner = h.cell.firstElementChild;
      if (inner && window.getComputedStyle(inner).display.indexOf('flex') >= 0) {
        if (td.textAlign === 'left') inner.style.justifyContent = 'flex-end';
        else if (td.textAlign === 'center') inner.style.justifyContent = 'center';
      }
    });
  }

  function run(root) {
    var tables = (root || document).querySelectorAll('table');
    for (var i = 0; i < tables.length; i++) {
      try { align(tables[i]); } catch (e) { /* یک جدول عجیب نباید بقیه را خراب کند */ }
    }
  }

  function start() {
    if ('requestIdleCallback' in window) window.requestIdleCallback(function () { run(); }, { timeout: 1500 });
    else window.setTimeout(run, 0);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();

  document.addEventListener('fs:tables', function (event) { run(event.target && event.target.querySelectorAll ? event.target : document); });

  // تغییر اندازهٔ صفحه (مثلاً چرخش موبایل از حالت کارت به جدول): جدول‌های هنوز بررسی‌نشده دوباره امتحان می‌شوند.
  var timer = 0;
  window.addEventListener('resize', function () {
    window.clearTimeout(timer);
    timer = window.setTimeout(function () { run(); }, 300);
  }, { passive: true });
})();
