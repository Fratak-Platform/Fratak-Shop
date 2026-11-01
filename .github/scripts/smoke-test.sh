#!/usr/bin/env bash
# End-to-end smoke test of the storefront against a running instance seeded with
# the demo catalog (Shop:SeedDemoData=true).
#   usage: smoke-test.sh http://localhost:5080
set -uo pipefail

BASE="${1:-http://localhost:5080}"
JAR="$(mktemp)"
FAILS=0

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*"; FAILS=$((FAILS + 1)); }

# expect <status> <path> [curl args...]
expect() {
  local want="$1" path="$2"; shift 2
  local got
  got=$(curl -s -o /tmp/smoke-body -w "%{http_code}" -b "$JAR" -c "$JAR" "$@" "$BASE$path")
  if [ "$got" = "$want" ]; then pass "$got $path"; else fail "$got (expected $want) $path"; fi
}

# location <path> [curl args...] -> prints the redirect target of a request
location() {
  local path="$1"; shift
  curl -s -o /dev/null -w "%{redirect_url}" -b "$JAR" -c "$JAR" "$@" "$BASE$path"
}

contains_i() {
  if grep -qiF -- "$1" /tmp/smoke-body; then pass "page contains (i): $1"; else fail "page does not contain (i): $1"; fi
}

lacks() {
  if grep -qF -- "$1" /tmp/smoke-body; then fail "page should not contain: $1"; else pass "page lacks: $1"; fi
}

token() { grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' /tmp/smoke-body | head -1 | sed 's/.*value="//; s/"$//'; }

contains() {
  if grep -qF -- "$1" /tmp/smoke-body; then
    pass "page contains: $1"
  else
    fail "page does not contain: $1"
    echo "      --- <head> of the page:"
    tr '\n' ' ' < /tmp/smoke-body | grep -o '<head>.*</head>' | head -c 2500 | sed 's/^/      /'
    echo
  fi
}

echo "== Public pages"
for path in / /search /products /category/smart-watch /category/laptop /brand/apple "/search?discounted=true" \
            "/search?sort=BestSelling" "/search?minPrice=1000000&maxPrice=5000000&available=true" \
            /product/xiaomi-redmi-watch-3-active /product/sony-playstation-5 \
            /blog /blog/headphone-life-tips /blog/category/buying-guide \
            /faq /contact-us /page/about-us /page/rules /page/privacy /compare /coming-soon /login /register /cart /404; do
  want=200
  [ "$path" = "/404" ] && want=404
  expect "$want" "$path"
done

expect 200 "/search" -G --data-urlencode "q=اپل"
contains "MacBook"

expect 200 /product/xiaomi-redmi-watch-3-active
contains "ساعت هوشمند شیائومی"
contains "application/ld+json"

expect 200 /
contains "Haylou"
contains "تخفیف های ویژه زمان دار"

expect 200 /product/sony-playstation-5
contains "محصول موجود نمیباشد"

expect 200 /brand/apple
contains "MacBook"

echo "== Output cache (anonymous visitors get cached pages)"
for path in /product/xiaomi-redmi-watch-3-active /blog/headphone-life-tips "/category/smart-watch"; do
  curl -s -o /dev/null "$BASE$path"
  hit=$(curl -s -D - -o /dev/null "$BASE$path" | tr -d '\r' | grep -i '^x-page-cache:' | awk '{print $2}')
  if [ "$hit" = "HIT" ]; then pass "cache HIT $path"; else
    fail "no cache HIT on second request: $path"
    echo "      --- response headers (cookie values hidden):"
    curl -s -D - -o /dev/null "$BASE$path" | tr -d '\r' | sed -E 's/^(set-cookie: [^=]*)=.*/\1=…/I; s/^/      /'
  fi
done
# Signed-in or cookie-carrying visitors must never get a cached page.
hit=$(curl -s -D - -o /dev/null -H "Cookie: fratak_at=x" "$BASE/product/xiaomi-redmi-watch-3-active" | tr -d '\r' | grep -ci '^x-page-cache: HIT')
if [ "$hit" = "0" ]; then pass "no cached page for a signed-in cookie"; else fail "cached page served to a signed-in cookie"; fi

echo "== SEO & security"
expect 200 /login
contains 'content="noindex'
expect 200 /product/xiaomi-redmi-watch-3-active
lacks 'content="noindex'
contains 'rel="canonical"'
contains '"BreadcrumbList"'
expect 200 /blog/headphone-life-tips
contains '"BlogPosting"'
contains '"BreadcrumbList"'
expect 301 /about-us
expect 301 /rules
expect 301 /Products
expect 200 /sitemap.xml
contains "/page/about-us"
contains "/product/"
expect 200 /robots.txt
contains "Sitemap:"
curl -s -D /tmp/smoke-headers -o /dev/null "$BASE/"
for header in "content-security-policy" "x-content-type-options: nosniff" "referrer-policy"; do
  if grep -qi "^$header" /tmp/smoke-headers; then pass "header $header"; else fail "missing header $header"; fi
done

echo "== Round 3: public features"
expect 200 /club
contains "باشگاه"
contains "عضویت رایگان"
expect 302 /profile/club
expect 200 /track-order
expect 302 /login/code
expect 200 "/products?page=2"
contains 'rel="prev"'
expect 200 /
contains 'rel="manifest"'
contains "/club"
expect 200 /manifest.webmanifest
contains '"short_name"'
expect 200 /sw.js
expect 200 /offline.html
contains '/assets/brand/logo.svg'
# Visual identity (docs/BRAND.md): brand logo / favicons / icons / fonts are served, the manifest uses the
# brand icons and navy theme colour, and the old template brand (YekanBakh, /assets/image/pwa) is gone.
expect 200 /favicon.ico
expect 200 /assets/brand/logo.svg
expect 200 /assets/brand/logo-on-dark.svg
expect 200 /assets/brand/favicon.svg
expect 200 /assets/brand/app-icon-512.png
expect 200 /assets/brand/app-icon-maskable-512.png
expect 200 /assets/brand/og-default.png
expect 200 /assets/font/vazirmatn-fd-arabic-wght.woff2
expect 200 /assets/font/vazirmatn-latin-wght.woff2
expect 200 /manifest.webmanifest
contains '/assets/brand/app-icon-maskable-512.png'
contains '"theme_color":"#0B1927"'
lacks '/assets/image/pwa/'
contains '.png?v='
# Installed PWA icon (round 8): the manifest is revalidated on every use, and the old template icon URLs
# (stale manifests / earlier installs) now return the brand icon instead of the template sparkle or a 404.
if curl -s -D - -o /dev/null "$BASE/manifest.webmanifest" | grep -qi '^cache-control: no-cache'; then pass "manifest is no-cache"; else fail "manifest should be Cache-Control: no-cache"; fi
for legacy in /assets/image/pwa/icon-192.png /assets/image/pwa/icon-512.png /assets/image/pwa/maskable-512.png /assets/image/pwa/apple-touch-icon.png /assets/image/logo.png /apple-touch-icon.png; do
  expect 200 "$legacy"
done
if cmp -s <(curl -s "$BASE/assets/image/pwa/icon-192.png") <(curl -s "$BASE/assets/brand/app-icon-192.png"); then pass "legacy icon = brand icon"; else fail "legacy icon URL should serve the brand icon"; fi
# Round 10: the logo is an icon (no text inside the image) + HTML brand text from the settings.
# Header = icon only by default; login shows "فراتک" + coloured "شاپ".
expect 200 /
contains '/assets/brand/mark.svg'
contains '/assets/brand/mark-on-dark.svg'
lacks 'brand-text-accent'
contains '/assets/brand/favicon.svg'
contains 'content="#0B1927"'
lacks 'YekanBakh'
expect 200 /login
contains '/assets/brand/mark.svg'
contains 'bl-p2'
contains 'شاپ</span>'
# Specification filters (f-{slug}): water resistance IPX5 -> Haylou only.
# Without a category only general filters: no brand or specification boxes.
expect 200 /search
lacks 'name="f-water"'
lacks 'type="checkbox" name="brand"'
contains "برای فیلتر بر اساس برند"
# Category listing: its own brand + specification filters (water resistance IPX5 -> Haylou only).
expect 200 /search -G --data-urlencode "category=earbuds" --data-urlencode "f-water=IPX5"
contains "Haylou"
lacks "MacBook"
contains 'name="f-water"'
contains 'type="checkbox" name="brand"'
# Brand page: the brand is fixed by the URL, no brand box.
expect 200 /brand/apple
lacks 'type="checkbox" name="brand"'
# Guests may preview a code on the cart; an unknown code must not be stored.
expect 200 /cart

echo "== Internal links (no 404 / 5xx anywhere)"
: > /tmp/smoke-links
for page in / /products /blog /club /faq /cart /track-order /product/haylou-t15 /blog/headphone-life-tips /category/laptop /page/about-us; do
  curl -s -b "$JAR" "$BASE$page" | grep -o 'href="/[^"]*"' | sed 's/^href="//; s/"$//' >> /tmp/smoke-links
done
LINKS=$(grep -v -E '^/(assets|uploads|lib)/|^/logout|handler=|^//' /tmp/smoke-links | sed 's/&amp;/\&/g; s/#.*$//' | grep -v '^$' | sort -u | head -250)
BROKEN=0
for link in $LINKS; do
  code=$(curl -s -L -o /dev/null -w "%{http_code}" "$BASE$link")
  if [ "$code" -ge 400 ]; then fail "broken link $code $link"; BROKEN=$((BROKEN + 1)); fi
done
[ "$BROKEN" -eq 0 ] && pass "all $(echo "$LINKS" | wc -l) internal links answer (no 4xx/5xx)"

echo "== Compare"
expect 200 /product/haylou-t15
TOKEN=$(token)
PRODUCT=$(grep -o 'name="productId" value="[0-9a-f-]\{36\}"' /tmp/smoke-body | head -1 | grep -o '[0-9a-f-]\{36\}')
expect 302 "/compare?handler=Add" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "productId=$PRODUCT"
expect 200 /compare
contains "Haylou"

echo "== Not found"
expect 404 /product/this-product-does-not-exist
expect 404 /this/page/does/not/exist
expect 404 /category/missing-category

echo "== Auth required"
expect 302 /profile
expect 302 /checkout
expect 302 /profile/orders

echo "== Login (demo customer)"
expect 200 /login
TOKEN=$(token)
expect 302 /login --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Input.PhoneNumber=09120000000" --data-urlencode "Input.Password=123456" --data-urlencode "Input.RememberMe=true"
if grep -q "fratak_at" "$JAR" && grep -q "fratak_rt" "$JAR"; then pass "JWT access + refresh cookies issued"; else fail "auth cookies missing"; fi
for path in /profile /profile/orders /profile/addresses /profile/favorites /profile/messages /profile/info /profile/password /profile/wallet /profile/sessions /profile/notifications /profile/club; do
  expect 200 "$path"
done
expect 200 /profile/password
contains "ورود دو مرحله"
expect 302 /club
expect 200 /profile/club
contains "rewardId"
contains "کدهای تخفیف شما"

echo "== Club: redeem points (500 -> wallet credit)"
REWARD=$(grep -o 'name="rewardId" value="[0-9a-f-]\{36\}"' /tmp/smoke-body | head -1 | grep -o '[0-9a-f-]\{36\}')
TOKEN=$(token)
if [ -n "$REWARD" ]; then
  expect 302 "/profile/club?handler=Redeem" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "rewardId=$REWARD"
  expect 200 /profile/club
  # Club credit is promotional: shown as gift credit, never withdrawable.
  expect 200 /profile/wallet
  contains "اعتبار هدیه: <span"
else
  fail "no reward to redeem on /profile/club"
fi

echo "== Location API"
curl -s -b "$JAR" "$BASE/profile/addresses" -o /tmp/smoke-body
PROVINCE=$(grep -o 'option value="[0-9a-f-]\{36\}"' /tmp/smoke-body | head -1 | grep -o '[0-9a-f-]\{36\}')
if [ -n "$PROVINCE" ]; then
  expect 200 "/api/locations/provinces/$PROVINCE/cities"
  contains '"name"'
else
  fail "no province options on the address page"
fi

echo "== Add to cart"
expect 200 /product/haylou-t15
TOKEN=$(token)
PRODUCT=$(grep -o 'name="productId" value="[0-9a-f-]\{36\}"' /tmp/smoke-body | head -1 | grep -o '[0-9a-f-]\{36\}')
expect 302 "/cart?handler=Add" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "productId=$PRODUCT" --data-urlencode "quantity=2" --data-urlencode "returnUrl=/cart"
expect 200 /cart
contains "Haylou"

echo "== Coupons"
TOKEN=$(token)
expect 302 "/cart?handler=Coupon" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "code=EXPIRED50" --data-urlencode "returnUrl=/cart"
expect 200 /cart
lacks 'dir="ltr">EXPIRED50'
TOKEN=$(token)
expect 302 "/cart?handler=Coupon" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "code=audio15" --data-urlencode "returnUrl=/cart"
expect 200 /cart
contains "AUDIO15"

echo "== Checkout (courier + time slot + cash on delivery)"
expect 200 /checkout
contains "AUDIO15"
TOKEN=$(token)
ADDRESS=$(grep -o 'name="Input.AddressId" value="[0-9a-f-]\{36\}"' /tmp/smoke-body | head -1 | grep -o '[0-9a-f-]\{36\}')
# First shipping method that has a time-slot panel (courier) and its first selectable slot.
read -r METHOD SLOT < <(python3 - <<'PY'
import re
html = open('/tmp/smoke-body', encoding='utf-8').read()
panels = re.findall(r'id="slots-([0-9a-f-]{36})"', html)
method = panels[0] if panels else ''
slot = ''
if method:
    panel = html[html.index(f'id="slots-{method}"'):]
    for m in re.finditer(r'<input type="radio"[^>]*name="Input.DeliverySlot"[^>]*>', panel, re.S):
        if 'disabled' not in m.group(0):
            slot = re.search(r'value="([^"]+)"', m.group(0)).group(1)
            break
print(method or '-', slot or '-')
PY
)
TARGET=$(location "/checkout?handler=PlaceOrder" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Input.AddressId=$ADDRESS" --data-urlencode "Input.ShippingMethodId=$METHOD" \
  --data-urlencode "Input.DeliverySlot=$SLOT" \
  --data-urlencode "Input.PaymentMethod=CashOnDelivery" --data-urlencode "Input.Note=smoke test")
case "$TARGET" in
  */checkout/success/*)
    pass "order placed -> $TARGET"
    expect 200 "${TARGET#$BASE}"
    contains "سفارش شما با موفقیت ثبت شد"
    ORDER_NO="${TARGET##*/}"
    expect 200 "/profile/orders/$ORDER_NO/invoice"
    contains "data-invoice-pdf"
    contains "AUDIO15"
    # Unpaid cash-on-delivery order: pro-forma, fixed A4 sheet, amount in words.
    contains "پیش‌فاکتور"
    contains "مبلغ قابل پرداخت به حروف"
    contains "inv-sheet"
    expect 200 /profile/notifications
    contains_i "$ORDER_NO"
    expect 200 "/profile/orders/$ORDER_NO"
    contains "لغو سفارش"
    contains "data-invoice-quick-print"
    contains "data-confirm"
    TOKEN=$(token)
    expect 302 "/profile/orders/$ORDER_NO?handler=Cancel" --data-urlencode "__RequestVerificationToken=$TOKEN"
    expect 200 "/profile/orders/$ORDER_NO"
    contains "لغو شده" ;;
  *) fail "place order redirected to '$TARGET'" ;;
esac

echo "== Checkout (wallet payment)"
expect 200 /product/haylou-t15
TOKEN=$(token)
PRODUCT=$(grep -o 'name="productId" value="[0-9a-f-]\{36\}"' /tmp/smoke-body | head -1 | grep -o '[0-9a-f-]\{36\}')
expect 302 "/cart?handler=Add" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "productId=$PRODUCT"
expect 200 /checkout
TOKEN=$(token)
POST_METHOD=$(grep -o 'name="Input.ShippingMethodId" value="[0-9a-f-]\{36\}"' /tmp/smoke-body | grep -o '[0-9a-f-]\{36\}' | grep -v "$METHOD" | head -1)
TARGET=$(location "/checkout?handler=PlaceOrder" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Input.AddressId=$ADDRESS" --data-urlencode "Input.ShippingMethodId=${POST_METHOD:-$METHOD}" \
  --data-urlencode "Input.DeliverySlot=$SLOT" --data-urlencode "Input.PaymentMethod=Wallet")
case "$TARGET" in
  */checkout/success/*) pass "wallet order -> $TARGET"; expect 200 "${TARGET#$BASE}"; contains "پرداخت با موفقیت انجام شد"
    expect 200 /profile/wallet; contains "پرداخت سفارش"
    # Paid order: final invoice with a serial number.
    WALLET_NO="${TARGET##*/}"
    expect 200 "/profile/orders/$WALLET_NO/invoice"
    contains "صورتحساب فروش کالا و خدمات"
    contains "INV-" ;;
  *) fail "wallet order redirected to '$TARGET'" ;;
esac

echo "== Checkout (online payment -> ZarinPal sandbox)"
expect 200 /product/apple-airpods-pro-2
TOKEN=$(token)
PRODUCT=$(grep -o 'name="productId" value="[0-9a-f-]\{36\}"' /tmp/smoke-body | head -1 | grep -o '[0-9a-f-]\{36\}')
expect 302 "/cart?handler=Add" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "productId=$PRODUCT"
expect 200 /checkout
TOKEN=$(token)
TARGET=$(location "/checkout?handler=PlaceOrder" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Input.AddressId=$ADDRESS" --data-urlencode "Input.ShippingMethodId=${POST_METHOD:-$METHOD}" \
  --data-urlencode "Input.DeliverySlot=$SLOT" --data-urlencode "Input.PaymentMethod=OnlineGateway")
case "$TARGET" in
  https://sandbox.zarinpal.com/pg/StartPay/*) pass "redirected to ZarinPal sandbox -> $TARGET" ;;
  */checkout/failed/*) pass "order created; ZarinPal not reachable from CI -> $TARGET (expected offline)"; expect 200 "${TARGET#$BASE}" ;;
  *) fail "online order redirected to '$TARGET'" ;;
esac
# A forged callback must never mark anything as paid.
DONE=$(location "/payment/callback?Authority=A000000000000000000000000000forged&Status=OK")
case "$DONE" in
  */checkout/success/*) fail "forged callback was accepted -> $DONE" ;;
  *) pass "forged callback rejected -> $DONE" ;;
esac

echo "== Review"
expect 200 /product/haylou-t15
TOKEN=$(token)
expect 302 "/product/haylou-t15?handler=Review" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Review.Title=عالی" --data-urlencode "Review.Content=کیفیت صدا خیلی خوب است." \
  --data-urlencode "Review.Rating=5" --data-urlencode "Review.IsRecommended=true"

echo "== Review photos (metadata stripped) + one review per product"
python3 - <<'PY'
import struct
def seg(marker, data):
    return b"\xff" + bytes([marker]) + struct.pack(">H", len(data) + 2) + data
tiff = b"MM\x00\x2a\x00\x00\x00\x08" + struct.pack(">H", 1) + struct.pack(">HHIHH", 0x0112, 3, 1, 6, 0) + b"\x00" * 4 + b"GPSSECRET"
jpeg = (b"\xff\xd8" + seg(0xE0, b"JFIF\x00\x01\x01\x00\x00\x01\x00\x01\x00\x00") + seg(0xE1, b"Exif\x00\x00" + tiff)
        + seg(0xFE, b"COMMENTSECRET") + seg(0xDB, b"\x00" + bytes(range(1, 65)))
        + seg(0xC0, b"\x08" + struct.pack(">HH", 16, 24) + b"\x01\x01\x11\x00")
        + seg(0xC4, b"\x00" + bytes([0, 1] + [0] * 14) + b"\x00") + seg(0xDA, b"\x01\x01\x00\x00\x3f\x00")
        + b"\x12\x34\x56\xff\xd9" + b"TRAILERSECRET")
open("/tmp/smoke-review.jpg", "wb").write(jpeg)
open("/tmp/smoke-fake.jpg", "wb").write(b"<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")
PY
expect 200 /product/apple-airpods-pro-2
contains 'enctype="multipart/form-data"'
contains 'name="reviewImages"'
TOKEN=$(token)
# A script disguised as .jpg is rejected by its content.
expect 302 "/product/apple-airpods-pro-2?handler=Review" -F "__RequestVerificationToken=$TOKEN" \
  -F "Review.Content=تست فایل جعلی" -F "Review.Rating=4" -F "reviewImages=@/tmp/smoke-fake.jpg;type=image/jpeg"
expect 200 /product/apple-airpods-pro-2
contains "فقط تصاویر JPG، PNG و WebP"
TOKEN=$(token)
expect 302 "/product/apple-airpods-pro-2?handler=Review" -F "__RequestVerificationToken=$TOKEN" \
  -F "Review.Title=صدای عالی" -F "Review.Content=حذف نویز فوق‌العاده است." -F "Review.Rating=5" \
  -F "reviewImages=@/tmp/smoke-review.jpg;type=image/jpeg"
expect 200 /product/apple-airpods-pro-2
contains "دیدگاه شما برای این محصول ثبت شده است"
UPLOADED=$(ls -t out/wwwroot/uploads/comments/*.jpg 2>/dev/null | head -1)
if [ -n "$UPLOADED" ]; then
  if grep -q "GPSSECRET\|COMMENTSECRET\|TRAILERSECRET" "$UPLOADED"; then fail "review photo still has metadata: $UPLOADED"; else pass "review photo stored without metadata"; fi
  if grep -q "Exif" "$UPLOADED"; then pass "orientation kept"; else fail "orientation dropped"; fi
  case "$(basename "$UPLOADED")" in [0-9a-f]*.jpg) pass "random file name $(basename "$UPLOADED")" ;; *) fail "unexpected file name $UPLOADED" ;; esac
else
  pass "upload folder not visible from the script (skipped file checks)"
fi
# Second review for the same product is refused.
expect 200 /product/haylou-t15
contains "دیدگاه شما برای این محصول ثبت شده است"
TOKEN=$(token)
expect 302 "/product/haylou-t15?handler=Review" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Review.Content=دیدگاه دوم برای همان محصول" --data-urlencode "Review.Rating=3"
expect 200 /product/haylou-t15
contains "قبلاً برای این محصول دیدگاه"

echo "== Search suggestions"
expect 200 /api/search/suggest -G --data-urlencode "q=haylou"
contains '"/product/haylou-t15"'
contains '"searchUrl"'
expect 200 /api/search/suggest -G --data-urlencode "q=ه"
contains '"products":[]'
expect 200 /
contains "data-search-suggest"

echo "== PWA install prompt + page alerts"
expect 200 /product/haylou-t15
contains "data-pwa-install"
contains "data-page-alerts"
expect 200 /cart
lacks "data-pwa-install"

echo "== Reports (development only, loopback)"
expect 200 /_dev/reports
contains '"grossSales"'
contains '"pointsEarned"'
contains '"grossProfit"'
contains '"recoveryRate"'

echo "== Favorites"
expect 200 /product/apple-airpods-pro-2
TOKEN=$(token)
expect 302 "/profile/favorites?handler=Toggle" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "productId=$PRODUCT" --data-urlencode "returnUrl=/profile/favorites"
expect 200 /profile/favorites
contains "AirPods"

echo "== Contact form"
expect 200 /contact-us
TOKEN=$(token)
expect 302 /contact-us --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Input.FullName=کاربر تست" --data-urlencode "Input.PhoneNumber=09121111111" \
  --data-urlencode "Input.Subject=تست" --data-urlencode "Input.Content=این یک پیام آزمایشی است."

echo "== Logout + register"
expect 200 /profile
TOKEN=$(token)
expect 302 /logout --data-urlencode "__RequestVerificationToken=$TOKEN"
expect 200 /register
TOKEN=$(token)
TARGET=$(location /register --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Input.PhoneNumber=09129998877" --data-urlencode "Input.Password=secret123" \
  --data-urlencode "Input.ConfirmPassword=secret123" --data-urlencode "Input.AcceptRules=true")
case "$TARGET" in
  */verify*) pass "register -> $TARGET"; expect 200 /verify; contains "تایید حساب" ;;
  *) fail "register redirected to '$TARGET'" ;;
esac

echo "== Track order (guest, POST only)"
if [ -n "${ORDER_NO:-}" ]; then
  expect 200 /track-order
  TOKEN=$(token)
  expect 200 /track-order --data-urlencode "__RequestVerificationToken=$TOKEN" \
    --data-urlencode "Input.OrderNumber=${ORDER_NO^^}" --data-urlencode "Input.PhoneNumber=09120000000"
  contains_i "$ORDER_NO"
  TOKEN=$(token)
  expect 200 /track-order --data-urlencode "__RequestVerificationToken=$TOKEN" \
    --data-urlencode "Input.OrderNumber=${ORDER_NO^^}" --data-urlencode "Input.PhoneNumber=09129999999"
  lacks "${ORDER_NO^^}</"
fi

echo "== Passwordless login (SMS code; the code is never shown on the page, read it from the server log)"
rm -f "$JAR"; JAR="$(mktemp)"
expect 200 /login
TOKEN=$(token)
TARGET=$(location "/login?handler=SendCode" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "Otp.PhoneNumber=09125554433")
case "$TARGET" in
  */login/code*)
    pass "code sent -> $TARGET"
    expect 200 "${TARGET#$BASE}"
    # Round 10: the page must never reveal the code (no "development" hint either).
    if grep -q "محیط توسعه" /tmp/smoke-body; then fail "login code page reveals a development hint"; else pass "code not shown on /login/code"; fi
    # Development without an SMS.ir key logs the text instead of sending it ("SMS to <phone>: ... <code>").
    CODE=""
    for _ in 1 2 3 4 5; do
      CODE=$(sed -n 's/.*SMS to 09125554433: [^0-9]*\([0-9]\{5\}\).*/\1/p' "${APP_LOG:-app.log}" 2>/dev/null | tail -1)
      [ -n "$CODE" ] && break
      sleep 1
    done
    TOKEN=$(token)
    if [ -n "$CODE" ]; then
      DIGITS=()
      for ((i = 0; i < ${#CODE}; i++)); do DIGITS+=(--data-urlencode "Digits=${CODE:$i:1}"); done
      DONE=$(location "${TARGET#$BASE}" --data-urlencode "__RequestVerificationToken=$TOKEN" "${DIGITS[@]}")
      case "$DONE" in
        */welcome*|*/profile/info*|*/) pass "signed in with SMS code -> $DONE" ;;
        *) fail "SMS code login redirected to '$DONE'" ;;
      esac
      if grep -q "fratak_at" "$JAR"; then pass "session after SMS login"; else fail "no session after SMS login"; fi
    else
      fail "SMS code for 09125554433 not found in ${APP_LOG:-app.log}"
    fi ;;
  *) fail "send code redirected to '$TARGET'" ;;
esac

# admin_login <phone> -> fresh cookie jar signed in with the demo password
admin_login() {
  rm -f "$JAR"; JAR="$(mktemp)"
  expect 200 /login
  local t; t=$(token)
  expect 302 /login --data-urlencode "__RequestVerificationToken=$t" \
    --data-urlencode "Input.PhoneNumber=$1" --data-urlencode "Input.Password=123456"
}

# first_link <page> <regex> -> first matching href on a page
first_link() {
  curl -s -b "$JAR" "$BASE$1" | grep -o "href=\"$2\"" | head -1 | sed 's/^href="//; s/"$//'
}

echo "== Admin panel: customers cannot see it"
admin_login 09120000000
expect 404 /admin
expect 404 /admin/orders

echo "== Admin panel: owner sees every page"
admin_login 09120000009
for path in /admin /admin/orders "/admin/orders?invoice=missing" "/admin/orders?status=Processing" /admin/orders/purchase-invoices \
            /admin/returns "/admin/returns?status=all" /admin/customers /admin/feedback "/admin/feedback?period=all" /admin/products /admin/products/edit \
            /admin/comments /admin/tickets "/admin/tickets?state=all" /admin/messages "/admin/messages?state=all" \
            /admin/finance "/admin/finance?period=all" "/admin/finance?period=month" /admin/finance/payments /admin/finance/withdrawals "/admin/finance/withdrawals?status=" /admin/finance/wallet \
            /admin/gift-cards /admin/notifications /admin/abandoned-carts "/admin/abandoned-carts?period=all" \
            /admin/blog "/admin/blog?status=review" /admin/blog/edit /admin/content /admin/seo "/admin/seo?tab=404" "/admin/seo?tab=search" \
            /admin/staff /admin/manage /admin/system /admin/system/live /admin/system/errors /admin/system/audit /admin/system/security \
            /admin/inbox "/admin/inbox?unread=true" /admin/notifications/templates /admin/notifications/templates/OrderShipped /admin/ready-messages /profile/notifications \
            /admin/ux /admin/media /admin/help "/admin/help?new=true&path=/admin/orders"; do
  expect 200 "$path"
done
echo "== Admin UX (P2): help drawer, quick search, theme, media list"
expect 200 /admin/orders
contains_i "data-help-drawer"
contains_i "data-palette-open"
expect 200 "/admin/ux?handler=Search&q=0912"
contains_i "["
expect 200 "/admin/media?handler=List"
contains_i "pageCount"
echo "== Admin tables (P3): trash, sorting, page size, drafts"
expect 200 /admin/trash
expect 200 "/admin/trash?type=products"
expect 200 "/admin/trash?type=does-not-exist"
expect 200 "/admin/manage/faq-items?sort=CreatedAt&dir=desc&take=50"
contains_i "data-inline-url"
expect 200 "/admin/manage/faq-items?sort=NotAColumn&take=7"
expect 200 "/admin/ux?handler=Draft&key=blog:new"
contains_i "ok"
echo "== Orders (P4): SLA filters, kanban, staff order, payment link, CRM"
expect 200 "/admin/orders?sla=overdue"
expect 200 "/admin/orders?sla=soon&source=staff"
expect 200 /admin/orders/board
contains_i "data-board-column"
expect 200 /admin/orders/new
contains_i "data-staff-lookup"
expect 200 "/admin/orders/new?phone=09120000000"
contains_i "data-staff-order"
expect 200 "/admin/orders/new?handler=Products&q=%D8%A7"
contains_i "["
expect 404 /pay/not-a-valid-token
expect 404 /pay/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
expect 200 "/admin/customers?tag=%D8%B9%D9%85%D8%AF%D9%87"
echo "== Supplier sync (P5)"
expect 200 /admin/products/supplier-sync
contains_i "data-supplier-sync"
for tab in problems auto history runs; do
  expect 200 "/admin/products/supplier-sync?tab=$tab"
done
expect 200 "/admin/products/supplier-sync?kind=PriceChange&q=%D8%A7"
expect 200 "/admin/products/supplier-sync?tab=not-a-tab"
echo "== Catalog tools (P5b): bulk price, inventory, import"
expect 200 /admin/products/bulk-price
contains_i "data-bulk-price"
TOKEN=$(token)
expect 200 "/admin/products/bulk-price?handler=Preview" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Form.ScopeKind=filter" --data-urlencode "Form.State=active" --data-urlencode "Form.Target=0" \
  --data-urlencode "Form.Mode=0" --data-urlencode "Form.Percent=5" --data-urlencode "Form.RoundStep=1000" --data-urlencode "Form.RoundDirection=0"
contains_i "data-bp-preview"
expect 200 /admin/products/inventory
expect 200 "/admin/products/inventory?days=90&level=Critical"
expect 200 "/admin/products/inventory?tab=ledger"
expect 200 "/admin/products/inventory?tab=ledger&reason=Sale&from=1404/01/01&to=1404/12/29"
expect 200 /admin/products/import
expect 200 "/admin/products/import?handler=Template"
expect 200 "/admin/products/import?handler=Template&format=csv"
contains_i "کد کالا"
expect 200 /admin/products
contains_i "کامل بودن"
echo "== Shipment tracking (P6): carrier + code, shipments page"
for tab in active problems delivered all not-a-tab; do
  expect 200 "/admin/orders/shipments?tab=$tab"
done
expect 200 "/admin/orders/shipments?tab=all&carrier=IranPost&q=12"
order_link=$(first_link "/admin/orders?status=Delivered" "/admin/orders/[0-9a-f-]\{36\}")
if [ -n "$order_link" ]; then
  expect 200 "$order_link"
  TOKEN=$(token)
  # کد نامعتبر پست (حروف) باید رد شود و کد ۲۴ رقمی ثبت شود؛ هر دو به صفحهٔ سفارش برمی‌گردند.
  expect 302 "$order_link?handler=Tracking" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "carrier=1" --data-urlencode "trackingCode=ABC"
  expect 302 "$order_link?handler=Tracking" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "carrier=1" --data-urlencode "trackingCode=۱۲۳۴۵۶۷۸۹۰۱۲۳۴۵۶۷۸۹۰۱۲۳۴"
  expect 200 "$order_link"
  contains_i "123456789012345678901234"
  expect 200 "/admin/orders/shipments?tab=all"
  contains_i "123456789012345678901234"
else
  echo "SKIP  no order for tracking test"
fi
echo "== Analytics (P7a): beacon, funnel, products, demand, campaigns"
UA_BROWSER="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36"
expect 204 /api/a/hit -X POST -A "$UA_BROWSER" -H "Content-Type: application/json" --data '{"p":"/","q":"?utm_source=smoke&utm_medium=test&utm_campaign=ci","r":""}'
expect 204 /api/a/hit -X POST -A "$UA_BROWSER" -H "Content-Type: application/json" --data '{"p":"/admin","q":"","r":""}'
expect 204 /api/a/hit -X POST -H "Content-Type: application/json" --data '{"p":"//evil","q":"","r":"javascript:alert(1)"}'
for period in today 7d 30d month all; do
  expect 200 "/admin/reports?period=$period"
done
contains_i "قیف فروش"
expect 200 "/admin/reports?heat=orders&from=1404/01/01&to=1405/12/29"
for sort in revenue units views carts conversion profit returns bogus; do
  expect 200 "/admin/reports/products?sort=$sort&filter=all"
done
for filter in sold nosales out bogus; do
  expect 200 "/admin/reports/products?filter=$filter&q=a"
done
expect 200 /admin/reports/demand
contains_i "جست‌وجوهای بی‌نتیجه"
expect 200 "/admin/reports/campaigns?by=campaign&period=all"
expect 200 "/admin/reports/campaigns?url=/product/x&src=Instagram&med=story&cmp=Yalda%201405"
contains_i "utm_campaign=yalda-1405"
expect 200 "/admin/reports/campaigns?url=javascript:alert(1)&src=x"
expect 200 "/admin/settings?tab=analytics"
echo "== Realtime (SignalR hub + unread counter)"
expect 200 "/hubs/live/negotiate?negotiateVersion=1" -X POST
contains_i "connectionToken"
expect 200 /api/notifications/unread
contains_i "unread"
for tab in general contact footer orders sla supplier tracking payment wallet security invoice seo loyalty gift-cards marketing support catalog map pwa performance; do
  expect 200 "/admin/settings?tab=$tab"
done
for key in $(curl -s -b "$JAR" "$BASE/admin/manage" | grep -o 'href="/admin/manage/[a-z-]*"' | sed 's/.*manage\///; s/"$//' | sort -u); do
  expect 200 "/admin/manage/$key"
  if grep -q "href=\"/admin/manage/$key/edit\"" /tmp/smoke-body; then expect 200 "/admin/manage/$key/edit"; fi
  item=$(grep -o "href=\"/admin/manage/$key/edit/[0-9a-f-]\{36\}[^\"]*\"" /tmp/smoke-body | head -1 | sed 's/^href="//; s/"$//; s/&amp;/\&/g')
  if [ -n "$item" ]; then expect 200 "$item"; fi
done

echo "== Admin panel: every navigation page and settings tab (P7b-P10)"
# فهرست از خود منوی پنل خوانده می‌شود تا هر صفحهٔ تازه‌ای که به منو اضافه شود خودکار بررسی شود.
curl -s -b "$JAR" "$BASE/admin" -o /tmp/smoke-nav
for path in $(grep -o 'href="/admin[^"]*" class="a-nav-link' /tmp/smoke-nav | sed 's/^href="//; s/" class=.*$//; s/&amp;/\&/g' | sort -u); do
  expect 200 "$path"
done
curl -s -b "$JAR" "$BASE/admin/settings" -o /tmp/smoke-settings
for tab in $(grep -o 'href="/admin/settings?tab=[a-z0-9-]*"' /tmp/smoke-settings | sed 's/.*tab=//; s/"$//' | sort -u); do
  expect 200 "/admin/settings?tab=$tab"
done

echo "== Staff security & teamwork (P10): account, tasks, approvals, reauth"
expect 200 /admin/account
contains_i "برنامهٔ احراز هویت"
expect 200 "/admin/reauth?returnUrl=/admin/orders"
expect 200 "/admin/reauth?returnUrl=https://evil.example"
# آدرس بیرونی هرگز مقصد بازگشت نمی‌شود؛ فرم‌ها به /admin برمی‌گردند.
contains 'name="returnUrl" value="/admin"'
lacks 'value="https://evil.example"'
expect 200 /admin/approvals
expect 200 "/admin/approvals?status=all&mine=true"
expect 200 "/admin/tasks?handler=Staff"
contains_i "handle"
expect 200 "/admin/tasks?new=1&url=/admin/orders&title=smoke"
TOKEN=$(token)
expect 302 "/admin/tasks?handler=Save" -X POST --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "title=کار تست دود" --data-urlencode "priority=3" --data-urlencode "url=/admin/orders"
expect 200 /admin/tasks
contains "کار تست دود"
for view in open done delegated bogus; do
  expect 200 "/admin/tasks?view=$view"
done

echo "== Admin panel: detail pages"
for spec in "/admin/orders|/admin/orders/[0-9a-f-]\{36\}" "/admin/customers|/admin/customers/[0-9a-f-]\{36\}" \
            "/admin/products|/admin/products/edit/[0-9a-f-]\{36\}" "/admin/blog|/admin/blog/edit/[0-9a-f-]\{36\}" \
            "/admin/tickets?state=all|/admin/tickets/[0-9a-f-]\{36\}" "/admin/returns?status=all|/admin/returns/[0-9a-f-]\{36\}" \
            "/admin/staff|/admin/staff/[a-z]*/[0-9a-f-]\{36\}"; do
  list="${spec%%|*}"; pattern="${spec#*|}"
  link=$(first_link "$list" "$pattern")
  if [ -n "$link" ]; then expect 200 "$link"; else echo "SKIP  no item on $list"; fi
done

echo "== Admin panel: exports"
for path in "/admin/orders?handler=export&format=csv" "/admin/finance/payments?handler=export&format=csv" "/admin/finance/wallet?handler=export&format=xlsx" \
            "/admin/customers?handler=export&format=csv" "/admin/messages?handler=export&format=csv&state=all" "/admin/gift-cards?handler=export&format=csv" \
            "/admin/returns?handler=export&format=xlsx&status=all" "/admin/orders/purchase-invoices?handler=export&format=csv" "/admin/finance?handler=export&format=xlsx"; do
  expect 200 "$path"
done

echo "== Admin panel: role permissions (content writer)"
admin_login 09120000004
expect 200 /admin/blog
expect 200 /admin/blog/edit
expect 302 /admin/finance
expect 302 /admin/settings
expect 302 /admin/staff
expect 302 /admin/orders/board
expect 302 /admin/orders/new
expect 302 /admin/products/supplier-sync
expect 302 /admin/products/bulk-price
expect 302 /admin/products/import
expect 302 /admin/orders/shipments

echo
echo "Smoke test finished with $FAILS failure(s)."
rm -f "$JAR"
[ "$FAILS" -eq 0 ]
