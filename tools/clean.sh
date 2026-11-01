#!/usr/bin/env bash
# پاک‌سازی فایل‌های ساختنی و موقت (bin، obj، .vs، node_modules، TestResults، publish) برای کم کردن حجم پوشهٔ پروژه.
# هیچ فایل کد یا تنظیماتی پاک نمی‌شود؛ همه با «dotnet build» و «npm ci» دوباره ساخته می‌شوند.
# استفاده: از ریشهٔ مخزن  ./tools/clean.sh   (با --keep-node برای نگه داشتن node_modules)
set -euo pipefail
cd "$(dirname "$0")/.."
keep_node=false
[[ "${1:-}" == "--keep-node" ]] && keep_node=true

before=$(du -sh . 2>/dev/null | cut -f1)
find . -type d \( -name bin -o -name obj -o -name TestResults -o -name publish \) -not -path "./.git/*" -prune -exec rm -rf {} +
rm -rf .vs
if [[ "$keep_node" == false ]]; then
  find . -type d -name node_modules -not -path "./.git/*" -prune -exec rm -rf {} +
fi
after=$(du -sh . 2>/dev/null | cut -f1)
echo "حجم پوشه: $before → $after"
