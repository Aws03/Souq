#!/usr/bin/env bash
# ============================================================================
# تجربة استعادة على بنية تُرمى بعدها — الدليل الذي يحوّل "نأخذ نسخاً" إلى "نستطيع الاستعادة".
#
#   ./scripts/rehearse-restore.sh --set backups/souq-backup-…
#   ./scripts/rehearse-restore.sh --set … --api-image souq-api   # يشغّل التطبيق فوق المستعاد
#
# لا يلمس أي قاعدة قائمة: يُنشئ خادم SQL Server جديداً في حاوية بلا أي وحدة تخزين دائمة،
# يستعيد فيه، يتحقّق، ثم يهدم كل شيء (بما في ذلك عند الفشل أو المقاطعة).
# هذا هو المكان الوحيد المسموح فيه بالاستعادة بلا سؤال: الهدف وُلد قبل ثوانٍ ولا أحد يستخدمه.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

SET_DIR=""; API_IMAGE=""; KEEP=0
MSSQL_IMAGE="mcr.microsoft.com/mssql/server:2022-latest"
TARGET_DB="SouqDb"

usage() { sed -n '2,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; cat <<'USAGE'
الخيارات:
  --set <dir>          مجلد المجموعة (مطلوب)
  --api-image <image>  شغّل التطبيق فوق القاعدة المستعادة وافحص /health/ready
  --mssql-image <img>  الافتراضي mcr.microsoft.com/mssql/server:2022-latest
  --keep               لا تهدم البنية عند الانتهاء (للتشخيص — اهدمها بنفسك)
USAGE
exit "${1:-0}"; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --set) SET_DIR="$2"; shift 2;;
        --api-image) API_IMAGE="$2"; shift 2;;
        --mssql-image) MSSQL_IMAGE="$2"; shift 2;;
        --keep) KEEP=1; shift;;
        -h|--help) usage 0;;
        *) die "خيار غير معروف: $1";;
    esac
done
[[ -n "$SET_DIR" ]] || die "--set مطلوب."
[[ -f "$SET_DIR/database.bak" ]] || die "لا database.bak في '$SET_DIR'."
need docker

SUFFIX="$(date -u +%H%M%S)-$$"
NET="souq-rehearsal-$SUFFIX"
DB_C="souq-rehearsal-db-$SUFFIX"
API_C="souq-rehearsal-api-$SUFFIX"
SA_PASSWORD="Rehearsal-$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')-aA1!"

teardown() {
    local code=$?
    if [[ $KEEP -eq 1 ]]; then
        warn "--keep: البنية باقية. اهدمها بـ: docker rm -f $DB_C $API_C; docker network rm $NET"
        return $code
    fi
    step "الهدم"
    docker rm -f "$API_C" >/dev/null 2>&1 || true
    docker rm -f "$DB_C"  >/dev/null 2>&1 || true
    docker network rm "$NET" >/dev/null 2>&1 || true
    ok "لم يبقَ شيء."
    return $code
}
trap teardown EXIT INT TERM

step "بنية تُرمى بعدها: شبكة $NET وخادم $DB_C (بلا وحدة تخزين دائمة)"
docker network create "$NET" >/dev/null
docker run -d --name "$DB_C" --network "$NET" \
    -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=$SA_PASSWORD" \
    "$MSSQL_IMAGE" >/dev/null

export SOUQ_SQL_CONTAINER="$DB_C" SOUQ_SQL_SERVER="localhost" SOUQ_SQL_USER="sa" SOUQ_SQL_PASSWORD="$SA_PASSWORD"

step "انتظار الخادم"
for i in $(seq 1 60); do
    if sql_scalar "SELECT 1" >/dev/null 2>&1; then ok "جاهز بعد ${i}ث."; break; fi
    [[ $i -eq 60 ]] && die "الخادم لم يجهز خلال 60ث."
    sleep 1
done

step "الاستعادة"
"$(dirname "${BASH_SOURCE[0]}")/restore.sh" \
    --set "$SET_DIR" --target-database "$TARGET_DB" --container "$DB_C" --yes

# ── تحقّق أعمق مما يفعله restore.sh: بنية حقيقية لا مجرّد أعداد ──────────
step "فحوص ما بعد الاستعادة"
CHECKS_FAILED=0
check() {  # check <وصف> <استعلام> <المتوقَّع>
    local got; got="$(sql_scalar_in "$TARGET_DB" "$2")"
    if [[ "$got" == "$3" ]]; then ok "$1 ($got)"; else warn "$1: متوقَّع '$3' وجاء '$got'"; CHECKS_FAILED=1; fi
}
check "لا هجرة ناقصة مقارنةً بالبيان" \
      "SELECT TOP 1 [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC" \
      "$(manifest_get "$SET_DIR/manifest.txt" migration_head)"
check "مفاتيح أجنبية مركّبة على المستأجر سليمة" \
      "SELECT CASE WHEN COUNT(*) > 0 THEN 'yes' ELSE 'no' END FROM sys.foreign_keys WHERE [name] LIKE N'%Tenant%'" "yes"
check "لا صف بلا متجر في الطلبات" \
      "SELECT COUNT(*) FROM [Orders] WHERE [TenantId] IS NULL" "0"
check "لا قيد غير موثوق بعد الاستعادة" \
      "SELECT COUNT(*) FROM sys.foreign_keys WHERE [is_not_trusted] = 1 AND [is_disabled] = 0" "0"
check "القاعدة قابلة للكتابة (ليست في وضع استرداد)" \
      "SELECT CONVERT(nvarchar(20), DATABASEPROPERTYEX(N'$TARGET_DB', 'Status'))" "ONLINE"

# ── الاختبار الحقيقي: هل يخدم التطبيقُ هذه القاعدة؟ ─────────────────────
if [[ -n "$API_IMAGE" ]]; then
    step "تشغيل $API_IMAGE فوق القاعدة المستعادة"
    # منفذ على الحلقة المحلية فقط: الفحص التالي يحتاج ترويسة Host، وصورة التشغيل بلا curl
    # وصدفتها dash (لا /dev/tcp). المنفذ 0 يعني "اختر حرّاً" فلا تصادم مع أي شيء يعمل.
    docker run -d --name "$API_C" --network "$NET" -p 127.0.0.1:0:8080 \
        -e ASPNETCORE_ENVIRONMENT=Production \
        -e "ConnectionStrings__Default=Server=$DB_C,1433;Database=$TARGET_DB;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=True" \
        -e "Jwt__Key=rehearsal-only-key-0123456789abcdef0123456789abcdef" \
        -e Jwt__Issuer=Souq -e Jwt__Audience=SouqClient \
        -e Payments__Provider=Fake -e Email__Provider=Log \
        "$API_IMAGE" >/dev/null

    step "انتظار /health/ready"
    READY=0
    for i in $(seq 1 90); do
        if docker exec "$API_C" dotnet Souq.API.dll --health-check >/dev/null 2>&1; then
            ok "التطبيق أعلن جاهزيته فوق القاعدة المستعادة بعد ${i}ث."; READY=1; break
        fi
        sleep 1
    done
    if [[ $READY -eq 0 ]]; then
        warn "التطبيق لم يجهز — آخر السجل:"; docker logs --tail 30 "$API_C" >&2 || true
        CHECKS_FAILED=1
    else
        # جاهزية التطبيق تعني أيضاً: لا هجرة معلّقة على المخطّط المستعاد (DatabaseHealthCheck).
        # وهذه الخطوة تتجاوزها: طلب متجر حقيقي يُحدَّد من المضيف ويقرأ صفوفاً مستعادة.
        API_PORT="$(docker port "$API_C" 8080/tcp | head -1 | sed 's/.*://')"
        CONFIG_STATUS="$(curl -s -o /dev/null -w '%{http_code}' -H 'Host: localhost' \
            "http://127.0.0.1:$API_PORT/api/storefront/config" 2>/dev/null || true)"
        PRODUCTS="$(curl -s -H 'Host: localhost' "http://127.0.0.1:$API_PORT/api/products" 2>/dev/null | head -c 400 || true)"
        if [[ "$CONFIG_STATUS" == "200" ]]; then
            ok "واجهة المتجر تجيب 200 من البيانات المستعادة (المتجر تحدَّد من مضيفه)."
            [[ -n "$PRODUCTS" && "$PRODUCTS" != "[]" ]] \
                && ok "والكتالوج المستعاد يُقرأ فعلاً عبر الـ API." \
                || { warn "الكتالوج جاء فارغاً من قاعدة تحوي منتجات."; CHECKS_FAILED=1; }
        else
            warn "واجهة المتجر: ${CONFIG_STATUS:-لا استجابة}"; CHECKS_FAILED=1
        fi
    fi
fi

log ""
if [[ $CHECKS_FAILED -eq 0 ]]; then
    printf '%s✔ التجربة نجحت%s — هذه المجموعة استُعيدت وتحقَّقت على بنية نظيفة في %s\n' \
        "$GREEN$BOLD" "$OFF" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" >&2
else
    die "التجربة لم تنجح بالكامل — راجع التحذيرات أعلاه. لا تعتبر R-19 محلولاً."
fi
