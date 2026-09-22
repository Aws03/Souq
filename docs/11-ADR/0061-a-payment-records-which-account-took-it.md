# ADR-0061: A payment records *which* account took it, and refuses to be undone through another

- **Status:** Accepted 2026-09-22, implemented the same day, closing `TD-50`. Refines [ADR-0031](0031-payments-and-refunds.md), which it does not supersede.
- **Date:** 2026-09-22
- **Related modules:** Payments, Ordering
- **Related ADRs:** [ADR-0031](0031-payments-and-refunds.md) (the router and per-store accounts this corrects), [ADR-0048](0048-payment-provider-abstraction.md) (names `TD-50` a prerequisite for the re-shaped port), [ADR-0013](0013-optimistic-concurrency.md) (the payment row's token)

## Context

`Payment.Gateway` records the **kind** of account that created the intent — `stripe:store`, `stripe:deployment`, `fake` — and the router uses that kind to pick the account for every later call: confirm, cancel, refund.

The kind is not an identity. `stripe:store` means "the store's account", and *which* account that is resolves at call time from whatever keys the store has connected **now**.

So this happens, through ordinary onboarding rather than anything exotic:

1. A merchant connects test keys and takes a payment.
2. They switch to live keys — a transition `StorePaymentAccount` explicitly supports, and the expected path from trial to selling.
3. A refund on step 1's payment is sent to the live account, which has never heard of that intent.

The provider answers 404, the refund is marked `Failed`, and **a failed refund cannot be retried**. The customer's money then has no path back inside the application.

Three places in the repository claimed the opposite until `M6` corrected them. `M6` deliberately did not fix the behaviour inside an audit phase and filed `TD-50` instead, noting it needs a record of its own because it changes what happens to money.

## Problem

1. How does a refund reach the account that actually took the payment?
2. What happens to the payments already in every database, which carry no identity at all?
3. What identity is safe to store, given that payment tables are pinned by a test precisely so nothing sensitive drifts into them?

## Options considered

### A — Refuse to let a store replace its keys once it has taken a payment

**Rejected.** It makes the defect impossible by making the product unusable: a merchant could never move from test to live, and could never rotate a leaked key. Fixing a data problem by forbidding a legitimate operation is the worst trade on the list.

### B — Keep sending the refund and handle the failure better

**Rejected.** The refund still fails; the improvement is only in how the failure reads. It also leaves the money stranded, because a failed refund cannot be retried, and it spends a provider call to learn something we already know locally.

### C — Store the account's identity on the payment and compare before calling

**Chosen.** The comparison is local, happens before any network call, and can say something an operator can act on.

### D — Store a hash of the identity rather than the identity

**Rejected, after taking it seriously** — it is what `TD-50` offered as an alternative. A hash gives the comparison but takes away the operator message: "this payment was taken by an account you no longer have connected" is useful; "…by an account whose hash is 4f2a…" is not. And the thing being hashed is the **publishable key**, which the provider ships to every browser that loads the checkout page. Hashing public data to protect it is a ritual, not a control.

## Decision

### The payment records the publishable key of the account that took it

`Payment.GatewayAccount`, nullable, `varchar(255)`. It is set from `IPaymentGateway.PublishableKey` when the intent is created, carried on `PaymentIntentResult`, and written in the same call that already recorded the kind.

The publishable key is public by construction, which is exactly why it is admissible in a table whose column list is pinned by `PaymentDataRulesTests`. That test failed when the column arrived — as designed — and the column was added to the reviewed list with the reason written beside it.

### The router compares identity before it calls, and refuses on a mismatch

`ForIntentAsync` resolves the account by kind as before, then compares the recorded identity with the resolved account's publishable key. Different ⇒ `PaymentGatewayUnavailableException` (503) with a message that tells the operator what to do — reconnect the account that took it — rather than reporting what went wrong.

Refusing before the call is better than failing after it in the way that matters: the refusal is a **state the operator can repair**, and the failure is a dead record. The mismatch is also logged with both identities, because an operator answering a customer needs to know which account to reconnect.

### An unrecorded identity passes

`null` means "no identity recorded", and those calls behave exactly as they did before: every payment already in every database, and every payment taken by the fake gateway, which has no publishable key.

This is the only migration-safe rule, and it is worth being explicit about the trade rather than quiet: **a guard that refused the unknown would have blocked refunds on every existing payment on the day of the upgrade.** The old defect therefore remains reachable for payments taken before this change — and only for those. It closes for everything taken after it, with no backfill, because there is nothing to backfill from: the identity was never recorded.

### No migration risk

The column is nullable and additive; nothing is rewritten and nothing is read that did not exist. The migration was regenerated from a fresh build after the first attempt produced `nvarchar(max)` from a stale assembly.

## Consequences

**Good**

- The reachable path from "switch test keys to live" to "customer's money is stuck" is closed.
- The failure is now local, immediate and legible, instead of a provider 404 recorded as a dead refund.
- It satisfies the prerequisite [ADR-0048](0048-payment-provider-abstraction.md) names, so the re-shaped port inherits identity rather than retrofitting it.

**Costs, honestly**

- Payments taken before this change are still exposed. Nothing can fix that; the data does not exist.
- The identity is the publishable key, so a store that reconnects the *same* account with the *same* keys matches, and one that rotates to new keys on the same provider account does not. That is stricter than necessary in that one case, and it fails in the safe direction: a refusal an operator can resolve, rather than a call that silently lands somewhere wrong.
- One more column on a table whose column list is deliberately pinned, which means one more thing a reviewer must agree to.

## Deliberately out of scope

- **Backfilling identities** onto existing payments. There is no source for them.
- **Making a failed refund retryable.** A separate defect with its own trade-offs; this record removes one of its causes rather than its consequence.
- **A concurrency token on `StorePaymentAccounts`**, which `C12` still owes: two admins editing keys at once remain last-write-wins.
