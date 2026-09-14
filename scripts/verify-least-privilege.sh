#!/usr/bin/env bash
# ============================================================================
# يقيس ما تحتاجه هويّة التشغيل فعلاً (R-12) — ولا يكتفي بالادّعاء.
#
#   ./scripts/verify-least-privilege.sh --api-image souq-api
#
# على بنية تُرمى بعدها: ينشئ خادماً، يطبّق scripts/sql/least-privilege-logins.sql،
# يشغّل التطبيق بهويّتين منفصلتين (تشغيل/هجرات)، يمارس مسارات حقيقية، ثم:
#   • يفحص السجل بحثاً عن أي رفض صلاحية (يلتقط أعطال المهام الخلفية الصامتة أيضاً)،
#   • ويثبت عكسياً أن هوية التشغيل *لا* تستطيع تغيير المخطّط أو منح صلاحيات.
# الفحص العكسي هو الجزء الذي يجعل النتيجة ذات معنى: مسارات ناجحة وحدها لا تثبت أن
# الصلاحيات ضيّقة، فحساب db_owner ينجح فيها كلها أيضاً.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

API_IMAGE=""; KEEP=0
MSSQL_IMAGE="mcr.microsoft.com/mssql/server:2022-latest"
DB="SouqDb"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --api-image) API_IMAGE="$2"; shift 2;;
        --mssql-image) MSSQL_IMAGE="$2"; shift 2;;
        --keep) KEEP=1; shift;;
        -h|--help) sed -n '2,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0;;
        *) die "خيار غير معروف: $1";;
    esac
done
[[ -n "$API_IMAGE" ]] || die "--api-image مطلوب (مثلاً: docker compose build api ثم مرّر اسم الصورة)."
need docker; need curl

SUFFIX="$(date -u +%H%M%S)-$$"
NET="souq-privcheck-$SUFFIX"; DB_C="souq-privcheck-db-$SUFFIX"; API_C="souq-privcheck-api-$SUFFIX"
SA_PASSWORD="Priv-$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')-aA1!"
APP_PASSWORD="App-$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')-aA1!"
MIG_PASSWORD="Mig-$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')-aA1!"
FAILED=0

teardown() {
    local code=$?
    if [[ $KEEP -eq 1 ]]; then warn "--keep: اهدمها بنفسك: docker rm -f $DB_C $API_C; docker network rm $NET"; return $code; fi
    docker rm -f "$API_C" "$DB_C" >/dev/null 2>&1 || true
    docker network rm "$NET" >/dev/null 2>&1 || true
    return $code
}
trap teardown EXIT INT TERM

step "خادم يُرمى بعده"
docker network create "$NET" >/dev/null
docker run -d --name "$DB_C" --network "$NET" -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=$SA_PASSWORD" "$MSSQL_IMAGE" >/dev/null
export SOUQ_SQL_CONTAINER="$DB_C" SOUQ_SQL_SERVER="localhost" SOUQ_SQL_USER="sa" SOUQ_SQL_PASSWORD="$SA_PASSWORD"
for i in $(seq 1 60); do sql_scalar "SELECT 1" >/dev/null 2>&1 && break; [[ $i -eq 60 ]] && die "الخادم لم يجهز."; sleep 1; done
ok "جاهز."

step "إنشاء الهويّات من scripts/sql/least-privilege-logins.sql"
docker cp scripts/sql/least-privilege-logins.sql "$DB_C:/tmp/lp.sql" >/dev/null
docker exec -e SQLCMDPASSWORD="$SA_PASSWORD" "$DB_C" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -C -b -i /tmp/lp.sql \
    -v AppPassword="$APP_PASSWORD" -v MigratorPassword="$MIG_PASSWORD" >/dev/null \
    || die "فشل إنشاء الهويّات."
ok "souq_app و souq_migrator أُنشئا، والقاعدة [$DB] موجودة."

# قاعدة تطبيق أخرى على الخادم نفسه — نشر آخر، أو زبون آخر. الفحص العكسي لاحقاً يثبت
# أن هوية التشغيل لا تصل إليها: هذا هو العزل الذي يهمّ، لا رؤية بيانات master الوصفية.
sql "CREATE DATABASE [OtherApp];" >/dev/null
sql_run "CREATE TABLE dbo.Secret (Value nvarchar(50)); INSERT INTO dbo.Secret VALUES (N'not-yours');" -d "OtherApp" >/dev/null

step "تشغيل التطبيق بهويّتين منفصلتين"
docker run -d --name "$API_C" --network "$NET" -p 127.0.0.1:0:8080 \
    -e ASPNETCORE_ENVIRONMENT=Production \
    -e "ConnectionStrings__Default=Server=$DB_C,1433;Database=$DB;User Id=souq_app;Password=$APP_PASSWORD;TrustServerCertificate=True" \
    -e "ConnectionStrings__Migrations=Server=$DB_C,1433;Database=$DB;User Id=souq_migrator;Password=$MIG_PASSWORD;TrustServerCertificate=True" \
    -e "Jwt__Key=privcheck-only-key-0123456789abcdef0123456789abcdef" \
    -e Jwt__Issuer=Souq -e Jwt__Audience=SouqClient \
    -e Payments__Provider=Fake -e Email__Provider=Log \
    -e Seed__DemoData=true -e Seed__DefaultTenantHosts=localhost \
    -e Seed__AdminEmail=priv-admin@souq.test -e "Seed__AdminPassword=Priv-Admin-2026-xY" \
    "$API_IMAGE" >/dev/null

step "انتظار الجاهزية (الهجرات تجري بهوية المُهاجر)"
READY=0
for i in $(seq 1 120); do
    docker exec "$API_C" dotnet Souq.API.dll --health-check >/dev/null 2>&1 && { ok "جاهز بعد ${i}ث."; READY=1; break; }
    sleep 1
done
[[ $READY -eq 1 ]] || { warn "لم يجهز — السجل:"; docker logs --tail 40 "$API_C" >&2; die "الهجرات أو التشغيل فشلا بالصلاحيات المقترحة."; }

PORT="$(docker port "$API_C" 8080/tcp | head -1 | sed 's/.*://')"
API="http://127.0.0.1:$PORT"
H=(-H 'Host: localhost')

# ── مسارات حقيقية: كل واحد يلمس جداول مختلفة بقراءة وكتابة ────────────────
step "ممارسة مسارات تقرأ وتكتب"
probe() {  # probe <وصف> <المتوقَّع> <curl args…>
    local what="$1" expect="$2"; shift 2
    local code; code="$(curl -s -o /tmp/lp-body -w '%{http_code}' "$@" || echo 000)"
    if [[ "$code" == "$expect" ]]; then ok "$what ($code)"
    else warn "$what: متوقَّع $expect وجاء $code — $(head -c 200 /tmp/lp-body)"; FAILED=1; fi
}
EMAIL="priv-$(date +%s)@souq.test"
probe "إعداد المتجر (قراءة)"       200 "${H[@]}" "$API/api/storefront/config"
probe "قائمة المنتجات (قراءة)"      200 "${H[@]}" "$API/api/products"
probe "تسجيل عميل (كتابة)"          200 "${H[@]}" -X POST "$API/api/auth/register" \
      -H 'Content-Type: application/json' -d "{\"fullName\":\"فحص الصلاحيات\",\"email\":\"$EMAIL\",\"password\":\"Priv-Check-2026\"}"
probe "دخول (كتابة جلسة)"           200 "${H[@]}" -X POST "$API/api/auth/login" \
      -H 'Content-Type: application/json' -d "{\"email\":\"$EMAIL\",\"password\":\"Priv-Check-2026\"}"
TOKEN="$(curl -s "${H[@]}" -X POST "$API/api/auth/login" -H 'Content-Type: application/json' \
        -d "{\"email\":\"priv-admin@souq.test\",\"password\":\"Priv-Admin-2026-xY\"}" | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')"
[[ -n "$TOKEN" ]] && probe "لوحة الإدارة (قراءة عبر وحدات)" 200 "${H[@]}" -H "Authorization: Bearer $TOKEN" "$API/api/admin/inventory" \
                  || { warn "تعذّر دخول المدير."; FAILED=1; }
probe "سلّة الزائر (كتابة)"          200 "${H[@]}" -X POST "$API/api/basket/items" \
      -H 'Content-Type: application/json' -d '{"productId":1,"quantity":1}'

# ── المهام الخلفية: تفشل بصمت، فالسجل هو الشاهد ───────────────────────────
step "فحص السجل بحثاً عن أي رفض صلاحية"
sleep 5   # دورة صندوق الصادر والمهام الدورية
DENIED="$(docker logs "$API_C" 2>&1 | grep -iE 'permission was denied|permission denied|SELECT permission|INSERT permission|UPDATE permission|DELETE permission|EXECUTE permission|CREATE TABLE permission|Msg 229|Msg 262' || true)"
if [[ -n "$DENIED" ]]; then
    warn "رُفضت صلاحيات أثناء التشغيل — الأدوار المقترحة ناقصة:"; printf '%s\n' "$DENIED" | head -20 >&2; FAILED=1
else
    ok "لا رفض صلاحية واحد في السجل (المهام الخلفية شملها)."
fi

# ── الفحص العكسي: هل الهوية ضيّقة فعلاً؟ ──────────────────────────────────
step "إثبات أن هوية التشغيل مقيَّدة فعلاً"
export SOUQ_SQL_USER="souq_app" SOUQ_SQL_PASSWORD="$APP_PASSWORD"
denied() {  # denied <وصف> <جملة يجب أن تُرفض>
    if sql_run "$2" >/dev/null 2>&1; then warn "$1: نجحت وكان يجب أن تُرفض!"; FAILED=1
    else ok "$1: مرفوضة كما يجب."; fi
}
denied "إنشاء جدول"        "USE [$DB]; CREATE TABLE dbo.ShouldNotExist (Id int);"
denied "حذف جدول"          "USE [$DB]; DROP TABLE dbo.Orders;"
denied "منح صلاحية لنفسها" "USE [$DB]; ALTER ROLE db_owner ADD MEMBER [souq_app];"
denied "إنشاء قاعدة"       "CREATE DATABASE ShouldNotExist;"
# sys.sql_logins ليست فحص "مرفوض/مسموح": كل تسجيل دخول يرى صفّه هو دائماً (رؤية البيانات
# الوصفية في SQL Server)، ولا يمكن منع ذلك ولا معنى لمنعه — الحساب يعرف كلمته أصلاً.
# السؤال الحقيقي: هل يرى غيره؟ ذلك يتطلّب ALTER ANY LOGIN أو VIEW ANY DEFINITION.
# ما يظهر فعلاً: صفّه هو، وصفّ مالك القاعدة (الحساب الإداري الذي أنشأها). وجود sa ليس سرّاً —
# فهو موجود على كل خادم SQL Server — ولا سبيل لإخفاء مالك القاعدة عن مستخدميها.
VISIBLE="$(sql_scalar "SELECT ISNULL(STRING_AGG([name], ','), '(none)') FROM sys.sql_logins WHERE [name] <> SUSER_SNAME()")"
log "    تسجيلات الدخول المرئية عدا نفسه: $VISIBLE (مالك القاعدة — متوقَّع)"
# السؤال الأمني الحقيقي: هل يقرأ تجزئة كلمة مرور غيره؟ ذلك يفتح باب الكسر دون اتصال.
LEAKED="$(sql_scalar "SELECT COUNT(*) FROM sys.sql_logins WHERE [name] <> SUSER_SNAME() AND [password_hash] IS NOT NULL")"
if [[ "$LEAKED" == "0" ]]; then ok "لا يقرأ تجزئة كلمة مرور أي حساب آخر."
else warn "يقرأ تجزئة كلمة مرور $LEAKED حساب آخر — خطر كسر دون اتصال!"; FAILED=1; fi
# العزل الذي يهمّ: قاعدة تطبيق أخرى على الخادم نفسه.
# (ملاحظة: قراءة البيانات الوصفية العامة في master ممكنة لأي تسجيل دخول — guest مفعّل هناك
#  افتراضياً في SQL Server، ولا علاقة لذلك بما نمنحه هنا ولا بيانات تطبيق فيها.)
denied "قراءة قاعدة تطبيق أخرى" "SELECT COUNT(*) FROM [OtherApp].dbo.[Secret];"
# وفي المقابل: القراءة والكتابة العاديتان تعملان.
if sql_run "USE [$DB]; SELECT COUNT(*) FROM [Tenants];" >/dev/null 2>&1; then ok "قراءة الصفوف: تعمل."
else warn "قراءة الصفوف فشلت!"; FAILED=1; fi

log ""
if [[ $FAILED -eq 0 ]]; then
    printf '%s✔ قِيست وتحقّقت%s — db_datareader + db_datawriter تكفيان للتشغيل، و db_ddladmin\n' "$GREEN$BOLD" "$OFF" >&2
    printf '  + reader/writer تكفي للهجرات؛ ولا تستطيع هوية التشغيل تغيير المخطّط ولا ترقية نفسها.\n' >&2
else
    die "القياس لم ينجح بالكامل — لا تعلن أن R-12 محلول."
fi
