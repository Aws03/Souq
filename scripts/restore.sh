#!/usr/bin/env bash
# ============================================================================
# استعادة مجموعة نسخ احتياطي إلى قاعدة *مُسمّاة صراحةً*.
#
#   ./scripts/restore.sh --set backups/souq-backup-… --target-database SouqDb_check --container souq-db-1
#
# الحواجز مقصودة، وهي جوهر هذا النص لا زينته:
#   • لا قاعدة هدف افتراضية. اسم الهدف يُكتب في كل مرة — لا استعادة "على المكان الصحيح" بالخطأ.
#   • هدف موجود ⇒ رفض، ما لم يُمرَّر --replace بوعي.
#   • بلا --yes: يجب كتابة اسم القاعدة يدوياً للتأكيد.
#   • التجزئات تُفحص قبل لمس أي شيء (نسخة تالفة تُكتشف قبل الاستعادة لا بعدها).
# استعادة فوق قاعدة إنتاج حيّة قرار تشغيلي، لا خطوة نص. BackupAndRestore.md §6.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

SET_DIR=""; TARGET=""; REPLACE=0; ASSUME_YES=0
DATA_DIR="/var/opt/mssql/data"
RESTORE_UPLOADS_TO=""

usage() { sed -n '2,16p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; cat <<'USAGE'
الخيارات:
  --set <dir>                  مجلد المجموعة (يحوي database.bak و manifest.txt)
  --target-database <name>     مطلوب — لا افتراضي
  --server <host[,port]> / --user <login> / --container <name>
  --data-dir <path>            مسار ملفات القاعدة على الخادم (الافتراضي /var/opt/mssql/data)
  --replace                    اسمح بالكتابة فوق قاعدة موجودة بهذا الاسم
  --restore-uploads-to <dir>   فكّ uploads.tar.gz هنا (لا يفعل شيئاً بلا هذا الخيار)
  --yes                        بلا سؤال تفاعلي (للأتمتة والتجريب)
USAGE
exit "${1:-0}"; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --set) SET_DIR="$2"; shift 2;;
        --target-database) TARGET="$2"; shift 2;;
        --server) SOUQ_SQL_SERVER="$2"; shift 2;;
        --user) SOUQ_SQL_USER="$2"; shift 2;;
        --container) SOUQ_SQL_CONTAINER="$2"; shift 2;;
        --data-dir) DATA_DIR="$2"; shift 2;;
        --replace) REPLACE=1; shift;;
        --restore-uploads-to) RESTORE_UPLOADS_TO="$2"; shift 2;;
        --yes) ASSUME_YES=1; shift;;
        -h|--help) usage 0;;
        *) die "خيار غير معروف: $1";;
    esac
done

[[ -n "$SET_DIR" ]] || die "--set مطلوب."
[[ -n "$TARGET"  ]] || die "--target-database مطلوب. لا اسم افتراضي هنا عمداً."
[[ -f "$SET_DIR/database.bak" ]] || die "لا database.bak في '$SET_DIR'."
[[ -f "$SET_DIR/manifest.txt" ]] || die "لا manifest.txt في '$SET_DIR' — مجموعة ناقصة، لا تُستعاد."

step "فحص سلامة المجموعة قبل أي تغيير"
if [[ -f "$SET_DIR/SHA256SUMS" ]]; then
    while read -r want file; do
        got="$(sha256_of "$SET_DIR/$file")"
        [[ "$got" == "$want" ]] || die "تجزئة $file لا تطابق SHA256SUMS — المجموعة تالفة أو مُعدَّلة."
    done < "$SET_DIR/SHA256SUMS"
    ok "كل التجزئات تطابق."
else
    warn "لا ملف SHA256SUMS — لا يمكن إثبات سلامة المجموعة."
fi

MIGRATION_HEAD="$(manifest_get "$SET_DIR/manifest.txt" migration_head)"
EXPECTED_ROWS="$(manifest_get "$SET_DIR/manifest.txt" row_counts)"

# ── الحاجز ───────────────────────────────────────────────────────────────
EXISTS="$(sql_scalar "SELECT COUNT(*) FROM sys.databases WHERE name = N'$TARGET'")"
if [[ "$EXISTS" == "1" && $REPLACE -eq 0 ]]; then
    die "توجد قاعدة باسم '$TARGET'. مرّر --replace إن كنت تقصد الكتابة فوقها — وتأكّد أنها ليست قاعدة حيّة."
fi

log ""
log "  المجموعة  : $SET_DIR"
log "  المصدر    : $(manifest_get "$SET_DIR/manifest.txt" source_server) / $(manifest_get "$SET_DIR/manifest.txt" database)"
log "  الهجرة    : $MIGRATION_HEAD"
log "  الخادم    : ${SOUQ_SQL_CONTAINER:-$SOUQ_SQL_SERVER}"
log "  الهدف     : ${BOLD}$TARGET${OFF}$([[ "$EXISTS" == "1" ]] && printf ' (موجودة — ستُستبدل)')"
log ""
if [[ $ASSUME_YES -eq 0 ]]; then
    [[ -t 0 ]] || die "لا مُدخل تفاعلي. مرّر --yes إن كنت متأكّداً."
    printf 'اكتب اسم القاعدة الهدف للمتابعة: ' >&2
    read -r typed < /dev/tty
    [[ "$typed" == "$TARGET" ]] || die "لم يطابق. أُلغيت العملية بلا أي تغيير."
fi

# ── نقل الملف إلى حيث يراه الخادم ────────────────────────────────────────
SERVER_BAK="$DATA_DIR/restore-$(utc_stamp).bak"
if [[ -n "$SOUQ_SQL_CONTAINER" ]]; then
    docker cp "$SET_DIR/database.bak" "$SOUQ_SQL_CONTAINER:$SERVER_BAK" >/dev/null
    # docker cp يحتفظ بمالك الملف على المضيف (uid غريب داخل الحاوية) وبوضع 640، وخادم SQL
    # يعمل بمستخدم mssql — فيرى "Operating system error 5 (Access is denied)" على ملف موجود
    # أمامه. الملكية جزء من النقل، لا تفصيل.
    docker exec -u 0 "$SOUQ_SQL_CONTAINER" chown mssql "$SERVER_BAK" 2>/dev/null \
        || warn "تعذّر ضبط ملكية ملف النسخة داخل الحاوية؛ قد يرفض الخادم قراءته."
    cleanup_bak() { docker exec "$SOUQ_SQL_CONTAINER" rm -f "$SERVER_BAK" 2>/dev/null || true; }
else
    cp "$SET_DIR/database.bak" "$SERVER_BAK"
    cleanup_bak() { rm -f "$SERVER_BAK"; }
fi
trap cleanup_bak EXIT

step "الأسماء المنطقية داخل النسخة"
# -w واسع: صفوف FILELISTONLY عريضة جداً، وبعرض السطر الافتراضي تُلفّ فينكسر التحليل.
FILELIST="$(sql_run "RESTORE FILELISTONLY FROM DISK = N'$SERVER_BAK'" -s "|" -w 8000 2>&1)" || {
    log "$FILELIST"; die "تعذّرت قراءة محتوى النسخة (انظر رسالة الخادم أعلاه)."
}
MOVES=""
while IFS='|' read -r logical _physical type _rest; do
    logical="$(printf '%s' "$logical" | sed 's/[[:space:]]*$//')"
    type="$(printf '%s' "$type" | tr -d ' \r')"
    [[ -n "$logical" && "$type" =~ ^[DL]$ ]] || continue
    ext=".mdf"; [[ "$type" == "L" ]] && ext="_log.ldf"
    MOVES="$MOVES, MOVE N'$logical' TO N'$DATA_DIR/${TARGET}${ext}'"
    log "    $logical ($type) → $DATA_DIR/${TARGET}${ext}"
done <<< "$FILELIST"
[[ -n "$MOVES" ]] || { log "$FILELIST"; die "لا ملفات بيانات في النسخة — هل الملف نسخة SQL Server صحيحة؟"; }

step "الاستعادة إلى [$TARGET]"
if [[ "$EXISTS" == "1" ]]; then
    sql "ALTER DATABASE [$TARGET] SET SINGLE_USER WITH ROLLBACK IMMEDIATE" >/dev/null
fi
sql "RESTORE DATABASE [$TARGET] FROM DISK = N'$SERVER_BAK' WITH REPLACE, CHECKSUM, RECOVERY$MOVES, STATS = 25" >/dev/null
[[ "$EXISTS" == "1" ]] && sql "ALTER DATABASE [$TARGET] SET MULTI_USER" >/dev/null
ok "استُعيدت."

# ── التحقّق بعد الاستعادة ────────────────────────────────────────────────
step "المقارنة بالبيان"
GOT_HEAD="$(sql_scalar_in "$TARGET" "SELECT TOP 1 [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC")"
GOT_ROWS="$(sql_scalar_in "$TARGET" "
    SELECT STRING_AGG(CONCAT(t.[name], ':', p.[rows]), ',') WITHIN GROUP (ORDER BY t.[name])
    FROM sys.tables t JOIN sys.partitions p ON p.[object_id] = t.[object_id] AND p.[index_id] IN (0,1)
    WHERE t.[name] IN (N'Tenants', N'Users', N'Orders', N'OrderItems', N'Products', N'Payments', N'Customers')")"

FAILED=0
[[ "$GOT_HEAD" == "$MIGRATION_HEAD" ]] || { warn "الهجرة: البيان $MIGRATION_HEAD، المستعاد $GOT_HEAD"; FAILED=1; }
[[ "$GOT_ROWS" == "$EXPECTED_ROWS"  ]] || { warn "الصفوف: البيان [$EXPECTED_ROWS]، المستعاد [$GOT_ROWS]"; FAILED=1; }
[[ $FAILED -eq 0 ]] && ok "الهجرة والصفوف تطابق البيان: $GOT_ROWS"

if [[ -n "$RESTORE_UPLOADS_TO" ]]; then
    if [[ -f "$SET_DIR/uploads.tar.gz" ]]; then
        step "فكّ الوسائط إلى $RESTORE_UPLOADS_TO"
        mkdir -p "$RESTORE_UPLOADS_TO"
        tar -xzf "$SET_DIR/uploads.tar.gz" -C "$RESTORE_UPLOADS_TO"
        ok "فُكَّت."
    else
        warn "المجموعة بلا uploads.tar.gz — الصور ستكون مفقودة."
    fi
fi

log ""
warn "لم يكتمل الأمر بعد: الأسرار ليست في هذه المجموعة. بلا SECRETS_KEY الأصلي تبقى مفاتيح"
warn "  دفع المتاجر المشفَّرة غير قابلة للقراءة، وبلا JWT_KEY تسقط كل الجلسات. BackupAndRestore.md §3."
[[ $FAILED -eq 0 ]] || die "الاستعادة تمّت لكن التحقّق لم يطابق البيان — افحص قبل الاعتماد عليها."
