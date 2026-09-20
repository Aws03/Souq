#!/usr/bin/env python3
"""Provision the "second store" QA fixture that `frontend/e2e/second-tenant.spec.js` needs.

WHY THIS EXISTS (M19)
---------------------
DeveloperQualityGates.md described this fixture and then said: "Nothing in the repository
creates it — provision it once through the platform console." A browser journey whose
prerequisite is a hand-made fixture is a journey **nobody runs**, and the full-suite run in
M19 proved it: all six of its tests failed on a machine where nobody had done the manual step.
This is that step, written down and repeatable.

WHAT IT CREATES, and why each part is needed by the spec
-------------------------------------------------------
  • a store named "Second Store", slug `second`, currency USD, default culture `en`
    (the spec asserts the identity, and that no trace of the first store's "Marka"/"JOD" leaks);
  • an administrator for it, invited and accepted through the real flow — no row is written
    behind the application's back, so what this produces is what the product produces;
  • three active products whose slugs begin `second-widget`, priced in USD.

It is **idempotent**: run it twice and the second run reports what already exists and stops.

REQUIREMENTS
------------
A Development API (invitation links are logged in Development only — ConsoleEmailService), and
its log, because the invitation token is read from it exactly as the journeys do:

    python3 scripts/qa-second-store.py \\
        --api http://127.0.0.1:5200 --api-log /path/to/api.log \\
        --owner-email <platform owner> --owner-password <password> \\
        --platform-host admin.localhost --store-host second.localhost

Only one API instance may be running against the database: the outbox is leased, so a second
instance can dispatch the invitation into *its* log instead of this one. (That is how it
behaved when both a container and a local API were up during M19 — the message was not lost,
it went to the other instance.)
"""

import argparse
import json
import re
import sys
import time
import urllib.error
import urllib.request

ADMIN = {"email": "second-admin@souq.test", "password": "SecondAdmin@12345", "name": "Second Store Admin"}
PRODUCTS = [
    ("second-widget-one", "Second Widget One", 19.99, 12),
    ("second-widget-two", "Second Widget Two", 24.50, 8),
    ("second-widget-three", "Second Widget Three", 31.00, 5),
]


def call(base, path, host, method="GET", body=None, token=None, expect=(200, 201, 204)):
    request = urllib.request.Request(f"{base}{path}", method=method)
    request.add_header("Host", host)              # المتجر يُحلّ من المضيف وحده (ADR-0006)
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    if body is not None:
        request.add_header("Content-Type", "application/json")
        request.data = json.dumps(body).encode()
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            raw = response.read().decode()
            if response.status not in expect:
                raise SystemExit(f"!! {method} {path} → {response.status}: {raw[:300]}")
            return json.loads(raw) if raw.strip() else None
    except urllib.error.HTTPError as error:
        raw = error.read().decode()
        if error.code in expect:
            return json.loads(raw) if raw.strip() else None
        raise SystemExit(f"!! {method} {path} → {error.code}: {raw[:300]}") from error


def sign_in(base, host, email, password):
    return call(base, "/api/auth/login", host, "POST", {"email": email, "password": password})["accessToken"]


def can_sign_in(base, host, email, password):
    try:
        sign_in(base, host, email, password)
        return True
    except SystemExit:
        return False


def invitation_token(log_path, store_host, since, timeout=60):
    """The token from the invitation link the API logged — the same source the journeys read."""
    pattern = re.compile(rf"https?://{re.escape(store_host)}[^\s\"]*accept-invitation[^\s\"]*")
    deadline = time.time() + timeout
    while time.time() < deadline:
        with open(log_path, encoding="utf-8", errors="replace") as handle:
            handle.seek(since)
            found = pattern.findall(handle.read())
        if found:
            link = found[-1].rstrip("\\\"',.")
            match = re.search(r"token=([^&\s\"]+)", link)
            if match:
                return match.group(1)
        time.sleep(1)
    raise SystemExit("!! no invitation link appeared in the API log. Is this a Development API, "
                     "and is it the only instance running against this database?")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--api", default="http://127.0.0.1:5200")
    parser.add_argument("--api-log", required=True)
    parser.add_argument("--owner-email", required=True)
    parser.add_argument("--owner-password", required=True)
    parser.add_argument("--platform-host", default="admin.localhost")
    parser.add_argument("--store-host", default="second.localhost")
    parser.add_argument("--time-zone", default="Asia/Amman")
    # ── متجرٌ بلا مديره ─────────────────────────────────────────────────────────
    # دعوة المدير وحدها تحتاج API في وضع Development (الرابط يُقرأ من السجلّ). أمّا
    # `cross-tenant-adversarial.spec.js` فلا يحتاج مدير المتجر الثاني أصلاً — يحتاج
    # **متجراً ثانياً نشطاً بمضيفه**، وذلك يُنشأ بتوكن مالك المنصّة في أيّ بيئة.
    # فبلا هذا الخيار كانت رحلةُ عزلٍ كاملة تسقط على حزمة Production لسببٍ لا يخصّها.
    parser.add_argument("--skip-admin", action="store_true",
                        help="أنشئ المتجر ونطاقه وفعّله فقط — بلا دعوة مدير ولا منتجات (يعمل في Production)")
    args = parser.parse_args()

    platform, store = args.platform_host, args.store_host

    print("→ signing in as the platform owner")
    owner = sign_in(args.api, platform, args.owner_email, args.owner_password)

    # **كل خطوة تُستأنف، لا تُعاد.** تشغيلٌ نصفُه نجح (متجر بلا نطاق مثلاً) يُكمَّل لا يُرفَض:
    # سكربت تجهيزٍ يشترط قاعدةً نظيفة هو سكربتٌ يُهجَر عند أوّل خطأ.
    existing = call(args.api, "/api/platform/tenants?pageSize=100", platform, token=owner)
    found = next((t for t in existing.get("items", []) if t.get("slug") == "second"), None)
    if found:
        tenant_id = found["id"]
        print(f"→ store 'second' already exists (id {tenant_id}, status {found.get('status')})")
    else:
        print("→ creating the store")
        created = call(args.api, "/api/platform/tenants", platform, "POST", token=owner, body={
            "name": "Second Store", "slug": "second", "currency": "USD",
            "defaultCulture": "en", "timeZone": args.time_zone,
        })
        tenant_id = created["id"] if isinstance(created, dict) else created
        print(f"   id {tenant_id}")

    # النطاق قبل الدعوة: الخادم يرفض دعوة مديرٍ لمتجرٍ بلا نطاق، لأنّ رابط الدعوة يُفتح على نطاق المتجر.
    print("→ ensuring its domain")
    call(args.api, f"/api/platform/tenants/{tenant_id}/domains", platform, "POST", token=owner,
         body={"host": store}, expect=(200, 201, 204, 409, 422))

    if args.skip_admin:
        print("→ activating the store")
        call(args.api, f"/api/platform/tenants/{tenant_id}/status", platform,
             "POST", token=owner, body={"action": "Activate"}, expect=(200, 204, 409, 422))
        print(f"\nDone (--skip-admin). A second active store is served on http://{store}")
        print("`cross-tenant-adversarial.spec.js` can run. `second-tenant.spec.js` also needs "
              "its administrator and products — rerun without --skip-admin against a Development API.")
        return 0

    if not can_sign_in(args.api, store, ADMIN["email"], ADMIN["password"]):
        with open(args.api_log, encoding="utf-8", errors="replace") as handle:
            handle.seek(0, 2)
            log_mark = handle.tell()

        print("→ inviting its administrator")
        call(args.api, f"/api/platform/tenants/{tenant_id}/admins", platform, "POST", token=owner,
             body={"fullName": ADMIN["name"], "email": ADMIN["email"]}, expect=(200, 201, 204, 409, 422))

        print("→ reading the invitation link from the API log")
        token = invitation_token(args.api_log, store, log_mark)

        print("→ accepting the invitation (the real flow, not a row written behind the app)")
        call(args.api, "/api/auth/reset-password", store, "POST",
             body={"token": token, "newPassword": ADMIN["password"]})
    else:
        print("→ its administrator can already sign in")

    print("→ activating the store")
    call(args.api, f"/api/platform/tenants/{tenant_id}/status", platform, "POST",
         token=owner, body={"action": "Activate"}, expect=(200, 204, 409, 422))

    print("→ signing in as the store administrator")
    admin = sign_in(args.api, store, ADMIN["email"], ADMIN["password"])

    categories = call(args.api, "/api/categories", store, token=admin)
    items = categories if isinstance(categories, list) else categories.get("items", [])
    if items:
        category_id = items[0]["id"]
    else:
        print("→ creating a category to hold them")
        created = call(args.api, "/api/categories", store, "POST", token=admin, body={
            "slug": "second-widgets", "parentId": None, "sortOrder": 1, "isActive": True,
            "translations": {"en": {"name": "Widgets"}, "ar": {"name": "أدوات"}},
        })
        category_id = created["id"] if isinstance(created, dict) else created

    listed = call(args.api, "/api/products?pageSize=100", store, token=admin)
    have = {p.get("slug") for p in listed.get("items", [])}

    print("→ creating the products that are missing")
    for slug, name, price, stock in PRODUCTS:
        if slug in have:
            print(f"   {slug} (already there)")
            continue
        call(args.api, "/api/products", store, "POST", token=admin, body={
            "categoryId": category_id, "price": price, "stockQuantity": stock,
            "slug": slug, "status": "Active",
            "translations": {"en": {"name": name, "description": f"{name} — a QA fixture product."},
                             "ar": {"name": name, "description": f"{name} — منتج تجربة."}},
        })
        print(f"   {slug}")

    print(f"\nDone. `second-tenant.spec.js` can now run against http://{store}:5173")
    print(f"Administrator: {ADMIN['email']} / {ADMIN['password']} (QA only — never a real deployment)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
