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

token() { grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' /tmp/smoke-body | head -1 | sed 's/.*value="//; s/"$//'; }

contains() {
  if grep -q "$1" /tmp/smoke-body; then pass "page contains: $1"; else fail "page does not contain: $1"; fi
}

echo "== Public pages"
for path in / /search /products /category/smart-watch /category/laptop /brand/apple "/search?discounted=true" \
            "/search?sort=BestSelling" "/search?minPrice=1000000&maxPrice=5000000&available=true" \
            /product/xiaomi-redmi-watch-3-active /product/sony-playstation-5 \
            /blog /blog/headphone-life-tips /blog/category/buying-guide \
            /faq /contact-us /about-us /rules /compare /coming-soon /login /register /cart /404; do
  want=200
  [ "$path" = "/404" ] && want=404
  expect "$want" "$path"
done

expect 200 "/search" -G --data-urlencode "q=اپل"
contains "MacBook"

expect 200 /product/xiaomi-redmi-watch-3-active
contains "ساعت هوشمند شیائومی"
contains "application/ld+json"

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
for path in /profile /profile/orders /profile/addresses /profile/favorites /profile/messages /profile/info /profile/password; do
  expect 200 "$path"
done

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

echo "== Checkout (cash on delivery)"
expect 200 /checkout
TOKEN=$(token)
ADDRESS=$(grep -o 'name="Input.AddressId" value="[0-9a-f-]\{36\}"' /tmp/smoke-body | head -1 | grep -o '[0-9a-f-]\{36\}')
TARGET=$(location "/checkout?handler=PlaceOrder" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Input.AddressId=$ADDRESS" --data-urlencode "Input.ShippingMethod=post" \
  --data-urlencode "Input.PaymentMethod=CashOnDelivery" --data-urlencode "Input.Note=smoke test")
case "$TARGET" in
  */checkout/success/*) pass "order placed -> $TARGET"; expect 200 "${TARGET#$BASE}"; contains "سفارش شما با موفقیت ثبت شد" ;;
  *) fail "place order redirected to '$TARGET'" ;;
esac

echo "== Checkout (online payment, sandbox gateway)"
expect 200 /product/apple-airpods-pro-2
TOKEN=$(token)
PRODUCT=$(grep -o 'name="productId" value="[0-9a-f-]\{36\}"' /tmp/smoke-body | head -1 | grep -o '[0-9a-f-]\{36\}')
expect 302 "/cart?handler=Add" --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "productId=$PRODUCT"
expect 200 /checkout
TOKEN=$(token)
TARGET=$(location "/checkout?handler=PlaceOrder" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Input.AddressId=$ADDRESS" --data-urlencode "Input.ShippingMethod=courier" \
  --data-urlencode "Input.PaymentMethod=OnlineGateway")
case "$TARGET" in
  */payment/start/*)
    pass "online order -> $TARGET"
    SANDBOX=$(location "${TARGET#$BASE}")
    case "$SANDBOX" in
      */payment/sandbox/*)
        pass "gateway -> $SANDBOX"
        expect 200 "${SANDBOX#$BASE}"
        CALLBACK=$(grep -o 'href="[^"]*status=ok"' /tmp/smoke-body | head -1 | sed 's/^href="//; s/"$//; s/&amp;/\&/g')
        DONE=$(location "${CALLBACK#$BASE}")
        case "$DONE" in
          */checkout/success/*) pass "payment verified -> $DONE"; expect 200 "${DONE#$BASE}"; contains "پرداخت با موفقیت انجام شد" ;;
          *) fail "payment callback redirected to '$DONE'" ;;
        esac ;;
      *) fail "payment start redirected to '$SANDBOX'" ;;
    esac ;;
  *) fail "online order redirected to '$TARGET'" ;;
esac

echo "== Review"
expect 200 /product/haylou-t15
TOKEN=$(token)
expect 302 "/product/haylou-t15?handler=Review" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Review.Title=عالی" --data-urlencode "Review.Content=کیفیت صدا خیلی خوب است." \
  --data-urlencode "Review.Rating=5" --data-urlencode "Review.IsRecommended=true"

echo "== Favorites"
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

echo
echo "Smoke test finished with $FAILS failure(s)."
rm -f "$JAR"
[ "$FAILS" -eq 0 ]
