#!/usr/bin/env bash
# ساخت خروجی انتشار کم‌حجم (مرحلهٔ هفدهم). معادل tools/deploy/publish.ps1 برای لینوکس/مک.
#   tools/deploy/publish.sh            لینوکس ← publish/linux و publish/fratak-shop-linux.tar.gz
#   tools/deploy/publish.sh windows    ویندوز (IIS / Plesk)
#   tools/deploy/publish.sh all        همهٔ سیستم‌عامل‌ها (حجیم)
set -euo pipefail
target="${1:-linux}"
case "$target" in windows|linux|all) ;; *) echo "target must be windows, linux or all" >&2; exit 2;; esac
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"
out="publish/$target"
rm -rf "$out"
dotnet publish src/Fratak-Shop.Web/Fratak-Shop.Web.csproj -c Release -o "$out" "-p:FratakPublishTarget=$target"
echo "output: $out ($(du -sh "$out" | cut -f1))"
tar -czf "publish/fratak-shop-$target.tar.gz" -C "$out" .
echo "archive: publish/fratak-shop-$target.tar.gz ($(du -sh "publish/fratak-shop-$target.tar.gz" | cut -f1))"
