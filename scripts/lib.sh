#!/usr/bin/env bash
# ============================================================================
# أدوات مشتركة لنصوص النسخ الاحتياطي والاستعادة (docs/09-OPERATIONS/BackupAndRestore.md).
# لا تُشغَّل وحدها — تُضمَّن: . "$(dirname "$0")/lib.sh"
#
# مبدأ ناظم: لا شيء هنا يعرف مزوّد الاستضافة. النصوص تتكلّم SQL Server قياسي وملفات،
# والخطوة الوحيدة الخاصة بالنشر (نسخ المجموعة إلى مكان آخر) معزولة ومُعلَّمة صراحةً.
# ============================================================================
set -euo pipefail

RED=$'\033[31m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'; BOLD=$'\033[1m'; OFF=$'\033[0m'
[[ -t 1 ]] || { RED=''; GREEN=''; YELLOW=''; BOLD=''; OFF=''; }

log()  { printf '%s\n' "$*" >&2; }
step() { printf '%s==>%s %s\n' "$BOLD" "$OFF" "$*" >&2; }
warn() { printf '%s[warn]%s %s\n' "$YELLOW" "$OFF" "$*" >&2; }
ok()   { printf '%s[ok]%s %s\n'   "$GREEN"  "$OFF" "$*" >&2; }
die()  { printf '%s[error]%s %s\n' "$RED" "$OFF" "$*" >&2; exit 1; }

need() { command -v "$1" >/dev/null 2>&1 || die "'$1' غير موجود في PATH."; }

# ── تشغيل sqlcmd ───────────────────────────────────────────────────────────
# ثلاث حالات، بنفس الواجهة: sql "<T-SQL>"  و  sql_scalar "<T-SQL>"
#   SOUQ_SQL_CONTAINER=<name>  ⇒ داخل حاوية (حزمة Compose، أو حاوية التجريب)
#   sqlcmd محلي                ⇒ يُستخدم مباشرة
# المصادقة من SOUQ_SQL_SERVER / SOUQ_SQL_USER / SOUQ_SQL_PASSWORD.
# كلمة المرور لا تُمرَّر أبداً على سطر أوامر يراه `ps` على المضيف: تُمرَّر بيئةً
# (-P من متغيّر داخل الحاوية) أو عبر SQLCMDPASSWORD للـ sqlcmd المحلي.
: "${SOUQ_SQL_SERVER:=localhost}"
: "${SOUQ_SQL_USER:=sa}"
: "${SOUQ_SQL_CONTAINER:=}"

sqlcmd_bin_in_container() {
    local c="$1"
    for p in /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd; do
        if docker exec "$c" test -x "$p" 2>/dev/null; then printf '%s' "$p"; return 0; fi
    done
    die "لا sqlcmd داخل الحاوية '$c'."
}

sql_run() {   # sql_run <extra-args...> -- <query>
    local query="$1"; shift || true
    [[ -n "${SOUQ_SQL_PASSWORD:-}" ]] || die "SOUQ_SQL_PASSWORD غير مضبوط."
    if [[ -n "$SOUQ_SQL_CONTAINER" ]]; then
        local bin; bin="$(sqlcmd_bin_in_container "$SOUQ_SQL_CONTAINER")"
        docker exec -e SQLCMDPASSWORD="$SOUQ_SQL_PASSWORD" "$SOUQ_SQL_CONTAINER" \
            "$bin" -S "$SOUQ_SQL_SERVER" -U "$SOUQ_SQL_USER" -C -b -h -1 -W "$@" -Q "$query"
    else
        need sqlcmd
        SQLCMDPASSWORD="$SOUQ_SQL_PASSWORD" \
            sqlcmd -S "$SOUQ_SQL_SERVER" -U "$SOUQ_SQL_USER" -C -b -h -1 -W "$@" -Q "$query"
    fi
}

sql() { sql_run "$1"; }

# قيمة واحدة، بلا ترويسة ولا سطر "rows affected".
sql_scalar() { sql_run "SET NOCOUNT ON; $1" | sed '/^$/d' | head -1 | tr -d '\r'; }

# نفسه داخل قاعدة بعينها. تُختار بـ -d لا بـ USE: فـ USE تطبع "Changed database context to…"
# وهي رسالة إعلامية تصبح أول سطر في الخرج، فتُقرأ كأنها القيمة.
sql_scalar_in() { local db="$1"; shift; sql_run "SET NOCOUNT ON; $1" -d "$db" | sed '/^$/d' | head -1 | tr -d '\r'; }

# ── تجزئة ووقت ─────────────────────────────────────────────────────────────
sha256_of() {
    if command -v shasum >/dev/null 2>&1; then shasum -a 256 "$1" | awk '{print $1}'
    else need sha256sum; sha256sum "$1" | awk '{print $1}'; fi
}

utc_stamp() { date -u +%Y%m%dT%H%M%SZ; }

# قراءة قيمة من manifest.txt (صيغة key=value).
manifest_get() { sed -n "s/^$2=//p" "$1" | head -1; }
