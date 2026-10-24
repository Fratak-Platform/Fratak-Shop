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

echo "== SEO & security"
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
# Specification filters (f-{slug}): water resistance IPX5 -> Haylou only.
expect 200 /search -G --data-urlencode "f-water=IPX5"
contains "Haylou"
lacks "MacBook"
expect 200 /search
contains 'name="f-'
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
for path in /profile /profile/orders /profile/addresses /profile/favorites /profile/messages /profile/info /profile/password /profile/wallet /profile/sessions /profile/notifications; do
  expect 200 "$path"
done
expect 200 /profile/password
contains "دومرحله"
expect 200 /club
contains "rewardId"

echo "== Club: redeem points (500 -> wallet credit)"
REWARD=$(grep -o 'name="rewardId" value="[0-9a-f-]\{36\}"' /tmp/smoke-body | head -1 | grep -o '[0-9a-f-]\{36\}')
TOKEN=$(token)
if [ -n "$REWARD" ]; then
  expect 302 "/club?handler=Redeem" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "rewardId=$REWARD"
  expect 200 /club
else
  fail "no reward to redeem on /club"
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
    expect 200 /profile/notifications
    contains_i "$ORDER_NO"
    expect 200 "/profile/orders/$ORDER_NO"
    contains "لغو سفارش"
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
    expect 200 /profile/wallet; contains "پرداخت سفارش" ;;
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

echo "== Passwordless login (SMS code; development shows the code)"
rm -f "$JAR"; JAR="$(mktemp)"
expect 200 /login
TOKEN=$(token)
TARGET=$(location "/login?handler=SendCode" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "Otp.PhoneNumber=09125554433")
case "$TARGET" in
  */login/code*)
    pass "code sent -> $TARGET"
    expect 200 "${TARGET#$BASE}"
    CODE=$(grep -o 'class="font-bold" dir="ltr">[0-9]*' /tmp/smoke-body | grep -o '[0-9]*$')
    TOKEN=$(token)
    if [ -n "$CODE" ]; then
      DIGITS=()
      for ((i = 0; i < ${#CODE}; i++)); do DIGITS+=(--data-urlencode "Digits=${CODE:$i:1}"); done
      DONE=$(location "${TARGET#$BASE}" --data-urlencode "__RequestVerificationToken=$TOKEN" "${DIGITS[@]}")
      case "$DONE" in
        */welcome*|*/) pass "signed in with SMS code -> $DONE" ;;
        *) fail "SMS code login redirected to '$DONE'" ;;
      esac
      if grep -q "fratak_at" "$JAR"; then pass "session after SMS login"; else fail "no session after SMS login"; fi
    else
      fail "development code not shown on /login/code"
    fi ;;
  *) fail "send code redirected to '$TARGET'" ;;
esac

echo
echo "Smoke test finished with $FAILS failure(s)."
rm -f "$JAR"
[ "$FAILS" -eq 0 ]
