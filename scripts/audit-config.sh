#!/usr/bin/env bash
# ============================================================================
# هل يُنتج ملف الإعداد هذا نشراً *آمناً*؟ — لا "هل الملف موجود".
#
#   ./scripts/audit-config.sh --env-file .env --environment Production
#
# الفحوص هنا كلّها أشياء يقبلها التطبيق ويُقلع بها، لكنها خاطئة لزبائن حقيقيين. الإقلاع
# يرفض الإعداد *الناقص*؛ هذا يرفض الإعداد *الكامل والخطير*: بوّابة تجريبية، بريد لا يصل،
# بيانات عرض، قيمة نائبة منشورة، أو وكيل بلا حدود ثقة.
#
# يخرج بـ 0 حين لا خطأ، وبـ 1 عند أول خطأ. كل سطر يبدأ بـ FAIL/WARN/OK ليُقرأ آلياً.
# قراءة محضة: لا يتصل بشيء ولا يعدّل شيئاً.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

ENV_FILE=""
ENVIRONMENT="Production"
ERRORS=0
WARNINGS=0

usage() { sed -n '2,9p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; cat <<'USAGE'
الخيارات:
  --env-file <path>       ملف بصيغة KEY=VALUE (مطلوب)
  --environment <name>    Production (الافتراضي) أو Staging أو Development
USAGE
exit "${1:-0}"; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --env-file) ENV_FILE="$2"; shift 2;;
        --environment) ENVIRONMENT="$2"; shift 2;;
        -h|--help) usage 0;;
        *) die "خيار غير معروف: $1";;
    esac
done
[[ -n "$ENV_FILE" ]] || die "--env-file مطلوب."
[[ -f "$ENV_FILE" ]] || die "لا ملف: $ENV_FILE"

bad()  { printf 'FAIL  %s\n' "$1"; ERRORS=$((ERRORS+1)); }
soft() { printf 'WARN  %s\n' "$1"; WARNINGS=$((WARNINGS+1)); }
fine() { printf 'OK    %s\n' "$1"; }

# قراءة مفتاح دون تنفيذ الملف: ملف إعداد ليس سكربتاً يُصدَّق.
value() { sed -n "s/^[[:space:]]*$1=//p" "$ENV_FILE" | tail -1 | sed 's/^["'\'']//;s/["'\'']$//'; }

is_local() { [[ "$ENVIRONMENT" == "Development" || "$ENVIRONMENT" == "Testing" ]]; }
# تصغير الحروف بـ tr لا بـ ${VAR,,}: الأخيرة من bash 4، وmacOS يشحن 3.2 — و bash -n لا يكشفها
# لأنها خطأ وقت تنفيذ لا وقت تحليل. هذه النصوص تعمل على جهاز المطوّر وعلى الخط معاً.
lower() { printf '%s' "$1" | tr '[:upper:]' '[:lower:]'; }
# سلسلة واردة لا أنبوب: أنبوبٌ إلى `grep -q` تحت pipefail يقرأ الوجود غياباً على مدخلٍ كبير
# (OperationalScriptTests). القيم هنا قصيرة فلا يقع اليوم، والنمط يُمنع أينما كان.
placeholder() { grep -qiE 'replace|change_?me|placeholder|your_|example|todo' <<< "$1"; }

printf 'INFO  audit of %s for environment %s\n' "$ENV_FILE" "$ENVIRONMENT"

# ── أسرار لا يجوز أن تكون نائبة ─────────────────────────────────────────
JWT="$(value JWT_KEY)"
if [[ -z "$JWT" ]]; then bad "JWT_KEY فارغ — الإقلاع سيُرفض"
elif placeholder "$JWT"; then bad "JWT_KEY ما زال قيمة نائبة منشورة في المستودع — من يقرؤها يزوّر توكن أي متجر"
elif (( ${#JWT} < 32 )); then bad "JWT_KEY أقصر من 32 بايت"
else fine "JWT_KEY حقيقي وبطول كافٍ"; fi

DB_PASSWORD="$(value DB_SA_PASSWORD)"
if [[ -n "$DB_PASSWORD" ]] && placeholder "$DB_PASSWORD"; then
    bad "DB_SA_PASSWORD ما زال القيمة النائبة"
elif [[ -n "$DB_PASSWORD" ]]; then fine "DB_SA_PASSWORD مضبوط"; fi

# ── وسائل العرض التوضيحي لا تُشحن لزبائن ────────────────────────────────
PAYMENTS="$(value PAYMENTS_PROVIDER)"
if ! is_local && [[ "$(lower "$PAYMENTS")" == "fake" ]]; then
    bad "PAYMENTS_PROVIDER=Fake في $ENVIRONMENT — كل دفع يُعتبر ناجحاً بلا مال"
elif [[ "$(lower "$PAYMENTS")" == "fake" ]]; then fine "بوّابة تجريبية (مقبولة في $ENVIRONMENT)"
else fine "بوّابة الدفع ليست التجريبية"; fi

EMAIL="$(value EMAIL_PROVIDER)"
if ! is_local && [[ "$(lower "$EMAIL")" == "log" ]]; then
    bad "EMAIL_PROVIDER=Log في $ENVIRONMENT — لا تصل رسالة إعادة تعيين ولا تأكيد طلب"
else fine "مزوّد البريد ليس السجلّ"; fi

DEMO="$(value SEED_DEMO_DATA)"
if ! is_local && [[ "$(lower "$DEMO")" == "true" ]]; then
    bad "SEED_DEMO_DATA=true في $ENVIRONMENT — كتالوج عرض في قاعدة زبون"
else fine "بيانات العرض غير مطلوبة"; fi

# ── حدود الثقة ──────────────────────────────────────────────────────────
PROXY="$(value TRUSTED_PROXY_NETWORKS)"
if [[ "$PROXY" == "0.0.0.0/0" ]]; then
    bad "TRUSTED_PROXY_NETWORKS=0.0.0.0/0 — أي عميل يزوّر عنوانه ومخطّطه"
elif [[ -z "$PROXY" ]] && ! is_local; then
    soft "TRUSTED_PROXY_NETWORKS فارغ — خلف وكيل: لا HSTS، روابط بريد http، وحدّ معدّل مشترك للجميع"
else fine "شبكة الوكيل الموثوقة محدّدة"; fi

# ── أقلّ صلاحية ─────────────────────────────────────────────────────────
if [[ -z "$(value DB_MIGRATIONS_CONNECTION)" ]] && ! is_local; then
    soft "DB_MIGRATIONS_CONNECTION فارغ — التطبيق يعمل بهوية تستطيع تغيير المخطّط (R-12)"
else fine "هوية الهجرات منفصلة"; fi

# ── حسابات أولى ─────────────────────────────────────────────────────────
ADMIN_PASSWORD="$(value SEED_ADMIN_PASSWORD)"
if [[ -n "$ADMIN_PASSWORD" ]]; then
    if placeholder "$ADMIN_PASSWORD"; then bad "SEED_ADMIN_PASSWORD قيمة نائبة"
    elif (( ${#ADMIN_PASSWORD} < 12 )); then bad "SEED_ADMIN_PASSWORD أقصر من 12 محرفاً — الإقلاع سيُرفض"
    else fine "كلمة مرور المدير الأول مقبولة"; fi
fi

# ── أسرار اختيارية ذات أثر دائم ─────────────────────────────────────────
if [[ -z "$(value SECRETS_KEY)" ]] && ! is_local; then
    soft "SECRETS_KEY فارغ — لا متجر يربط حساب دفعه؛ الجميع يقبض في حساب النشر (D-13)"
else fine "مفتاح أسرار المتاجر مضبوط"; fi

FRONTEND="$(value FRONTEND_URL)"
if [[ -n "$FRONTEND" ]] && ! is_local && [[ "$FRONTEND" != https://* ]]; then
    soft "FRONTEND_URL ليس https — روابط البريد ستخرج بمخطّط غير مشفّر"
elif [[ -n "$FRONTEND" ]]; then fine "FRONTEND_URL مضبوط"; fi

printf 'RESULT %s errors=%d warnings=%d\n' "$([[ $ERRORS -eq 0 ]] && echo OK || echo FAIL)" "$ERRORS" "$WARNINGS"
exit $(( ERRORS > 0 ? 1 : 0 ))
