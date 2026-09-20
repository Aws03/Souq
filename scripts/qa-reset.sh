#!/usr/bin/env bash
# ============================================================================
# يُعيد متجر الـ QA إلى خطّ أساسٍ معلوم قبل مشوار رحلات كامل.
#
# لماذا؟ (TD-57)
# رحلات المتصفّح تتشارك متجراً واحداً ولا تنظّف بعدها — وهذا مقصود: الأرشفة لا تُلغى،
# وحسابات المنصّة لا تُحذف. لكنّ الأثر التراكمي أنّ المتجر يكبر بلا حدّ: قِيس 275 صفّ جرد
# في 2026-09-19 صباحاً، و400 مساءً، وكل مشوار يضيف. ورحلاتٌ تبحث عن سلعتها داخل قائمة
# مشتركة تكبر بلا حدّ تبدأ بالسقوط واحدةً بعد أخرى — لا لعيبٍ في المنتج، بل لأنّ فرضية
# "سلعتي ضمن أوّل صفحات الشاشة" تصحّ على متجرٍ صغير وحده.
#
# سُدّت في M19 وبعدها أربعة أسباب بعينها (هيكل تحميل، قائمة قديمة، إشعار زائل، وفرضية
# الصفحة الأولى). وما يبقى بعدها ليس عيباً يُصلَح مرّةً، بل **نموّاً** — وعلاجه أن يبدأ
# المشوار من حجمٍ معلوم، لا أن تُرقَّع كل قائمة على حدة كلّما تجاوزت حدّاً جديداً.
#
# ما يفعله: يحذف حجم قاعدة الـ QA ومرفوعاتها، ويُقلع الحزمة فتُعيد الهجرة والبذر
# (بيانات العرض تُبذَر بـ SEED_DEMO_DATA=true)، ثمّ ينتظر الجاهزية.
#
# **مدمِّر بالتعريف.** لذلك: لا حزمة افتراضية، ويرفض ما لم يُمرَّر --yes.
# لا يُشغَّل على حزمةٍ فيها بيانات يريدها أحد. بيانات الـ QA تُعاد بناؤها بالتعريف.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT=""; ENV_FILE=""; EXTRA_FILES=(); CONFIRMED=0

usage() { sed -n '2,24p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; cat <<'USAGE'
الخيارات:
  --project <name>     اسم حزمة Compose (إلزامي — لا افتراضي لفعلٍ مدمِّر)
  --env-file <path>    ملف البيئة الذي أُقلعت به الحزمة
  --file <path>        ملف compose إضافي (يُكرَّر)
  --yes                تأكيد الحذف
USAGE
exit "${1:-0}"; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --project) PROJECT="$2"; shift 2;;
        --env-file) ENV_FILE="$2"; shift 2;;
        --file) EXTRA_FILES+=(-f "$2"); shift 2;;
        --yes) CONFIRMED=1; shift;;
        -h|--help) usage 0;;
        *) die "خيار غير معروف: $1";;
    esac
done

need docker
[[ -n "$PROJECT" ]] || die "--project إلزامي: لا حزمة افتراضية لفعلٍ يحذف بيانات."
[[ $CONFIRMED -eq 1 ]] || die "سيُحذف حجم قاعدة الحزمة '$PROJECT' ومرفوعاتها. أعد الأمر مع --yes."

compose() {
    local args=(-p "$PROJECT")
    [[ -n "$ENV_FILE" ]] && args+=(--env-file "$ENV_FILE")
    args+=(-f "$ROOT/docker-compose.yml" "${EXTRA_FILES[@]}")
    docker compose "${args[@]}" "$@"
}

step "إيقاف الحزمة وحذف حجومها (قاعدة + مرفوعات)"
compose down -v

step "إقلاع الحزمة — الهجرة والبذر يجريان عند أوّل إقلاع"
compose up -d

step "انتظار /health/ready"
READY=""
for _ in $(seq 1 90); do
    if [[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5201/health/ready || true)" == "200" ]]; then
        READY=yes; break
    fi
    sleep 5
done
[[ -n "$READY" ]] || die "لم تبلغ الحزمة الجاهزية — اقرأ: docker compose -p $PROJECT logs api"

COUNT="$(curl -s 'http://localhost:8081/api/products?pageSize=1' | sed -n 's/.*"totalCount":\([0-9]*\).*/\1/p')"
ok "المتجر عند خطّ الأساس: ${COUNT:-?} منتجاً"
log ""
log 'خطّ الأساس ينقص متجراً ثانياً — وcross-tenant-adversarial.spec.js يحتاجه:'
log "  python3 scripts/qa-second-store.py --skip-admin \\"
log "      --api http://127.0.0.1:5201 --api-log /dev/null \\"
log "      --owner-email <platform owner> --owner-password <password>"
log "  (يعمل على حزمة Production. ولرحلات second-tenant.spec.js شغّله بلا --skip-admin على API تطوير)"
