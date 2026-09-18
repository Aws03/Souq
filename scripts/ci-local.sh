#!/usr/bin/env bash
# ============================================================================
# تشغيل بوّابة CI على **Linux** من آلة التطوير، قبل الدفع (M18).
#
#   ./scripts/ci-local.sh [--frontend]
#
# لماذا يوجد هذا أصلاً: خطّ CI يعمل على ubuntu، والتطوير هنا يجري على macOS. وهذه الفجوة
# أخفت ثلاثة أعطال حقيقية اكتُشفت كلّها في M18 دفعةً واحدة، ولم يكن أيٌّ منها ظاهراً محلّياً:
#
#   1. **قاعدة تعدّ اختبارات الواجهة أعطت رقمين مختلفين على النظامين** لنفس البايتات ونفس .NET —
#      `.*` شَرِهٌ داخل مجموعة اختيارية، وتحسين «الذرّية التلقائية» يحسمه على نحوٍ يختلف بين
#      المنصّتين. الجرد المولَّد مُلتزَمٌ في المستودع، فكان الخطّ أحمر على كل دفعة.
#   2. **تحذيرا `using` مكرّر** مرّا محلّياً وهما خطآن في الخطّ (`-warnaserror`).
#   3. **`backup-verify.sh` كان يتخطّى فحص العمر بصمت** على مضيف بلا python3 — أي أنّ إنذار
#      النسخ الاحتياطي كان يمرّ دائماً على خادم مُقتصَد، وهو المضيف الأرجح لمهمّة نسخ.
#
# ولا واحد منها كان سيُكتشف بتشغيل `dotnet test` محلياً مرّةً أخرى. ثلاثتها تحتاج Linux.
#
# **ما يُشغَّل هنا:** ما تُشغّله مهمّة `fast` في `.github/workflows/ci.yml` — البناء بالتحذيرات
# أخطاءً، والمجموعات الثلاث السريعة، وفحص «لم يتغيّر شيء» الذي يمسك جرداً مولَّداً منحرفاً.
# **وما لا يُشغَّل:** مجموعة التكامل (تحتاج Testcontainers داخل حاوية، أي Docker داخل Docker)،
# وفحوص سلسلة التوريد. هذه تبقى على الخطّ نفسه — وهذا مذكورٌ هنا لا مسكوتٌ عنه، لأنّ بوّابةً
# تُوهم بتغطية لا تملكها أسوأ من غياب البوّابة.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDK_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"
NODE_IMAGE="node:22"
NUGET_VOLUME="souq-ci-nuget"
RUN_FRONTEND=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --frontend) RUN_FRONTEND=1; shift;;
        -h|--help) sed -n '2,26p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0;;
        *) die "خيار غير معروف: $1";;
    esac
done

need docker
need git

# **الملفّات المتتبَّعة بمحتوى شجرة العمل الحالية** — أي ما سيراه الخطّ لو دُفع الآن بالضبط.
# نسخُ المجلّد كما هو سيأخذ معه bin وobj وnode_modules ومخرجات macOS، فيقيس شيئاً آخر.
EXPORT="$(mktemp -d)"
trap 'rm -rf "$EXPORT"' EXIT
step "تصدير الملفّات المتتبَّعة (ما يستنسخه الخطّ)"
(cd "$ROOT" && git ls-files -z | tar --null -T - -cf -) | (cd "$EXPORT" && tar -xf -)

# بصمة قبل التشغيل: ما يعيد توليده شيءٌ أثناء الاختبارات يظهر كفرق، وهو نفس ما يمسكه
# `git diff --exit-code` في الخطّ — جردٌ مولَّد نُسي تحديثه في نفس التغيير.
before="$(cd "$EXPORT" && find . -type f -exec shasum {} + | sort)"

docker volume create "$NUGET_VOLUME" >/dev/null

step "البناء والمجموعات السريعة على Linux ($SDK_IMAGE)"
FAILED=0
docker run --rm -v "$EXPORT:/src" -v "$NUGET_VOLUME:/root/.nuget/packages" -w /src "$SDK_IMAGE" sh -c '
    set -e
    dotnet restore
    dotnet build --no-restore -warnaserror
    dotnet test tests/Souq.Domain.Tests --no-build
    dotnet test tests/Souq.Application.Tests --no-build
    dotnet test tests/Souq.ArchitectureTests --no-build
' || FAILED=1

step "فحص أنّ شيئاً لم يُعَد توليده"
after="$(cd "$EXPORT" && find . -type f -exec shasum {} + | sort)"
if [[ "$before" != "$after" ]]; then
    # ملفّات البناء الجديدة متوقّعة؛ المهمّ ملفٌّ **متتبَّع** تغيّر محتواه.
    changed="$(diff <(printf '%s\n' "$before") <(printf '%s\n' "$after") \
        | grep -E '^[<>]' | grep -vE '/(bin|obj|node_modules)/' | sort -u -k2 || true)"
    if [[ -n "$changed" ]]; then
        warn "ملفّات متتبَّعة تغيّرت أثناء التشغيل — جردٌ مولَّد منحرف على الأرجح:"
        printf '%s\n' "$changed" >&2
        FAILED=1
    fi
fi

if (( RUN_FRONTEND )); then
    step "الواجهة على Linux ($NODE_IMAGE)"
    docker run --rm -v "$EXPORT:/src" -w /src/frontend "$NODE_IMAGE" sh -c '
        set -e
        npm ci
        npm run lint
        npm run typecheck
        npm test
        npm run build
    ' || FAILED=1
fi

if (( FAILED )); then
    die "بوّابة Linux فشلت — هذا ما سيحدث على الخطّ."
fi
ok "بوّابة Linux خضراء (مجموعة التكامل وفحوص سلسلة التوريد تبقى على الخطّ نفسه)."
