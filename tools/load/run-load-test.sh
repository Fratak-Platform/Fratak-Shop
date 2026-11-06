#!/usr/bin/env bash
# دور نوزدهم (A29): آزمون بار روی «هاست ضعیف شبیه‌سازی‌شده».
# برنامهٔ منتشرشده (پوشهٔ out) داخل کانتینر با سقف ۵۱۲ مگابایت حافظه و نیم هستهٔ CPU اجرا می‌شود (مثل استخر IIS یک هاست
# اشتراکی ارزان)، بعد k6 تا ۱۰۰۰ کاربر هم‌زمان می‌فرستد. شکست = حتی یک پاسخ ۵xx، خطای اتصال، یا کشته شدن کانتینر (کمبود حافظه).
#   usage: tools/load/run-load-test.sh <publish-dir> <connection-string>
set -euo pipefail

OUT="$(cd "${1:-out}" && pwd)"
CONNECTION="${2:?connection string required}"
BASE="http://localhost:5080"
HERE="$(cd "$(dirname "$0")" && pwd)"

# متغیرهای محیطی اضافه برای آزمایش (مثلاً LOAD_ENV="DOTNET_TieredCompilation=0 Shop__Resources__HeapPercent=45").
extra_env=()
for kv in ${LOAD_ENV:-}; do extra_env+=(-e "$kv"); done

docker rm -f fratak-load > /dev/null 2>&1 || true
docker run -d --name fratak-load --network host "${extra_env[@]}" \
  --memory 512m --memory-swap 512m --cpus 0.5 \
  -v "$OUT:/app" -w /app \
  -e ConnectionDefault="$CONNECTION" \
  -e Shop__SeedDemoData=true \
  -e Shop__Resources__Profile=Low \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e ASPNETCORE_URLS="$BASE" \
  -e DOTNET_gcServer=0 \
  -e MALLOC_ARENA_MAX=2 \
  -e Shop__Diagnostics__ResourceLog=true \
  -e Logging__LogLevel__Default=Warning \
  -e Logging__LogLevel__Fratak_Shop=Information \
  mcr.microsoft.com/dotnet/aspnet:10.0 dotnet Fratak-Shop.Web.dll > /dev/null

echo "Waiting for the app (migrations + demo data)..."
ready=0
for i in $(seq 1 120); do
  code=$(curl -s -o /dev/null -w "%{http_code}" "$BASE/" || true)
  if [ "$code" = "200" ]; then ready=1; break; fi
  sleep 2
done
if [ "$ready" != "1" ]; then
  echo "::error::app did not become ready"; docker logs fratak-load 2>&1 | tail -80; exit 1
fi

# گرم کردن: هر صفحه دو بار (کش صفحه + «آخرین نسخهٔ سالم» روی دیسک ساخته می‌شود؛ همان چیزی که بعد از یک روز کار واقعی هست).
for path in / /product/xiaomi-redmi-watch-3-active /product/sony-playstation-5 /category/smart-watch /category/laptop \
            /brand/apple /products "/search?q=%D8%A7%D9%BE%D9%84" "/search?sort=BestSelling" /blog /blog/headphone-life-tips \
            /faq /contact-us /assets/css/output.css /assets/js/shop.js; do
  curl -s -o /dev/null "$BASE$path"; curl -s -o /dev/null "$BASE$path"
done
sleep 3

# نمونه‌برداری منابع کانتینر هر ۳ ثانیه (حافظهٔ واقعی، تعداد رشته‌ها، CPU) ← resources.log؛ اگر برنامه مرد، آخرین سطرها علت را نشان می‌دهند.
(
  while docker inspect -f '{{.State.Running}}' fratak-load 2>/dev/null | grep -q true; do
    status=$(docker exec fratak-load sh -c 'grep -E "^(RssAnon|RssFile|RssShmem|Threads):" /proc/1/status | tr -s " \t" " " | tr "\n" " "; grep -E "^(anon|file|shmem|kernel|sock|file_mapped|file_dirty) " /sys/fs/cgroup/memory.stat 2>/dev/null | awk "{printf \"%s=%dM \", \$1, \$2/1048576}"' 2>/dev/null)
    stats=$(docker stats --no-stream --format '{{.MemUsage}} cpu={{.CPUPerc}}' fratak-load 2>/dev/null)
    echo "$(date +%H:%M:%S) $stats $status"
    sleep 3
  done
) > resources.log 2>&1 &
sampler=$!
echo "Baseline after warm-up: $(docker stats --no-stream --format '{{.MemUsage}}' fratak-load)"

set +e
docker run --rm --network host -v "$HERE:/scripts" -e BASE="$BASE" -e PEAK="${PEAK:-1000}" \
  grafana/k6 run /scripts/storefront.js 2>&1 | tee k6.txt
k6_status=${PIPESTATUS[0]}
set -e
kill "$sampler" > /dev/null 2>&1 || true
echo "== Resource samples (every 3 s; last 40)"; tail -n 40 resources.log || true

running=$(docker inspect -f '{{.State.Running}}' fratak-load 2>/dev/null || echo false)
oom=$(docker inspect -f '{{.State.OOMKilled}}' fratak-load 2>/dev/null || echo unknown)
peak_mem=$(cat /sys/fs/cgroup/system.slice/docker-$(docker inspect -f '{{.Id}}' fratak-load).scope/memory.peak 2>/dev/null || echo "?")
echo "::notice::Load test: container running=$running, OOMKilled=$oom, peak memory bytes=$peak_mem, k6 exit=$k6_status"
docker logs fratak-load > app-load.log 2>&1 || true

# خلاصه به‌صورت annotation (در API گیت‌هاب هم خواندنی است، حتی وقتی گزارش کامل در دسترس نیست).
# نکته: با set -o pipefail، grep بدون نتیجه (مثلاً «هیچ ۵xx» در آزمون موفق) کل اسکریپت را شکست می‌داد؛ «|| true» لازم است.
summary=$(grep -E "server_errors\.|connection_errors\.|snapshot_responses|http_reqs\." k6.txt | tr -s ' .' ' ' | tr '\n' ';' || true)
errors5xx=$(grep -o "5xx [0-9]* [^ ]*" k6.txt | sort | uniq -c | sort -rn | head -n 8 | tr -s ' ' | tr '\n' ';' || true)
exceptions=$(grep -oE "System\.[A-Za-z.]*Exception" app-load.log | sort | uniq -c | sort -rn | head -n 5 | tr -s ' ' | tr '\n' ';' || true)
pressure=$(grep -E "Resources: level" app-load.log | tail -n 2 | tr -s ' ' | tr '\n' ';' || true)
echo "::notice title=k6 summary::$summary 5xx: ${errors5xx:-none} exceptions: ${exceptions:-none}"
echo "::notice title=pressure::${pressure:-n/a}"
# اولین OutOfMemoryException با چند خط پشته (برای پیدا کردن مسیر کد پرمصرف).
oom_stack=$(grep -n -m1 -B3 -A14 "OutOfMemoryException" app-load.log | cut -c1-220 | tr '\n' '|' | head -c 3000 || true)
[ -n "$oom_stack" ] && echo "::notice title=oom stack::$oom_stack"
# بیشینهٔ حافظهٔ غیرقابل آزادسازی (anon + shmem) در نمونه‌ها، و حافظه بعد از گرم شدن (پیش از بار).
peak_unreclaimable=$(awk '{a=0;s=0; for(i=1;i<=NF;i++){ if($i ~ /^anon=/){sub("anon=","",$i);sub("M","",$i);a=$i} if($i ~ /^shmem=/){sub("shmem=","",$i);sub("M","",$i);s=$i} } if(a+s>m)m=a+s} END{print m+0}' resources.log 2>/dev/null)
first_sample=$(head -n 1 resources.log 2>/dev/null | grep -oE "anon=[0-9]+M|shmem=[0-9]+M" | tr '\n' ' ' || true)
echo "::notice title=memory::env='${LOAD_ENV:-}' peak anon+shmem=${peak_unreclaimable}MB; idle after warm-up: ${first_sample:-n/a}"

if [ "$running" != "true" ]; then echo "::error::the app died during the load test (OOMKilled=$oom)"; exit 1; fi
if [ "$k6_status" != "0" ]; then echo "::error::load test thresholds failed (5xx or connection errors)"; exit 1; fi
echo "Load test passed: zero 5xx with 512 MB / 0.5 CPU."
