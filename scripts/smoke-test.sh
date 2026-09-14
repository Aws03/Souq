#!/usr/bin/env bash
# ============================================================================
# فحص الدخان لأول نشر — يُشغَّل بعد كل نشر، وأول مرّة قبل استقبال أي زبون.
#
#   ./scripts/smoke-test.sh --base-url http://localhost:8081 --api-url http://localhost:5201 \
#                           --store-host localhost --compose-project souq
#
# الترتيب مقصود: يبدأ بما لا يكتب شيئاً (حياة، جاهزية، تحديد متجر)، ثم يكتب بيانات اختبار
# موسومة بمعرّف تشغيل فريد، وينتهي بفحص السجلّات بحثاً عمّا كتبه هو نفسه.
#
# ما لا يفعله عمداً: لا يتظاهر بدفعة ناجحة. إن كانت البوّابة حقيقية (Stripe) يتوقّف عند حدّ
# إنشاء الطلب ويقول إن إتمام الدفع يحتاج بطاقة اختبار حقيقية — تزييف نجاح دفع في فحص دخان
# يعني أن الفحص يكذب في الحالة الوحيدة التي أُنشئ لأجلها.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

BASE_URL="http://localhost:8081"
API_URL="http://localhost:5201"
STORE_HOST="localhost"
COMPOSE_PROJECT=""
PASSED=0; FAILED=0; SKIPPED=0; EXTERNAL=0
RUN_ID="smoke-$(date -u +%Y%m%d%H%M%S)-$$"

usage() { sed -n '2,14p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; cat <<'USAGE'
الخيارات:
  --base-url <url>         عنوان الواجهة خلف الوكيل (الافتراضي http://localhost:8081)
  --api-url <url>          عنوان الـ API مباشرةً (الافتراضي http://localhost:5201)
  --store-host <host>      ترويسة Host لمتجر معروف (الافتراضي localhost)
  --compose-project <name> اسم حزمة Compose — يفعّل فحوص السجلّ والنسخ الاحتياطي
USAGE
exit "${1:-0}"; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --base-url) BASE_URL="${2%/}"; shift 2;;
        --api-url) API_URL="${2%/}"; shift 2;;
        --store-host) STORE_HOST="$2"; shift 2;;
        --compose-project) COMPOSE_PROJECT="$2"; shift 2;;
        -h|--help) usage 0;;
        *) die "خيار غير معروف: $1";;
    esac
done
need curl

pass()     { printf '  %s✔%s %s\n' "$GREEN" "$OFF" "$1" >&2; PASSED=$((PASSED+1)); }
fail()     { printf '  %s✘%s %s\n' "$RED" "$OFF" "$1" >&2; FAILED=$((FAILED+1)); }
skip()     { printf '  %s—%s %s\n' "$YELLOW" "$OFF" "$1" >&2; SKIPPED=$((SKIPPED+1)); }
external() { printf '  %s◆%s %s\n' "$YELLOW" "$OFF" "$1" >&2; EXTERNAL=$((EXTERNAL+1)); }
check()    { [[ "$2" == "$3" ]] && pass "[$2] $1" || fail "[got $2, want $3] $1"; }

# الحمولات تُبنى في متغيّرات قبل الاستدعاء، لا داخل $( ) مباشرةً: اقتباس مُهرَّب (\") داخل
# استبدال أمر داخل نصّ مقتبس يُحلّله bash تحليلاً مختلفاً عن المتوقّع، فيبتلع بقية السطر —
# وقد ابتلع هنا وسيطَ "القيمة المتوقّعة" فمرّ فحص فاشل على أنه ناجح. تشخيصه كلّف وقتاً.
api()  { curl -s -o /tmp/smoke-body -w '%{http_code}' -H "Host: $STORE_HOST" "$@" 2>/dev/null || echo 000; }
code() { curl -s -o /dev/null -w '%{http_code}' "$@" 2>/dev/null || echo 000; }
head_() { curl -sI "$@" 2>/dev/null || true; }
JSON=(-H 'Content-Type: application/json')
body() { cat /tmp/smoke-body 2>/dev/null; }
json() { body | sed -n "s/.*\"$1\":\([0-9]*\).*/\1/p" | head -1; }
jstr() { body | sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" | head -1; }

step "1–5 · الإقلاع والجاهزية والاتصال بالقاعدة"
check "الحيوية تجيب"            "$(code "$API_URL/health/live")" "200"
READY_CODE="$(curl -s -o /tmp/smoke-ready -w '%{http_code}' "$API_URL/health/ready" 2>/dev/null || echo 000)"
check "الجاهزية تجيب"           "$READY_CODE" "200"
if grep -q '"database":"Healthy"' /tmp/smoke-ready 2>/dev/null; then
    pass "الاتصال بالقاعدة سليم والمخطّط مطابق للإصدار"
else
    fail "الجاهزية لا تعلن قاعدة سليمة: $(cat /tmp/smoke-ready 2>/dev/null)"
fi
# الجاهزية تفشل عند مخطّط أقدم من الإصدار — وهذا ما يجعلها فحصاً لا زينة (Deployment.md §6).

step "6–9 · تحديد المتجر وعزله والكتالوج"
check "متجر معروف على مضيفه"     "$(api "$API_URL/api/storefront/config")" "200"
check "مضيف بلا متجر ⇒ 404"      "$(code -H 'Host: no-such-store.invalid' "$API_URL/api/storefront/config")" "404"
check "الحيوية لا تتأثّر بالمضيف" "$(code -H 'Host: no-such-store.invalid' "$API_URL/health/live")" "200"
check "الكتالوج يُقرأ"            "$(api "$API_URL/api/products")" "200"
PRODUCT_ID="$(body | sed -n 's/.*"id":\([0-9]*\).*/\1/p' | head -1)"
[[ -n "$PRODUCT_ID" ]] && pass "منتج للاختبار: #$PRODUCT_ID" || skip "لا منتجات — تخطّي فحوص السلة والطلب"

step "6 · المصادقة"
EMAIL="$RUN_ID@souq.test"; PASSWORD="Smoke-Test-2026"
REGISTER_BODY='{"fullName":"smoke","email":"'"$EMAIL"'","password":"'"$PASSWORD"'"}'
LOGIN_BODY='{"email":"'"$EMAIL"'","password":"'"$PASSWORD"'"}'
REG="$(api -X POST "$API_URL/api/auth/register" "${JSON[@]}" -d "$REGISTER_BODY")"
check "تسجيل عميل" "$REG" "200"
TOKEN="$(jstr accessToken)"
LOGIN="$(api -X POST "$API_URL/api/auth/login" "${JSON[@]}" -d "$LOGIN_BODY")"
check "الدخول" "$LOGIN" "200"
[[ -n "$TOKEN" ]] && pass "توكن وصول صادر" || fail "لا توكن وصول"
check "توكن خاطئ يُرفض" "$(code -H "Host: $STORE_HOST" -H 'Authorization: Bearer not-a-real-token' "$API_URL/api/account/profile")" "401"

step "10–12 · السلة والطلب"
if [[ -n "$PRODUCT_ID" && -n "$TOKEN" ]]; then
    AUTH=(-H "Authorization: Bearer $TOKEN")
    BASKET_BODY='{"productId":'"$PRODUCT_ID"',"quantity":1}'
    ORDER_BODY='{"shippingAddress":"'"$RUN_ID"' Amman","items":null,"couponCode":null}'
    ADD="$(api "${AUTH[@]}" -X POST "$API_URL/api/basket/items" "${JSON[@]}" -d "$BASKET_BODY")"
    check "إضافة إلى السلة" "$ADD" "200"
    check "قراءة السلة"      "$(api "${AUTH[@]}" "$API_URL/api/basket")" "200"
    ORDER="$(api "${AUTH[@]}" -X POST "$API_URL/api/orders" "${JSON[@]}" -d "$ORDER_BODY")"
    if [[ "$ORDER" == "201" ]]; then
        ORDER_ID="$(json orderId)"; pass "إنشاء الطلب (#$ORDER_ID)"
        check "الطلب يُقرأ لصاحبه" "$(api "${AUTH[@]}" "$API_URL/api/orders/$ORDER_ID")" "200"
    else
        fail "إنشاء الطلب: $ORDER — $(body | head -c 200)"
    fi
else
    skip "السلة والطلب (لا منتج أو لا توكن)"
fi

step "13 · سلوك الدفع"
api "$API_URL/api/payments/config" >/dev/null
PUBLISHABLE="$(jstr publishableKey)"
if [[ -z "$PUBLISHABLE" ]]; then
    pass "البوّابة تجريبية (لا مفتاح علني): لا مال حقيقي — صالح للعرض فقط، لا لزبائن"
else
    external "بوّابة حقيقية مضبوطة: إتمام دفعة يحتاج بطاقة اختبار من الحساب الحقيقي — لا يُزيَّف هنا"
fi

step "14 · البريد"
if [[ -n "$COMPOSE_PROJECT" ]]; then
    # سطر الإقلاع الصريح وحده، لا أي سطر يذكر كلمة email (تحذير "No email provider configured" مثلاً).
    PROVIDER="$(docker compose -p "$COMPOSE_PROJECT" logs api 2>/dev/null \
                | grep -o 'Adapters selected: payments [A-Za-z]*, email [A-Za-z]*' | tail -1 | awk '{print $NF}')"
    case "$PROVIDER" in
        Log) pass "مزوّد البريد Log: لا تصل أي رسالة (عرض توضيحي فقط)" ;;
        "")  skip "تعذّر قراءة مزوّد البريد من السجلّ" ;;
        *)   external "مزوّد البريد $PROVIDER: تأكّد يدوياً من وصول رسالة تسجيل حقيقية" ;;
    esac
else
    skip "مزوّد البريد (يحتاج --compose-project)"
fi

step "15–16 · الوكيل العكسي: الملفات المرفوعة والمخطّط"
check "الواجهة تُخدَم"           "$(code "$BASE_URL/")" "200"
check "الـ API عبر الوكيل"       "$(code -H "Host: $STORE_HOST" "$BASE_URL/api/storefront/config")" "200"
check "ملف مرفوع مفقود ⇒ 404 لا 500" "$(code -H "Host: $STORE_HOST" "$BASE_URL/uploads/does-not-exist.png")" "404"
NOSNIFF="$(head_ -H "Host: $STORE_HOST" "$BASE_URL/api/storefront/config" | grep -ci 'x-content-type-options' || true)"
check "ترويسات الأمان تمرّ عبر الوكيل" "$NOSNIFF" "1"
CSP_COUNT="$(head_ -H "Host: $STORE_HOST" "$BASE_URL/api/storefront/config" | grep -ci '^content-security-policy:' || true)"
check "سياسة محتوى واحدة لا مكرّرة" "$CSP_COUNT" "1"
HSTS_PLAIN="$(head_ -H "Host: $STORE_HOST" "$BASE_URL/api/storefront/config" | grep -ci 'strict-transport-security' || true)"
check "لا HSTS على http" "$HSTS_PLAIN" "0"
HSTS_TLS="$(head_ -H 'Host: smoke.example' -H 'X-Forwarded-Proto: https' "$BASE_URL/api/storefront/config" | grep -ci 'strict-transport-security' || true)"
check "HSTS حين يعلن الوكيل https" "$HSTS_TLS" "1"

step "17–18 · الربط والسرّية في السجلّات"
CORRELATION="$(head_ -H "Host: $STORE_HOST" "$API_URL/api/storefront/config" | grep -i 'x-correlation-id' | tr -d '\r' | awk '{print $2}' || true)"
[[ -n "$CORRELATION" ]] && pass "X-Correlation-Id على الاستجابة ($CORRELATION)" || fail "لا معرّف ربط"
if [[ -n "$COMPOSE_PROJECT" ]]; then
    LOGS="$(docker compose -p "$COMPOSE_PROJECT" logs 2>/dev/null)"
    # المطلوب أن يُربَط الطلب بسجلّه، لا أن يظهر جسمه: الأجسام لا تُسجَّل أصلاً (وهو المقصود).
    if printf '%s' "$LOGS" | grep -q "$CORRELATION"; then
        pass "معرّف الربط نفسه موجود في السجلّ — يُتتبَّع الطلب من ترويسته إلى سطره"
    else
        fail "معرّف ربط الاستجابة لا يظهر في السجلّ: لا يمكن ربط شكوى زبون بطلبه"
    fi
    if printf '%s' "$LOGS" | grep -q "$PASSWORD"; then fail "كلمة مرور الفحص ظهرت في السجلّ!"; else pass "لا كلمة مرور في السجلّ"; fi
    if printf '%s' "$LOGS" | grep -qi "Bearer $TOKEN"; then fail "توكن الوصول ظهر في السجلّ!"; else pass "لا توكن وصول في السجلّ"; fi
    if printf '%s' "$LOGS" | grep -qE '\?token=[A-Za-z0-9]'; then fail "رمز في سلسلة استعلام داخل السجلّ!"; else pass "لا رموز في سلاسل الاستعلام"; fi
else
    skip "فحص السجلّات (يحتاج --compose-project)"
fi

step "19–21 · النسخ الاحتياطي والاستعادة والرجوع"
if [[ -n "$COMPOSE_PROJECT" ]]; then
    if docker ps --format '{{.Names}}' | grep -q "^${COMPOSE_PROJECT}-db-1$"; then
        pass "حاوية القاعدة موجودة — نسخة احتياطية ممكنة بـ scripts/backup.sh"
        external "الاستعادة تُجرَّب بـ scripts/rehearse-restore.sh على بنية تُرمى — شغّلها قبل الافتتاح"
    else
        skip "حاوية القاعدة غير معروفة بهذا الاسم"
    fi
else
    skip "فحوص النسخ الاحتياطي (تحتاج --compose-project)"
fi
printf '  %s◆%s إجراء الرجوع: استعادة نسخة ثم نشر الصورة المطابقة — Migrations.md §7 و Deployment.md §11\n' "$YELLOW" "$OFF" >&2
EXTERNAL=$((EXTERNAL+1))

log ""
printf '%s  نجح %d · فشل %d · متخطّى %d · يحتاج تدخّلاً خارجياً %d%s\n' \
    "$BOLD" "$PASSED" "$FAILED" "$SKIPPED" "$EXTERNAL" "$OFF" >&2
log ""
if [[ $FAILED -gt 0 ]]; then
    die "فحص الدخان لم ينجح — لا تفتح المتجر."
fi
printf '%s✔ كل ما يمكن فحصه آلياً نجح.%s البنود الخارجية أعلاه تبقى مسؤولية بشرية.\n' "$GREEN$BOLD" "$OFF" >&2
