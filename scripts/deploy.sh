#!/usr/bin/env bash
# ============================================================================
# نشر نسخةٍ موسومة على بيئة، والرجوع عنها حين لا تُثبت نفسها (M18).
#
#   ./scripts/deploy.sh --version v1.4.0 --env-file .env --project souq \
#                       --api-url http://localhost:5201 --base-url http://localhost:8081
#
# ثلاثة أشياء تجعل هذا نشراً لا مجرّد `docker compose up`:
#
#   1. **للنسخة اسم.** الصور موسومة، فـ«ما كان يعمل قبل دقيقة» له عنوان يُنادى به. النشر بالبناء
#      من المصدر الحاضر لا يمكن الرجوع عنه أصلاً، لأنّ سابقه لم يُسمَّ قط.
#   2. **يُتحقَّق بعد النشر لا قبله.** `/health/ready` يجيب عن سؤال واحد: هل تخدم هذه النسخة طلباً
#      حقيقياً؟ وحتى يجيب بنعم فالنشر لم ينجح، مهما قال `docker compose up` — فهو يقول إنّ الحاوية
#      أقلعت، لا إنّ التطبيق يعمل. والفرق بينهما هو كلّ ما يهمّ في نشرٍ فاشل.
#   3. **الرجوع مشروطٌ بالمخطّط، لا تلقائيٌّ دائماً.** هجرات هذا المستودع توسّع وتنقل وتقلّص في خطوة
#      واحدة (Migrations.md §7)، فنسختان لا تتشاركان القاعدة عبر واحدةٍ منها. لذلك:
#        • لم يتحرّك المخطّط ⇒ رجوعٌ تلقائي بتبديل الوسم، وهو آمنٌ وسريع.
#        • تحرّك المخطّط ⇒ **يتوقّف ويقول ماذا يُفعل بالضبط.** تبديل الوسم حينها يُشغّل نسخةً قديمة
#          على مخطّطٍ لا تعرفه — عمودٌ قرأته أمس اختفى — وهو عطلٌ أسوأ من الذي نهرب منه.
#      هذا هو السبب الذي من أجله يقرأ هذا السكربت `__EFMigrationsHistory` قبل النشر وبعده.
#
# ADR-0046 يشرح لماذا هذا الشكل بالذات، و docs/09-OPERATIONS/Deployment.md §11 يصف الرجوع اليدوي.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"

VERSION=""; ENV_FILE=""; PROJECT="souq"; COMPOSE_FILES=("-f" "$ROOT/docker-compose.yml")
API_URL="http://localhost:5201"; BASE_URL=""; STORE_HOST="localhost"
READY_TIMEOUT=180; DO_BUILD=0; ALLOW_ROLLBACK=1; MIGRATE_BUNDLE=""; BACKUP_DIR=""; SMOKE=0

usage() { sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; cat <<'USAGE'
الخيارات:
  --version <tag>            وسم النسخة المنشورة (مطلوب). يُمرَّر إلى compose كـ SOUQ_VERSION
  --env-file <path>          ملف بيئة الحزمة
  --project <name>           اسم مشروع compose (الافتراضي souq)
  --compose-file <path>      ملفّ compose إضافي (يُكرَّر)
  --api-url <url>            عنوان الـ API للتحقّق (الافتراضي http://localhost:5201)
  --base-url <url>           عنوان الواجهة — يُشغّل فحص الدخان بعد نجاح الجاهزية
  --store-host <host>        مضيف المتجر لفحص الدخان
  --ready-timeout <seconds>  مهلة انتظار /health/ready (الافتراضي 180)
  --migrate-bundle <path>    حزمة هجرات تُنفَّذ قبل إقلاع النسخة (Database:MigrateOnStartup=false)
  --backup-dir <path>        خذ نسخة احتياطية قبل الترحيل — وهي ما يُستعاد إن تحرّك المخطّط وفشل النشر
  --build                    ابنِ الصور ووسِمها بهذه النسخة قبل النشر
  --no-rollback              لا ترجع تلقائياً؛ اترك الفشل قائماً للفحص
USAGE
exit "${1:-0}"; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) VERSION="$2"; shift 2;;
        --env-file) ENV_FILE="$2"; shift 2;;
        --project) PROJECT="$2"; shift 2;;
        --compose-file) COMPOSE_FILES+=("-f" "$2"); shift 2;;
        --api-url) API_URL="${2%/}"; shift 2;;
        --base-url) BASE_URL="${2%/}"; SMOKE=1; shift 2;;
        --store-host) STORE_HOST="$2"; shift 2;;
        --ready-timeout) READY_TIMEOUT="$2"; shift 2;;
        --migrate-bundle) MIGRATE_BUNDLE="$2"; shift 2;;
        --backup-dir) BACKUP_DIR="$2"; shift 2;;
        --build) DO_BUILD=1; shift;;
        --no-rollback) ALLOW_ROLLBACK=0; shift;;
        -h|--help) usage 0;;
        *) die "خيار غير معروف: $1";;
    esac
done

[[ -n "$VERSION" ]] || die "--version مطلوب. نشرٌ بلا وسم لا يمكن الرجوع عنه."
need docker
need curl

compose() {
    local args=(compose -p "$PROJECT")
    [[ -n "$ENV_FILE" ]] && args+=(--env-file "$ENV_FILE")
    args+=("${COMPOSE_FILES[@]}")
    SOUQ_VERSION="$VERSION" docker "${args[@]}" "$@"
}

# الوسم العامل الآن، من صورة حاوية الـ API نفسها لا من ملفّ حالة: ملفّ الحالة ينحرف عن الواقع
# بصمت، والحاوية العاملة لا تكذب عمّا تشغّله.
running_version() {
    local id image
    id="$(compose ps -q api 2>/dev/null | head -1)"
    [[ -n "$id" ]] || return 1
    image="$(docker inspect --format '{{.Config.Image}}' "$id" 2>/dev/null)" || return 1
    [[ "$image" == *:* ]] || return 1
    printf '%s' "${image##*:}"
}

# آخر هجرة مطبَّقة. غيابها (لا قاعدة، لا صلاحية، لا sqlcmd) ليس خطأً هنا — بل يعني أنّ حركة
# المخطّط غير معروفة، وهو ما يمنع الرجوع التلقائي بدل أن يسمح به على غير علم.
schema_head() {
    local container
    container="$(compose ps -q db 2>/dev/null | head -1)"
    [[ -n "$container" ]] || return 1
    [[ -n "${SOUQ_SQL_PASSWORD:-}" ]] || return 1
    SOUQ_SQL_CONTAINER="$container" \
        sql_scalar_in SouqDb "SELECT ISNULL(MAX(MigrationId), '(none)') FROM __EFMigrationsHistory" 2>/dev/null \
        || return 1
}

wait_ready() {
    local deadline=$(( SECONDS + READY_TIMEOUT )) code
    while (( SECONDS < deadline )); do
        code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$API_URL/health/ready" || true)"
        [[ "$code" == "200" ]] && return 0
        sleep 3
    done
    return 1
}

# ── ما هو عاملٌ الآن ─────────────────────────────────────────────────────
PREVIOUS="$(running_version || true)"
if [[ -n "$PREVIOUS" ]]; then
    step "النسخة العاملة: $PREVIOUS ⇒ $VERSION"
else
    warn "لا نسخة عاملة — هذا أوّل نشر، فلا رجوع ممكن إن فشل."
fi

SCHEMA_BEFORE="$(schema_head || true)"
if [[ -n "$SCHEMA_BEFORE" ]]; then
    log "المخطّط قبل النشر: $SCHEMA_BEFORE"
else
    warn "تعذّرت قراءة حالة المخطّط (SOUQ_SQL_PASSWORD أو حاوية db) — سيُعامَل المخطّط كأنّه تحرّك، فلا رجوع تلقائي."
fi

# ── البناء والوسم ────────────────────────────────────────────────────────
if (( DO_BUILD )); then
    step "بناء الصور ووسمها بـ $VERSION"
    compose build || die "فشل البناء — لم يُلمس أي شيء عامل."
fi

compose config --quiet || die "ملفّ compose غير صالح مع هذه القيم — أُوقف قبل أن يُلمس شيء."

# ── نسخة احتياطية قبل الترحيل ───────────────────────────────────────────
BACKUP_TAKEN=""
if [[ -n "$BACKUP_DIR" ]]; then
    step "نسخة احتياطية قبل الترحيل"
    "$HERE/backup.sh" --dir "$BACKUP_DIR" >&2 \
        || die "فشلت النسخة الاحتياطية — لا يُرحَّل مخطّطٌ بلا شبكة أمان."
    BACKUP_TAKEN="$(find "$BACKUP_DIR" -maxdepth 1 -type d -name 'souq-backup-*' | sort | tail -1)"
    ok "النسخة: $(basename "$BACKUP_TAKEN")"
else
    warn "بلا --backup-dir: إن تحرّك المخطّط وفشل النشر فلا نسخة تُستعاد. هذا هو الرجوع المفضّل (Deployment.md §11)."
fi

# ── خطوة الترحيل المتعمّدة ──────────────────────────────────────────────
# الترتيب الذي يجعلها تستحقّ العناء: رحّل، تحقّق، ثمّ أطلق. مع الترحيل عند الإقلاع تصير الثلاثة
# حدثاً واحداً، وتُطبَّق هجرةٌ سيّئة بمجرّد فعل النشر (R-18).
if [[ -n "$MIGRATE_BUNDLE" ]]; then
    [[ -x "$MIGRATE_BUNDLE" ]] || die "حزمة الهجرات غير قابلة للتنفيذ: $MIGRATE_BUNDLE"
    step "إيقاف التطبيق قبل الترحيل (لا نشر متدرّج عبر هجرة — Migrations.md §7)"
    compose stop api web >&2 || true
    step "تنفيذ حزمة الهجرات"
    "$MIGRATE_BUNDLE" --connection "${SOUQ_MIGRATIONS_CONNECTION:?SOUQ_MIGRATIONS_CONNECTION غير مضبوط لخطوة الترحيل}" >&2 \
        || die "فشل الترحيل — التطبيق ما زال موقوفاً والمخطّط كما كان. لا تُطلق النسخة الجديدة."
    ok "الهجرات مطبَّقة"
fi

# ── النشر ────────────────────────────────────────────────────────────────
step "نشر $VERSION"
DEPLOY_OK=0
# فشل `up` نفسه ليس خروجاً بل **أشيع أشكال النشر الفاشل**: نسخة لا تُقلع أصلاً، أو تسقط فحص
# صحّتها فيتوقّف compose عندها. فهو يسلك مسار الفشل ذاته ويصل إلى الرجوع — لا `die` تترك البيئة
# على نسخةٍ معطوبة لأنّ السكربت خرج قبل أن يُصلحها.
if compose up -d --no-build --pull missing >&2; then
    step "انتظار /health/ready (حتى ${READY_TIMEOUT}s)"
    if wait_ready; then
        ok "جاهز"
        DEPLOY_OK=1
    else
        warn "لم تُجب $API_URL/health/ready بـ 200 خلال ${READY_TIMEOUT}s"
    fi
else
    warn "لم تُقلع حاويات النسخة $VERSION (أو سقطت فحص صحّتها)."
fi

# فحص الدخان بعد الجاهزية: الجاهزية تقول إنّ الاتصال بالقاعدة قائم، وفحص الدخان يقول إنّ متجراً
# يُحدَّد وصفحةً تُخدَم وطلباً يُنشأ. نشرٌ يجتاز الأولى ويسقط في الثانية نشرٌ فاشل.
if (( DEPLOY_OK && SMOKE )); then
    step "فحص الدخان"
    "$HERE/smoke-test.sh" --base-url "$BASE_URL" --api-url "$API_URL" \
        --store-host "$STORE_HOST" --compose-project "$PROJECT" >&2 \
        || { warn "فشل فحص الدخان"; DEPLOY_OK=0; }
fi

SCHEMA_AFTER="$(schema_head || true)"
[[ -n "$SCHEMA_AFTER" ]] && log "المخطّط بعد النشر: $SCHEMA_AFTER"

if (( DEPLOY_OK )); then
    ok "النشر نجح: $VERSION"
    [[ -n "$SCHEMA_BEFORE" && "$SCHEMA_BEFORE" != "$SCHEMA_AFTER" ]] \
        && warn "تحرّك المخطّط ($SCHEMA_BEFORE ⇒ $SCHEMA_AFTER): الرجوع بعد الآن يحتاج استعادة، لا تبديل وسم."
    printf 'DEPLOY OK %s\n' "$VERSION"
    exit 0
fi

# ── الفشل: هل الرجوع آمن؟ ────────────────────────────────────────────────
SCHEMA_MOVED=1
[[ -n "$SCHEMA_BEFORE" && -n "$SCHEMA_AFTER" && "$SCHEMA_BEFORE" == "$SCHEMA_AFTER" ]] && SCHEMA_MOVED=0

if (( ! ALLOW_ROLLBACK )); then
    warn "--no-rollback: النشر الفاشل قائمٌ كما هو للفحص. سجلّات: docker compose -p $PROJECT logs api"
    printf 'DEPLOY FAIL %s (no rollback requested)\n' "$VERSION"; exit 1
fi

if [[ -z "$PREVIOUS" || "$PREVIOUS" == "$VERSION" ]]; then
    warn "لا نسخة سابقة يُرجَع إليها."
    printf 'DEPLOY FAIL %s (no previous version)\n' "$VERSION"; exit 1
fi

if (( SCHEMA_MOVED )); then
    # الحالة التي يكون فيها الرجوع التلقائي أسوأ من العطل: نسخةٌ قديمة على مخطّطٍ تحرّك.
    log ""
    warn "المخطّط تحرّك أو حالته غير معروفة ⇒ **لا رجوع تلقائي**."
    log "  نسخة قديمة على مخطّطٍ لا تعرفه تقرأ أعمدةً اختفت — عطلٌ أسوأ من الذي نهرب منه."
    log "  الرجوع هنا استعادةٌ ثمّ نشر النسخة المطابقة (Deployment.md §11):"
    if [[ -n "$BACKUP_TAKEN" ]]; then
        log "    ./scripts/restore.sh --set $BACKUP_TAKEN     # أوقف api أوّلاً"
    else
        log "    ./scripts/restore.sh --set <نسخة ما قبل النشر>   # لم تُؤخذ نسخة: مرّر --backup-dir في المرّة القادمة"
    fi
    log "    ./scripts/deploy.sh --version $PREVIOUS ...        # بعد اكتمال الاستعادة"
    printf 'DEPLOY FAIL %s (schema moved — manual restore required)\n' "$VERSION"
    exit 1
fi

step "المخطّط لم يتحرّك ⇒ رجوع تلقائي إلى $PREVIOUS"
VERSION="$PREVIOUS"
compose up -d --no-build --pull missing >&2 || die "فشل الرجوع أيضاً — تدخّل يدوي مطلوب فوراً."
if wait_ready; then
    ok "رجعت الخدمة إلى $PREVIOUS"
    printf 'DEPLOY FAIL (rolled back to %s)\n' "$PREVIOUS"
    exit 1
fi
die "الرجوع إلى $PREVIOUS لم يُصبح جاهزاً أيضاً — العطل ليس في النسخة الجديدة وحدها."
