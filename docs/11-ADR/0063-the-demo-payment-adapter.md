# ADR-0063: The demo payment adapter — deterministic, local, and never pretending

- **Status:** Accepted 2026-09-22, implemented the same day. Answers `C-01`'s provider question in **portfolio mode**; does not supersede [ADR-0048](0048-payment-provider-abstraction.md), which it fills in.
- **Date:** 2026-09-22
- **Related modules:** Payments, Ordering
- **Related ADRs:** [ADR-0048](0048-payment-provider-abstraction.md) (the flow-agnostic port this sits behind), [ADR-0036](0036-payment-intent-state-machine.md) (the intent states it must produce honestly), [ADR-0031](0031-payments-and-refunds.md) (the router and per-store accounts), [ADR-0061](0061-a-payment-records-which-account-took-it.md) (the account identity it records)

## Context

Souq is being finished as a portfolio demonstration of a commercial SaaS architecture rather than launched as a business. No payment provider will be contracted, no merchant account opened, no money moved.

The repository already had a gateway called `FakeGateway`: every payment succeeded, and the checkout screen fell back to it **silently**, with no indication to the shopper that nothing was being charged. That is defensible in a developer's sandbox and indefensible in something shown publicly — a payment screen that does not say it is a demo is implicitly claiming it is not one.

## Problem

1. How does a portfolio build demonstrate a payment system without a provider, without faking one?
2. How does a demo show anything other than the happy path, repeatably?
3. How does a reviewer see where a real provider would be inserted?

## Options considered

### A — Leave the always-succeeds fake

**Rejected.** It demonstrates one path out of five. A reviewer learns nothing about how the system handles a decline, a pending authorisation, or a cancellation — which is where the actual engineering lives.

### B — Random outcomes

**Rejected.** A demo that behaves differently each time cannot be walked through, and its tests cannot be written. Randomness looks like realism and buys neither.

### C — Sign up for a provider's sandbox

**Rejected, and it is the option that looks most "real".** It needs an account, it ties a public repository to someone's credentials, its behaviour changes when the provider changes, and it stops working when the key is rotated or the account lapses. A portfolio artefact should not rot because a third party expired it.

### D — A deterministic local adapter, announced as one

**Chosen.**

## Decision

### The outcome is a function of the amount

The last two minor units of the order total select the path, **in the currency's own minor units** — `…01` declines, `…02` stays processing, `…03` is cancelled at the gateway, anything else succeeds. This mirrors how real providers' test modes use magic amounts, and it means a reviewer can demonstrate every branch by changing a quantity.

The chosen outcome is stamped into the intent id at creation and read back at confirmation, which makes `ConfirmAsync` a **pure function**: no stored result, no ledger to consult, and therefore idempotent by construction. That matters beyond neatness — `OrderPaymentConfirmation` has a customer-versus-webhook race whose correctness depends on confirming twice giving the same answer.

The currency detail is not incidental. The first implementation multiplied by 100 and was wrong for JOD, which has three decimals: `10.001` did not end in "01" by that arithmetic. Its own test caught it.

### A decline is *retryable*, not failed

`…01` returns `Retryable`, so the order stays `Pending` and the shopper can try again on the same intent — the behaviour [ADR-0036](0036-payment-intent-state-machine.md) exists to protect. A demo that cancelled the order on a decline would be demonstrating the bug that ADR fixed.

### Cancellation always succeeds here, and that is a statement about *this* adapter

There is no provider session to ask, and "payment" is a button in the browser, so an unconfirmed intent holds nothing. A real adapter **must not** copy this: it has to ask, because the session may have succeeded between the request and the cancel, and claiming otherwise leaves money captured against a cancelled order. The port returns a *state* rather than `void` precisely so an adapter can say what it found.

### The screen says what it is

The checkout panel carries a warning-toned notice: demo payment, no card collected, no provider contacted, nothing charged — and the sentence explaining how to drive the other outcomes. The silent fallback is gone.

### It is named for what it is

`DemoPaymentGateway`, `PaymentProvider.Demo`, `Payments:Demo:WebhookSecret`, and `demo` recorded on the payment. `Fake` is still accepted as a provider value so an older configuration does not fail to boot. Rows written when the recorded name was `fake` keep working untouched: the router sends anything that is not a store account to the deployment account, so no migration and no orphan value.

### What it does not do

No card number is accepted, parsed, or stored — there is nothing to accept it with. No credential is held. No network call is made. The publishable key is `null`, which is exactly how the frontend already decides to show the demo panel instead of a card form.

## Consequences

**Good**

- Every branch of the payment state machine is demonstrable and repeatable, including the ones that are hard to trigger against a real provider.
- The seam is visible: one class implements `IPaymentGateway`, and a real provider is a second one.
- Nothing rots. No account, no key, no contract.
- Refund idempotency is genuinely exercised — the ledger returns the same refund for the same key, as a real provider would.

**Costs, honestly**

- **This proves the architecture, not the integration.** Nobody should read a green test here as evidence that a real provider would work; the interesting failures of real providers are exactly the ones a local adapter cannot have.
- Tying outcomes to the amount is a convention a reader must be told about. It is written on the checkout screen, in this record, and in the README.
- `CancelIntentAsync` returning `Cancelled` unconditionally is correct here and wrong for a real adapter, which is a trap if someone copies this class as a starting point. Hence the comment saying so, in the file.

## Deliberately out of scope

- **Any real provider adapter.** Choosing one is a commercial decision with a contract attached, and the repository is explicit that none has been made.
- **Card data of any kind**, which [ADR-0031](0031-payments-and-refunds.md) already forbids and a test enforces.
- **The attempt aggregate, event-log totals and the webhook inbox**, which [ADR-0048](0048-payment-provider-abstraction.md)'s implementation notes defer to the first redirect-first adapter. The demo adapter produces `ClientScript`, so the shopper never leaves the process and none of the three has anything to do.
