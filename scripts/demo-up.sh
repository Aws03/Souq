#!/usr/bin/env bash
# ============================================================================
# حزمة عرضٍ محلية تعمل من نسخةٍ نظيفة، بحسابٍ يُعرف اسمه وكلمته.
#
# لماذا هذا النصّ موجود أصلاً؟
# `docker compose up` وحده لا يكفي: الحزمة تعمل بوسائط Production، والبذر خارج التطوير
# **يرفض** إنشاء أي مدير ما لم تُضبط Seed:AdminEmail/Password صراحةً (DbSeeder، فحص B1).
# و`.env.example` يتركهما فارغين عمداً — فلا كلمة مرور منشورة في مستودع. النتيجة الصحيحة
# أمنياً كانت نتيجةً سيّئة عملياً: حزمة تُقلع ولا يستطيع أحد الدخول إليها، فصار كل من يريد
# عرضاً يخترع ملف بيئةٍ خاصّاً به خارج المستودع — وهو ما لا يُستعاد ولا يُراجَع.
#
# هذا النصّ يحسم الاثنين معاً: **الأسرار تُولَّد هنا ولا تُرفع أبداً** (كلمة مرور SQL،
# مفتاح JWT — إلى `.env.demo` وهو في .gitignore)، و**حساب العرض موثَّق في المستودع**
# لأنّه ليس سرّاً: حزمةٌ بوّابةُ دفعها وهمية وبريدها سجلّ فقط، على 127.0.0.1.
#
# ما لا يفعله: لا يلمس `.env` (ملف المالك)، ولا يُستعمل لنشرٍ حقيقي. أي حزمة يخدمها
# زبائن تُعطى أسرارها من خارجها — docs/09-OPERATIONS/Configuration.md.
# ============================================================================
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env.demo"

# ── حساب العرض: موثَّق عمداً، وطويلٌ بما يكفي لتجاوز حارس البذر (12 حرفاً فأكثر،
#    ولا يساوي كلمة مرور التطوير `Admin@123` التي يرفضها DbSeeder خارج التطوير). ──
DEMO_ADMIN_EMAIL="admin@souq.com"
DEMO_ADMIN_PASSWORD="Admin@123456"
DEMO_OWNER_EMAIL="owner@souq.com"
DEMO_OWNER_PASSWORD="Owner@123456"

RECREATE=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --recreate) RECREATE=1; shift;;
        -h|--help) sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0;;
        *) die "خيار غير معروف: $1";;
    esac
done

need docker
need openssl

# الأسرار تُولَّد مرّة وتبقى: إعادة توليد كلمة مرور sa على حجمٍ قائم تعني قاعدة لا تُفتح.
if [[ -f "$ENV_FILE" ]]; then
    step "ملف العرض موجود — تُعاد قيمه كما هي: $ENV_FILE"
else
    step "توليد أسرار محلية (لا تُرفع — .env.demo في .gitignore)"
    umask 077
    cat > "$ENV_FILE" <<EOF
# وُلِّد بـ scripts/demo-up.sh — محلي، لا يُرفع، لا يُستعمل لنشرٍ حقيقي.
DB_SA_PASSWORD=$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 20)aA1!
JWT_KEY=$(openssl rand -base64 48)
PAYMENTS_PROVIDER=Fake
EMAIL_PROVIDER=Log
SEED_DEMO_DATA=true
SEED_ADMIN_EMAIL=$DEMO_ADMIN_EMAIL
SEED_ADMIN_PASSWORD=$DEMO_ADMIN_PASSWORD
SEED_PLATFORM_OWNER_EMAIL=$DEMO_OWNER_EMAIL
SEED_PLATFORM_OWNER_PASSWORD=$DEMO_OWNER_PASSWORD
DEFAULT_TENANT_HOSTS=localhost
PLATFORM_HOST=admin.localhost
FRONTEND_URL=http://localhost:8081
DATABASE_MIGRATE_ON_STARTUP=true
EOF
    ok "كُتب $ENV_FILE"
fi

step "بناء الصور وإقلاع الحزمة"
compose() { docker compose -p souq-demo --env-file "$ENV_FILE" -f "$ROOT/docker-compose.yml" "$@"; }
compose build
if [[ $RECREATE -eq 1 ]]; then compose up -d --force-recreate; else compose up -d; fi

# الجاهزية لا الإقلاع: الـ API يُرحّل ويبذر قبل أن يخدم طلباً، وأوّل إقلاع على مضيفٍ
# يُحاكي x86 يطول (Troubleshooting §21). ننتظره بدل أن نعلن نجاحاً مبكّراً.
step "انتظار /health/ready"
for _ in $(seq 1 60); do
    if [[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5201/health/ready || true)" == "200" ]]; then
        ok "الـ API جاهز"; break
    fi
    sleep 5
done

printf '\n%s== حزمة العرض تعمل ==%s\n' "$BOLD" "$OFF" >&2
cat >&2 <<EOF

  المتجر        http://localhost:8081
  لوحة المنصّة   http://admin.localhost:8081
  الـ API        http://localhost:5201

  مدير المتجر    $DEMO_ADMIN_EMAIL / $DEMO_ADMIN_PASSWORD
  مالك المنصّة    $DEMO_OWNER_EMAIL / $DEMO_OWNER_PASSWORD

  حسابا عرضٍ موثَّقان في المستودع عمداً — لا أسرار إنتاج. هذه الحزمة بوّابة دفعها وهمية
  (كل دفع "ينجح" بلا مال) وبريدها سجلّ فقط (لا تصل أي رسالة). لا زبائن حقيقيون عليها.

  الإيقاف مع حفظ البيانات:  docker compose -p souq-demo --env-file .env.demo down
  الإيقاف مع حذف البيانات:  docker compose -p souq-demo --env-file .env.demo down -v
EOF
