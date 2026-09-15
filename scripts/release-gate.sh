#!/usr/bin/env bash
# ============================================================================
# بوّابة الإصدار: تُنفّذ ما كان مكتوباً في قائمة تُقرأ باليد.
#
#   ./scripts/release-gate.sh --env-file .env --backup-dir /var/backups/souq \
#                             --base-url https://store.example --api-url http://api:8080 \
#                             --store-host store.example --suites
#
# ليست فحص وجود ملفات: كل قسم يفحص ثابتاً حقيقياً — إعدادٌ يُنتج نشراً آمناً، نسخٌ حيّة،
# مستودعٌ يبني ويمرّ اختباراته، ونشرٌ يجيب فعلاً.
#
# القاعدة الحاكمة: **ما لم يُفحص يُعلَن متخطّى بصوت عالٍ، ولا يُحسب نجاحاً.** بوّابة تمرّ
# لأنك لم تعطها شيئاً أسوأ من غياب البوّابة، لأنها تمنح ثقة لم تُكتسب.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
ENV_FILE=""; ENVIRONMENT="Production"; BACKUP_DIR=""; DRILL_DAYS=30
BASE_URL=""; API_URL=""; STORE_HOST="localhost"; COMPOSE_PROJECT=""
RUN_SUITES=0; REQUIRE_ALL=0
FAILED=0; SKIPPED=0; PASSED=0

usage() { sed -n '2,13p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; cat <<'USAGE'
الخيارات:
  --env-file <path> [--environment <name>]   افحص إعداد النشر الهدف
  --backup-dir <path> [--drill-days <n>]     افحص حياة النسخ الاحتياطية
  --base-url / --api-url / --store-host      شغّل فحص الدخان على نشر قائم
  --compose-project <name>                   يُمرَّر لفحص الدخان (سجلّات ونسخ)
  --suites                                   ابنِ وشغّل كل مجموعات الاختبار
  --require-all                              اعتبر أي قسم متخطّى فشلاً
USAGE
exit "${1:-0}"; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --env-file) ENV_FILE="$2"; shift 2;;
        --environment) ENVIRONMENT="$2"; shift 2;;
        --backup-dir) BACKUP_DIR="$2"; shift 2;;
        --drill-days) DRILL_DAYS="$2"; shift 2;;
        --base-url) BASE_URL="${2%/}"; shift 2;;
        --api-url) API_URL="${2%/}"; shift 2;;
        --store-host) STORE_HOST="$2"; shift 2;;
        --compose-project) COMPOSE_PROJECT="$2"; shift 2;;
        --suites) RUN_SUITES=1; shift;;
        --require-all) REQUIRE_ALL=1; shift;;
        -h|--help) usage 0;;
        *) die "خيار غير معروف: $1";;
    esac
done

section() { printf '\n%s── %s %s\n' "$BOLD" "$1" "$OFF" >&2; }
passed()  { printf '  %s✔%s %s\n' "$GREEN" "$OFF" "$1" >&2; PASSED=$((PASSED+1)); }
failed()  { printf '  %s✘%s %s\n' "$RED" "$OFF" "$1" >&2; FAILED=$((FAILED+1)); }
skipped() {
    printf '  %s—%s %s\n' "$YELLOW" "$OFF" "$1" >&2
    if [[ $REQUIRE_ALL -eq 1 ]]; then FAILED=$((FAILED+1)); else SKIPPED=$((SKIPPED+1)); fi
}

section "1 · إعداد النشر الهدف"
if [[ -n "$ENV_FILE" ]]; then
    if "$HERE/audit-config.sh" --env-file "$ENV_FILE" --environment "$ENVIRONMENT" >/tmp/gate-config.out 2>&1; then
        passed "الإعداد آمن لبيئة $ENVIRONMENT ($(grep '^RESULT' /tmp/gate-config.out))"
    else
        failed "الإعداد غير آمن لبيئة $ENVIRONMENT:"
        grep '^FAIL' /tmp/gate-config.out | sed 's/^/      /' >&2
    fi
    grep '^WARN' /tmp/gate-config.out | sed 's/^/      /' >&2 || true
else
    skipped "لم يُعطَ --env-file: إعداد النشر غير مفحوص"
fi

section "2 · حياة النسخ الاحتياطية"
if [[ -n "$BACKUP_DIR" ]]; then
    if "$HERE/backup-verify.sh" --dir "$BACKUP_DIR" --require-drill-within-days "$DRILL_DAYS" \
        >/tmp/gate-backup.out 2>&1; then
        passed "النسخ حديثة وسليمة وتجربة الاستعادة حديثة"
    else
        failed "النسخ الاحتياطية غير سليمة:"
        grep '^FAIL' /tmp/gate-backup.out | sed 's/^/      /' >&2
    fi
else
    skipped "لم يُعطَ --backup-dir: حياة النسخ غير مفحوصة"
fi

section "3 · المستودع: بناء واختبارات"
if [[ $RUN_SUITES -eq 1 ]]; then
    if (cd "$ROOT" && dotnet build -warnaserror --nologo -v q >/tmp/gate-build.out 2>&1); then
        passed "البناء نظيف والتحذيرات أخطاء"
    else
        failed "البناء فشل:"; tail -5 /tmp/gate-build.out | sed 's/^/      /' >&2
    fi
    if (cd "$ROOT" && dotnet test --nologo -v q >/tmp/gate-test.out 2>&1); then
        passed "كل مجموعات .NET خضراء ($(grep -c 'Passed!' /tmp/gate-test.out) مجموعات)"
    else
        failed "اختبارات فاشلة:"; grep -E 'Failed!' /tmp/gate-test.out | sed 's/^/      /' >&2
    fi
    # المدقّق وفحص الأنواع ضمن البوّابة منذ المرحلة 16: كلاهما أمسك عيوباً لا يراها البناء —
    # خطأ تحليل في JSX، وصفحتين تسقطان وقت التشغيل باستعمال متغيّر قبل تعريفه.
    if (cd "$ROOT/frontend" && npm run lint >/tmp/gate-fe.out 2>&1 \
          && npm run typecheck >>/tmp/gate-fe.out 2>&1 \
          && npm test >>/tmp/gate-fe.out 2>&1 \
          && npm run build >>/tmp/gate-fe.out 2>&1); then
        passed "الواجهة: مدقّق، أنواع، اختبارات، بناء"
    else
        failed "الواجهة:"; tail -8 /tmp/gate-fe.out | sed 's/^/      /' >&2
    fi
else
    skipped "لم يُطلب --suites: البناء والاختبارات غير مُشغَّلة"
fi

section "4 · اعتماديات ذات ثغرات"
if (cd "$ROOT" && dotnet list package --vulnerable --include-transitive 2>&1 \
      | grep -q "has the following vulnerable packages"); then
    failed "حزم NuGet ذات ثغرات معروفة"
else
    passed "لا حزمة NuGet ذات ثغرة معروفة"
fi
if (cd "$ROOT/frontend" && npm audit --omit=dev --audit-level=high >/dev/null 2>&1); then
    passed "لا ثغرة عالية فيما يُشحن إلى المتصفّح"
else
    failed "ثغرة عالية في اعتمادية تُشحن"
fi

section "5 · النشر القائم"
if [[ -n "$API_URL" ]]; then
    smoke=("$HERE/smoke-test.sh" --api-url "$API_URL" --store-host "$STORE_HOST")
    [[ -n "$BASE_URL" ]] && smoke+=(--base-url "$BASE_URL")
    [[ -n "$COMPOSE_PROJECT" ]] && smoke+=(--compose-project "$COMPOSE_PROJECT")
    if "${smoke[@]}" >/tmp/gate-smoke.out 2>&1; then
        passed "فحص الدخان: $(grep -oE 'نجح [0-9]+' /tmp/gate-smoke.out | tail -1)"
    else
        failed "فحص الدخان رفض النشر:"
        grep '✘' /tmp/gate-smoke.out | head -6 | sed 's/^/      /' >&2
    fi
else
    skipped "لم يُعطَ --api-url: النشر القائم غير مفحوص"
fi

log ""
printf '%s  نجح %d · فشل %d · متخطّى %d%s\n' "$BOLD" "$PASSED" "$FAILED" "$SKIPPED" "$OFF" >&2
if (( FAILED > 0 )); then
    die "بوّابة الإصدار: مرفوض."
fi
if (( SKIPPED > 0 )); then
    printf '%s⚠ مرّ ما فُحص — و%d قسماً لم يُفحص أصلاً.%s استعمل --require-all قبل إصدار حقيقي.\n' \
        "$YELLOW" "$SKIPPED" "$OFF" >&2
else
    printf '%s✔ كل أقسام البوّابة نجحت.%s\n' "$GREEN$BOLD" "$OFF" >&2
fi
