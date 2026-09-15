#!/usr/bin/env bash
# ============================================================================
# هل النسخ الاحتياطية *حيّة*؟ — لا "هل يوجد نص نسخ احتياطي".
#
#   ./scripts/backup-verify.sh --dir /var/backups/souq --max-age-hours 26
#   ./scripts/backup-verify.sh --dir /var/backups/souq --require-drill-within-days 30
#
# يخرج بـ 0 حين كل شيء سليم، وبـ 1 عند أول خلل — فيصلح مباشرةً كفحص لمراقب خارجي
# (cron + تنبيه، أو فحص جاهزية في نظام المراقبة). كل سطر خرج يبدأ بحالة قابلة للقراءة آلياً.
#
# ما يفحصه، وكلّه أعطال حقيقية رآها الناس:
#   • لا نسخة إطلاقاً (المهمّة لم تُجدوَل قط، أو صمتت منذ شهر)
#   • نسخة قديمة (المهمّة تفشل بصمت منذ أيام — أسوأ من غيابها لأنها تبدو موجودة)
#   • مجموعة ناقصة (قاعدة بلا بيان، أو بيان بلا تجزئات)
#   • تجزئة لا تطابق (تلف صامت في التخزين أو النقل)
#   • بيان تالف (حقل مهم يحمل نصّاً بدل قيمة — عطل رأيناه فعلاً)
#   • تجربة استعادة لم تُعد منذ مدّة (نسخة لم تُستعَد ليست نسخة بعد)
#
# ما لا يفعله: لا يتصل بقاعدة بيانات ولا يستعيد شيئاً. فحص قراءة محض، آمن على أي جدولة.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

DIR=""
MAX_AGE_HOURS=26          # ليلية + هامش: 24 تُطلق إنذاراً كاذباً عند أول تأخّر دقائق
REQUIRE_DRILL_DAYS=0      # 0 = لا تفحص تجربة الاستعادة
PROBLEMS=0

usage() { sed -n '2,10p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; cat <<'USAGE'
الخيارات:
  --dir <path>                      مجلد المجموعات (مطلوب)
  --max-age-hours <n>               أقصى عمر مقبول لأحدث نسخة (الافتراضي 26)
  --require-drill-within-days <n>   افحص أن تجربة استعادة جرت خلال هذه المدّة
USAGE
exit "${1:-0}"; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --dir) DIR="$2"; shift 2;;
        --max-age-hours) MAX_AGE_HOURS="$2"; shift 2;;
        --require-drill-within-days) REQUIRE_DRILL_DAYS="$2"; shift 2;;
        -h|--help) usage 0;;
        *) die "خيار غير معروف: $1";;
    esac
done
[[ -n "$DIR" ]] || die "--dir مطلوب."

problem() { printf 'FAIL  %s\n' "$1"; PROBLEMS=$((PROBLEMS+1)); }
good()    { printf 'OK    %s\n' "$1"; }
note()    { printf 'INFO  %s\n' "$1"; }

[[ -d "$DIR" ]] || { problem "مجلد النسخ غير موجود: $DIR"; printf 'RESULT FAIL %d\n' "$PROBLEMS"; exit 1; }

# أحدث مجموعة بالاسم: الطابع الزمني في الاسم بصيغة قابلة للترتيب معجمياً (UTC ISO مضغوط).
LATEST="$(find "$DIR" -maxdepth 1 -type d -name 'souq-backup-*' | sort | tail -1)"
if [[ -z "$LATEST" ]]; then
    problem "لا توجد أي مجموعة نسخ احتياطي في $DIR — المهمّة لم تُشغَّل قط أو تكتب في مكان آخر"
    printf 'RESULT FAIL %d\n' "$PROBLEMS"; exit 1
fi
note "أحدث مجموعة: $(basename "$LATEST")"

# ── العمر ────────────────────────────────────────────────────────────────
STAMP="$(basename "$LATEST" | sed 's/^souq-backup-//')"
AGE_HOURS=""
if command -v python3 >/dev/null 2>&1; then
    AGE_HOURS="$(python3 - "$STAMP" <<'PY'
import sys, datetime
try:
    taken = datetime.datetime.strptime(sys.argv[1], "%Y%m%dT%H%M%SZ").replace(tzinfo=datetime.timezone.utc)
    print(int((datetime.datetime.now(datetime.timezone.utc) - taken).total_seconds() // 3600))
except Exception:
    print("")
PY
)"
fi
if [[ -z "$AGE_HOURS" ]]; then
    note "تعذّر قراءة الطابع الزمني من الاسم؛ لا فحص عمر"
elif (( AGE_HOURS > MAX_AGE_HOURS )); then
    problem "أحدث نسخة عمرها ${AGE_HOURS} ساعة (الحدّ ${MAX_AGE_HOURS}) — المهمّة متوقّفة أو تفشل بصمت"
else
    good "العمر ${AGE_HOURS} ساعة، ضمن الحدّ ${MAX_AGE_HOURS}"
fi

# ── اكتمال المجموعة ──────────────────────────────────────────────────────
for required in database.bak manifest.txt SHA256SUMS; do
    [[ -f "$LATEST/$required" ]] && good "موجود: $required" || problem "ناقص: $required — المجموعة غير قابلة للاستعادة"
done

# ── سلامة التجزئات ───────────────────────────────────────────────────────
if [[ -f "$LATEST/SHA256SUMS" ]]; then
    mismatched=0
    while read -r want file; do
        [[ -f "$LATEST/$file" ]] || { problem "SHA256SUMS يذكر $file وهو غير موجود"; mismatched=1; continue; }
        got="$(sha256_of "$LATEST/$file")"
        [[ "$got" == "$want" ]] || { problem "تجزئة $file لا تطابق — تلف في التخزين أو النقل"; mismatched=1; }
    done < "$LATEST/SHA256SUMS"
    [[ $mismatched -eq 0 ]] && good "كل التجزئات تطابق"
fi

# ── سلامة البيان ─────────────────────────────────────────────────────────
# حقل يحمل نصّاً بدل قيمة يعني نسخة لا يمكن التحقّق من استعادتها لاحقاً.
if [[ -f "$LATEST/manifest.txt" ]]; then
    head="$(manifest_get "$LATEST/manifest.txt" migration_head)"
    rows="$(manifest_get "$LATEST/manifest.txt" row_counts)"
    if [[ -z "$head" || "$head" == *"Changed database context"* ]]; then
        problem "البيان بلا migration_head صالح — لا يمكن التحقّق من الاستعادة مقابله"
    else
        good "البيان يحمل الهجرة: $head"
    fi
    [[ -n "$rows" && "$rows" == *":"* ]] && good "البيان يحمل أعداد الصفوف" \
        || problem "البيان بلا أعداد صفوف — الاستعادة لن تكون قابلة للتحقّق"
    [[ "$(manifest_get "$LATEST/manifest.txt" uploads)" == "captured" ]] \
        && good "الوسائط ملتقطة" \
        || note "الوسائط غير ملتقطة في هذه المجموعة — الاستعادة ستعطي متجراً بصور مفقودة"
fi

# ── دليل تجربة الاستعادة ────────────────────────────────────────────────
if (( REQUIRE_DRILL_DAYS > 0 )); then
    DRILL="$DIR/LAST-RESTORE-DRILL"
    if [[ ! -f "$DRILL" ]]; then
        problem "لا دليل على أي تجربة استعادة — نسخة لم تُستعَد ليست نسخة بعد (scripts/rehearse-restore.sh)"
    else
        drill_age=""
        if command -v python3 >/dev/null 2>&1; then
            drill_age="$(python3 - "$(head -1 "$DRILL")" <<'PY'
import sys, datetime
try:
    when = datetime.datetime.strptime(sys.argv[1].strip(), "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=datetime.timezone.utc)
    print(int((datetime.datetime.now(datetime.timezone.utc) - when).total_seconds() // 86400))
except Exception:
    print("")
PY
)"
        fi
        if [[ -z "$drill_age" ]]; then
            problem "ملف تجربة الاستعادة غير مقروء: $DRILL"
        elif (( drill_age > REQUIRE_DRILL_DAYS )); then
            problem "آخر تجربة استعادة منذ ${drill_age} يوماً (الحدّ ${REQUIRE_DRILL_DAYS})"
        else
            good "تجربة استعادة ناجحة منذ ${drill_age} يوماً"
        fi
    fi
fi

if (( PROBLEMS == 0 )); then
    printf 'RESULT OK 0\n'
else
    printf 'RESULT FAIL %d\n' "$PROBLEMS"
fi
exit $(( PROBLEMS > 0 ? 1 : 0 ))
