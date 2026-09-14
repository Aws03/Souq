#!/usr/bin/env bash
# ============================================================================
# نسخة احتياطية كاملة لمجموعة واحدة: قاعدة البيانات + الوسائط المرفوعة + بيان يصف
# ما التُقط بالضبط. الأسرار لا تُكتب هنا أبداً (انظر BackupAndRestore.md §3).
#
#   ./scripts/backup.sh --container souq-db-1 --uploads-container souq-api-1
#   ./scripts/backup.sh --server db.example.net --database SouqDb --out /mnt/backups
#
# يخرج بـ 0 فقط إذا نجح التحقّق (RESTORE VERIFYONLY) — نسخة غير قابلة للقراءة ليست نسخة.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

DATABASE="SouqDb"
OUT_ROOT="./backups"
SERVER_BACKUP_DIR="/var/opt/mssql/data"
UPLOADS_DIR=""
UPLOADS_CONTAINER=""
UPLOADS_CONTAINER_PATH="/app/wwwroot/uploads"
COMPRESSION=1

usage() {
    sed -n '2,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    cat <<'USAGE'
الخيارات:
  --database <name>            الافتراضي SouqDb
  --server <host[,port]>       خادم SQL (الافتراضي localhost)
  --user <login>               الافتراضي sa   [كلمة المرور من SOUQ_SQL_PASSWORD]
  --container <name>           نفّذ sqlcmd داخل هذه الحاوية بدل المضيف
  --server-backup-dir <path>   مسار يراه الخادم للكتابة فيه (الافتراضي /var/opt/mssql/data)
  --uploads-dir <path>         مجلد الوسائط على هذا الجهاز
  --uploads-container <name>   أو: انسخها من حاوية الـ API
  --uploads-container-path <p> الافتراضي /app/wwwroot/uploads
  --skip-uploads               لا تلتقط الوسائط (ناقصة عمداً — يُسجَّل في البيان)
  --out <dir>                  جذر الإخراج (الافتراضي ./backups)
  --no-compression             لخوادم لا تدعم ضغط النسخ
USAGE
    exit "${1:-0}"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --database) DATABASE="$2"; shift 2;;
        --server) SOUQ_SQL_SERVER="$2"; shift 2;;
        --user) SOUQ_SQL_USER="$2"; shift 2;;
        --container) SOUQ_SQL_CONTAINER="$2"; shift 2;;
        --server-backup-dir) SERVER_BACKUP_DIR="$2"; shift 2;;
        --uploads-dir) UPLOADS_DIR="$2"; shift 2;;
        --uploads-container) UPLOADS_CONTAINER="$2"; shift 2;;
        --uploads-container-path) UPLOADS_CONTAINER_PATH="$2"; shift 2;;
        --skip-uploads) UPLOADS_DIR=""; UPLOADS_CONTAINER=""; SKIP_UPLOADS=1; shift;;
        --out) OUT_ROOT="$2"; shift 2;;
        --no-compression) COMPRESSION=0; shift;;
        -h|--help) usage 0;;
        *) die "خيار غير معروف: $1 (جرّب --help)";;
    esac
done

STAMP="$(utc_stamp)"
SET_DIR="$OUT_ROOT/souq-backup-$STAMP"
BAK_NAME="$DATABASE-$STAMP.bak"
SERVER_BAK="$SERVER_BACKUP_DIR/$BAK_NAME"
mkdir -p "$SET_DIR"

step "قاعدة البيانات: $DATABASE على ${SOUQ_SQL_CONTAINER:-$SOUQ_SQL_SERVER}"

[[ "$(sql_scalar "SELECT COUNT(*) FROM sys.databases WHERE name = N'$DATABASE'")" == "1" ]] \
    || die "لا توجد قاعدة باسم '$DATABASE' على هذا الخادم."

# ── اللقطة ────────────────────────────────────────────────────────────────
# CHECKSUM: يحسب تجزئة كل صفحة أثناء الكتابة، فيكشف تلفاً صامتاً عند الاستعادة بدل اكتشافه
# بعد سنة. INIT: يستبدل محتوى الملف بدل الإلحاق به (ملف .bak يحتمل عدّة نسخ متراكمة).
backup_with() {
    sql "BACKUP DATABASE [$DATABASE] TO DISK = N'$SERVER_BAK' WITH INIT, CHECKSUM, STATS = 25$1" >/dev/null
}
if [[ $COMPRESSION -eq 1 ]]; then
    backup_with ", COMPRESSION" 2>/dev/null || {
        warn "الخادم رفض الضغط (إصدار لا يدعمه؟) — إعادة المحاولة بلا ضغط."
        backup_with ""
    }
else
    backup_with ""
fi
ok "أُخذت اللقطة."

step "التحقّق من قابلية القراءة قبل الوثوق بها"
sql "RESTORE VERIFYONLY FROM DISK = N'$SERVER_BAK' WITH CHECKSUM" >/dev/null \
    || die "RESTORE VERIFYONLY فشل — النسخة غير سليمة، لا تعتمد عليها."
ok "VERIFYONLY نجح."

# ── معلومات تُقارَن بعد الاستعادة ────────────────────────────────────────
MIGRATION="$(sql_scalar_in "$DATABASE" "SELECT TOP 1 [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC")"
MIGRATION_COUNT="$(sql_scalar_in "$DATABASE" "SELECT COUNT(*) FROM [__EFMigrationsHistory]")"
SQL_VERSION="$(sql_scalar "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion'))")"
ROWCOUNTS="$(sql_scalar_in "$DATABASE" "
    SELECT STRING_AGG(CONCAT(t.[name], ':', p.[rows]), ',') WITHIN GROUP (ORDER BY t.[name])
    FROM sys.tables t JOIN sys.partitions p ON p.[object_id] = t.[object_id] AND p.[index_id] IN (0,1)
    WHERE t.[name] IN (N'Tenants', N'Users', N'Orders', N'OrderItems', N'Products', N'Payments', N'Customers')")"

# ── إخراج ملف النسخة ─────────────────────────────────────────────────────
if [[ -n "$SOUQ_SQL_CONTAINER" ]]; then
    docker cp "$SOUQ_SQL_CONTAINER:$SERVER_BAK" "$SET_DIR/database.bak" >/dev/null
    docker exec "$SOUQ_SQL_CONTAINER" rm -f "$SERVER_BAK" || warn "تعذّر حذف النسخة المؤقتة داخل الحاوية."
else
    [[ -f "$SERVER_BAK" ]] || die "الخادم كتب إلى '$SERVER_BAK' وهو غير مرئي من هنا. استخدم --container أو مساراً مشتركاً."
    mv "$SERVER_BAK" "$SET_DIR/database.bak"
fi
ok "database.bak ($(du -h "$SET_DIR/database.bak" | awk '{print $1}'))"

# ── الوسائط ──────────────────────────────────────────────────────────────
UPLOADS_STATE="skipped"
if [[ -n "$UPLOADS_CONTAINER" ]]; then
    step "الوسائط من الحاوية $UPLOADS_CONTAINER"
    TMP_UP="$(mktemp -d)"; trap 'rm -rf "$TMP_UP"' EXIT
    docker cp "$UPLOADS_CONTAINER:$UPLOADS_CONTAINER_PATH" "$TMP_UP/uploads" >/dev/null
    tar -czf "$SET_DIR/uploads.tar.gz" -C "$TMP_UP" uploads
    UPLOADS_STATE="captured"
elif [[ -n "$UPLOADS_DIR" ]]; then
    step "الوسائط من $UPLOADS_DIR"
    [[ -d "$UPLOADS_DIR" ]] || die "لا مجلد '$UPLOADS_DIR'."
    tar -czf "$SET_DIR/uploads.tar.gz" -C "$(dirname "$UPLOADS_DIR")" "$(basename "$UPLOADS_DIR")"
    UPLOADS_STATE="captured"
else
    warn "بلا وسائط: الاستعادة ستعطي متجراً بصور مفقودة. مقصود؟ (--uploads-dir / --uploads-container)"
fi
[[ "$UPLOADS_STATE" == "captured" ]] && ok "uploads.tar.gz ($(du -h "$SET_DIR/uploads.tar.gz" | awk '{print $1}'))"

# ── البيان ───────────────────────────────────────────────────────────────
# لا سرّ هنا: معرّف مفتاح الأسرار اسمٌ لا قيمة — لكنه ضروري، فبدون المفتاح المقابل تبقى
# مفاتيح دفع المتاجر المشفَّرة في هذه النسخة غير قابلة للقراءة إلى الأبد.
cat > "$SET_DIR/manifest.txt" <<MANIFEST
created_at_utc=$STAMP
database=$DATABASE
source_server=${SOUQ_SQL_CONTAINER:-$SOUQ_SQL_SERVER}
sql_product_version=$SQL_VERSION
migration_head=$MIGRATION
migration_count=$MIGRATION_COUNT
row_counts=$ROWCOUNTS
uploads=$UPLOADS_STATE
secrets_key_id_required=${SECRETS_KEY_ID:-unknown}
secrets_included=no
tool=scripts/backup.sh
MANIFEST

( cd "$SET_DIR" && for f in database.bak uploads.tar.gz manifest.txt; do
    [[ -f "$f" ]] && printf '%s  %s\n' "$(sha256_of "$f")" "$f"
  done > SHA256SUMS )

ok "المجموعة: $SET_DIR"
log ""
sed 's/^/    /' "$SET_DIR/manifest.txt" >&2
log ""
warn "الخطوة الخاصة بالنشر (ليست هنا عمداً): انسخ هذا المجلد خارج هذا الجهاز، واحفظ"
warn "  الأسرار (SECRETS_KEY وغيره) في مخزن أسرار منفصل. BackupAndRestore.md §3 و §7."
