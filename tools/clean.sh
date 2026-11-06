#!/usr/bin/env bash
# پاک‌سازی فایل‌های ساختنی و موقت (bin، obj، .vs، node_modules، TestResults، publish) برای کم کردن حجم پوشهٔ پروژه.
# هیچ فایل کد، تنظیمات، کلید یا دادهٔ کاربر (App_Data، wwwroot/uploads) پاک نمی‌شود؛ همه با «dotnet build» و «npm ci» دوباره ساخته می‌شوند.
# استفاده از ریشهٔ مخزن:
#   ./tools/clean.sh               پاک‌سازی کامل + گزارش حجم
#   ./tools/clean.sh --keep-node   node_modules (Tailwind) نگه داشته شود
#   ./tools/clean.sh --report      فقط گزارش حجم، بدون پاک کردن (کدام پوشه بزرگ است؟)
set -euo pipefail
cd "$(dirname "$0")/.."
keep_node=false
report_only=false
for arg in "$@"; do
  case "$arg" in
    --keep-node) keep_node=true ;;
    --report) report_only=true ;;
    *) echo "گزینهٔ ناشناخته: $arg" >&2; exit 2 ;;
  esac
done

report() {
  echo "── حجم پوشه‌ها (بزرگ‌ترها) ──"
  du -sh ./* ./.git ./.vs 2>/dev/null | sort -rh | head -n 12
  echo "── خروجی‌های ساختنی (قابل پاک شدن) ──"
  find . -type d \( -name bin -o -name obj -o -name TestResults -o -name publish -o -name node_modules -o -name .vs \) \
    -not -path "./.git/*" -prune -exec du -sh {} + 2>/dev/null | sort -rh | head -n 15 || true
}

if [[ "$report_only" == true ]]; then
  report
  exit 0
fi

before=$(du -sh . 2>/dev/null | cut -f1)
find . -type d \( -name bin -o -name obj -o -name TestResults -o -name publish \) -not -path "./.git/*" -prune -exec rm -rf {} +
rm -rf .vs
if [[ "$keep_node" == false ]]; then
  find . -type d -name node_modules -not -path "./.git/*" -prune -exec rm -rf {} +
fi
after=$(du -sh . 2>/dev/null | cut -f1)
echo "حجم پوشه: $before → $after"
report
