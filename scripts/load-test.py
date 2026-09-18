#!/usr/bin/env python3
"""Load test for a running Souq stack — the harness ScalingStrategy.md §0 names as missing.

WHY THIS AND NOT k6 / JMeter / Gatling (M16 tool decision)
---------------------------------------------------------
The question this phase asks is "where is the bottleneck, and does a change help?", not "how many
users can we serve". That question needs a few hundred requests against a handful of routes with
honest percentiles — not a scripting runtime, a browser recorder, or a distributed runner.

The requirement that decided it: **anyone who clones this repository must be able to run it.**
A tool that has to be installed first is a tool that is not run, and a performance number nobody
can reproduce is not evidence. This is Python standard library only — no pip install, no binary,
no lock file, nothing added to CI's dependency surface. `ab` was available on the machine this was
written on and is not in the repository's control; k6 is the better instrument for a real capacity
exercise and is the right thing to adopt *if and when* Stage 2 of ScalingStrategy.md is reached.

READ THE RATIO, NOT THE MILLISECONDS
------------------------------------
The same caveat `BestSellingPerformanceTests` records applies here: the pinned SQL Server image is
amd64 and is emulated on an arm64 machine, and the compose stack is memory-capped. Absolute figures
are therefore pessimistic and vary between runs. Comparisons *within one run* — this route against
that one, before a change against after — are the trustworthy part.

USAGE
-----
    python3 scripts/load-test.py --base-url http://localhost:8091 \
        --admin-email <email> --admin-password <password> \
        --concurrency 16 --requests 200

Exits non-zero if any scenario misses its p95 target, so it can gate a pipeline later.
"""

import argparse
import json
import statistics
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field


# ─────────────────────────────────────────────────────────────────────────────
# Targets. Internal engineering targets set in M16 from the shape of the system,
# not a commercial commitment — the roadmap's own example ("p95 catalogue latency
# under 300 ms") is the anchor, and the rest follow from what each route does.
#
# The admin dashboard is allowed an order of magnitude more than the storefront on
# purpose: it runs sixteen independent aggregates over the store's whole history,
# and a merchant opens it a few times a day, while every visitor hits the catalogue.
# ─────────────────────────────────────────────────────────────────────────────
@dataclass
class Scenario:
    name: str
    path: str
    p95_target_ms: float
    authenticated: bool = False
    samples: list = field(default_factory=list)
    errors: int = 0
    statuses: dict = field(default_factory=dict)


def scenarios():
    return [
        Scenario("storefront catalogue", "/api/products?pageSize=20", 300),
        Scenario("storefront search", "/api/products?keyword=%D9%85%D9%83%D9%86%D8%B3%D8%A9&pageSize=20", 500),
        Scenario("search suggestions", "/api/products/suggestions?q=%D9%85%D9%83", 200),
        Scenario("storefront config", "/api/storefront/config", 150),
        Scenario("categories", "/api/categories", 200),
        Scenario("admin product list", "/api/admin/products?pageSize=20", 500, authenticated=True),
        Scenario("admin inventory", "/api/admin/inventory?pageSize=20", 500, authenticated=True),
        Scenario("admin dashboard", "/api/admin/reports/dashboard", 3000, authenticated=True),
        Scenario("search insights", "/api/admin/search-synonyms/insights", 800, authenticated=True),
    ]


def sign_in(base_url, email, password):
    request = urllib.request.Request(f"{base_url}/api/auth/login", method="POST")
    request.add_header("Content-Type", "application/json")
    request.data = json.dumps({"email": email, "password": password}).encode()
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read().decode())["accessToken"]


def one_request(base_url, scenario, token):
    request = urllib.request.Request(base_url + scenario.path)
    if scenario.authenticated and token:
        request.add_header("Authorization", f"Bearer {token}")
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            response.read()
            status = response.status
    except urllib.error.HTTPError as error:
        status = error.code
    except Exception:
        status = 0
    elapsed_ms = (time.perf_counter() - started) * 1000
    return status, elapsed_ms


# One warm-up request is not enough and measuring that was the first thing this harness got wrong.
# On an emulated, memory-capped stack the first several requests to a route pay for JIT, the
# connection pool, EF's model and plan caches and SQL Server's own plan compilation — enough that a
# route whose steady state is 12 ms reported a p95 of 735 ms, which reads as a finding and is not one.
WARMUP_REQUESTS = 10


def run(scenario, base_url, token, concurrency, count):
    for _ in range(WARMUP_REQUESTS):
        one_request(base_url, scenario, token)

    with ThreadPoolExecutor(max_workers=concurrency) as pool:
        for status, elapsed in pool.map(
            lambda _: one_request(base_url, scenario, token), range(count)
        ):
            scenario.statuses[status] = scenario.statuses.get(status, 0) + 1
            if 200 <= status < 300:
                scenario.samples.append(elapsed)
            else:
                scenario.errors += 1
    return scenario


def percentile(values, fraction):
    if not values:
        return float("nan")
    ordered = sorted(values)
    index = min(len(ordered) - 1, int(round(fraction * (len(ordered) - 1))))
    return ordered[index]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base-url", default="http://localhost:8091")
    parser.add_argument("--admin-email")
    parser.add_argument("--admin-password")
    parser.add_argument("--concurrency", type=int, default=16)
    parser.add_argument("--requests", type=int, default=200)
    args = parser.parse_args()

    token = None
    if args.admin_email and args.admin_password:
        try:
            token = sign_in(args.base_url, args.admin_email, args.admin_password)
        except Exception as error:   # noqa: BLE001 — the report says so rather than dying
            print(f"!! could not sign in ({error}); authenticated scenarios will be skipped\n")

    print(f"Souq load test — {args.base_url}")
    print(f"concurrency {args.concurrency}, {args.requests} requests per scenario "
          f"after {WARMUP_REQUESTS} warm-up requests\n")
    print(f"{'scenario':<24}{'p50':>9}{'p95':>9}{'p99':>9}{'max':>9}{'rps':>9}{'errors':>9}   target")
    print("-" * 96)

    missed = []
    for scenario in scenarios():
        if scenario.authenticated and not token:
            continue
        started = time.perf_counter()
        run(scenario, args.base_url, token, args.concurrency, args.requests)
        wall = time.perf_counter() - started

        p50 = statistics.median(scenario.samples) if scenario.samples else float("nan")
        p95 = percentile(scenario.samples, 0.95)
        p99 = percentile(scenario.samples, 0.99)
        worst = max(scenario.samples) if scenario.samples else float("nan")
        rps = len(scenario.samples) / wall if wall else 0
        verdict = "ok" if p95 <= scenario.p95_target_ms else "MISSED"
        if verdict == "MISSED":
            missed.append((scenario.name, p95, scenario.p95_target_ms))

        print(f"{scenario.name:<24}{p50:>8.0f}m{p95:>8.0f}m{p99:>8.0f}m{worst:>8.0f}m"
              f"{rps:>9.0f}{scenario.errors:>9}   p95<{scenario.p95_target_ms:.0f}ms {verdict}")
        if scenario.errors:
            print(f"{'':<24}statuses: {scenario.statuses}")

    print()
    if missed:
        print("Missed targets:")
        for name, actual, target in missed:
            print(f"  {name}: p95 {actual:.0f} ms against a target of {target:.0f} ms")
        return 1

    print("All scenarios met their p95 targets.")
    print("Absolute figures on an emulated, memory-capped stack are pessimistic — compare within a run.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
