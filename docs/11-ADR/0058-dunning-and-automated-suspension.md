# ADR-0058: Dunning and automated suspension — a pure ladder, off by default, and two conditions before a store closes

- **Status:** Accepted 2026-09-22, and **implemented by `C6`** on the same day. Supersedes nothing.
- **Date:** 2026-09-22
- **Related modules:** Billing, Platform, Notifications, Identity
- **Related ADRs:** [ADR-0056](0056-platform-invoices-and-manual-collection.md) (the invoice this ladder acts on, which named dunning as out of its own scope), [ADR-0057](0057-cross-instance-coordination.md) (the lease this sweep runs under — the thing `C6` was waiting for), [ADR-0034](0034-notifications-outbox.md) (why the reminder goes to an outbox and not to a provider), [ADR-0021](0021-transaction-boundaries.md) (why revocation happens after the commit, not inside it), [ADR-0017](0017-error-contract.md) (why a decision carries a named reason)

## Context

`C5` gave Souq an invoice that can fall due and stay unpaid. It gave the platform no answer to what happens next, and said so: collection is a human act, and dunning is `C6`.

The owner's answer to `C-17` already settled what suspension *means* — a suspended store is admin-only, its storefront closed, its paid orders still trackable — and `C3` implemented it as an operator's button. What is new here is the absence of the operator.

This is worth stating without softening, because it decided nearly every detail below: **this is the first capability in the product that takes an irreversible-feeling action against a paying customer with no person in the loop.** Not a warning, not a flag on a dashboard — the storefront closes and shoppers see it. Everything in this record is shaped by the assumption that it will one day fire on the wrong store, and that the way to survive that is to make it slow, loud, reversible, and readable.

## Problem

1. When exactly does an unpaid invoice close a store?
2. Where does that rule live, so that it can be reviewed by someone who is not reading a background service?
3. What stops two API instances from suspending the same store twice, or from sending the same merchant two reminders?
4. What stops a platform that was offline for three days from sending three days of missed reminders at once?

## Options considered

### A — Suspend when the grace period passes

**Rejected.** It closes a store that was never told. A merchant whose invoice email went to a spam folder learns about a debt from a shopper's phone call, and the platform has no answer for why it never wrote. Grace is a measure of *time*, and time alone does not establish that anyone was informed.

### B — Suspend after N reminders

**Rejected, and it is the subtler mistake.** Reminders are countable, so the rule reads well — three reminders and the store closes. But the interval is configuration, and configuration drifts: an operator who sets the reminder interval to one day to chase a single account has, without noticing, shortened every store's runway to three days regardless of the grace period they thought they had set. A rule that can be tightened by changing an unrelated field is not a rule anyone can rely on.

### C — Both conditions, together

**Chosen.** A store is suspended only once **the grace period has passed** *and* **every configured reminder has been sent**. Each condition covers the other's failure: grace alone would suspend the uninformed, reminders alone would suspend the recently invoiced. Suspension means the merchant was both *given time* and *told*.

Setting `MaxRemindersBeforeSuspension` to zero deliberately collapses this to option A, for a platform that genuinely wants it. That is a value an operator types, not a rule engineering chose for them.

### D — Suspend, and let an operator un-suspend

This is not an alternative but a property the chosen option keeps: suspension is a **state**, restored by the button `C3` already ships. No deletion, no archival, no data moved. The worst outcome of a wrong suspension is an angry afternoon, not a lost store.

## Decision

### The rule is a pure function

`DunningPolicy.Decide(invoice, settings, utcNow)` reads no database, no clock, and sends nothing. It returns a `DunningDecision` — an action and a **named reason** — and the background service executes that answer without adding a single condition of its own.

This is the whole reason the most dangerous logic in the product can be reviewed in one screen and tested as a state table with no server and no database. `DunningPolicyTests` covers twelve cases; changing the `&&` in the escalation rule to `||` fails four of them.

The ladder, in order:

| # | Condition | Result |
|---|---|---|
| 1 | Dunning disabled | nothing (`DunningDisabled`) |
| 2 | Invoice not overdue | nothing (`NotOverdue`) |
| 3 | Already escalated | nothing (`AlreadyEscalated`) |
| 4 | Grace passed **and** reminders exhausted | **suspend** (`GraceExhausted`) |
| 5 | Reminders exhausted, still within grace | nothing (`WithinGracePeriod`) |
| 6 | A reminder is due | **remind** (`ReminderDue`) |
| 7 | otherwise | nothing (`ReminderNotDue`) |

The *ordering* is itself a decision: suspension is checked before reminding, so a store past every deadline is not given one more reminder before closing — a ladder whose last rung sends another reminder never ends.

Every branch names its reason even when it does nothing, for the reason [ADR-0017](0017-error-contract.md) gives about errors: an operator reading "no action" with no reason cannot tell disabled dunning from an invoice comfortably inside its terms.

### It is off until a human turns it on

`DunningEnabled` defaults to `false` and lives in `PlatformBillingSettings`. Nothing in provisioning, seeding or migration turns it on. The check is in the policy — not also in the service — so there is exactly one place that decides whether the platform chases anyone.

The domain refuses to enable it with a zero-day grace period, because a zero grace period means suspension the day after an invoice falls due, and nobody means that. The platform settings screen disables the toggle in the same case, so the refusal is read at the switch rather than after a save.

The screen also carries the warning in full, next to the control. A caution that lives in a document is read by people who went looking for it.

### The reminder interval is measured from the last reminder

Not from the due date. A platform whose sweep was down for two days comes back and sends **one** reminder, not the two it missed. Measuring from the due date would turn an outage into a burst of mail to customers who are already unhappy.

### Suspension and its mark are one transaction

`store.Suspend()` and `invoice.RecordEscalation(now)` commit together. A suspension without the mark is reconsidered every hour; a mark without the suspension leaves a running store that the ladder believes is closed. `RecordEscalation` throws if the invoice was already escalated, so the aggregate refuses the second attempt regardless of what any caller believes.

Session revocation and tenant-directory invalidation happen **after** the commit, because both are work outside the database ([ADR-0021](0021-transaction-boundaries.md)). They are what makes the suspension real rather than a value in a row: the merchant's open sessions stop, and every instance drops its cached snapshot of the store through `C4`'s shared generation.

### It runs under a lease, and reminders go through the outbox

The sweep holds `GuardedWork.Dunning` ([ADR-0057](0057-cross-instance-coordination.md)). The suspension itself is idempotent by the escalation mark, but the *noise* is not: two instances would write two audit entries and mail the merchant twice. A batch of 200 bounds a platform left unattended for months; the ladder is measured in days, so a deferred batch changes nothing.

The reminder is enqueued as an outbox message inside the same save that records it on the invoice — no reminder sent unrecorded, none recorded unsent — and the message is built with **the invoice's tenant taken explicitly**, because the sweep runs in platform scope where the ambient tenant is deliberately absent.

The reminder tells the merchant what is owed and how to pay it by the instructions on the invoice. It carries **no payment link**, because there is no payment provider in this path and inventing one in an email is how a product acquires an integration nobody decided on.

## Consequences

**Good**

- The rule that closes stores is a pure function with a state-table test suite, readable by someone who has never opened the background service.
- Off by default, and enabling it is an operator's deliberate act with the warning at the switch.
- Suspension is reversible by the button that already existed.
- An outage delays the ladder; it never compresses it.

**Costs, honestly**

- Two conditions mean a merchant who ignores every reminder keeps their store open until *both* are exhausted. That is the intended trade: the platform would rather be slow to close than wrong to close.
- Reminder volume is bounded by `MaxRemindersBeforeSuspension`, so a platform that sets it high and its interval low can still be noisy. The values are validated for range, not for taste.
- The sweep is coarse — hourly by default. An invoice becomes overdue at a moment; it is acted on within the hour. Nothing here needs finer resolution, and a finer one would only make the lease contend.

## Deliberately out of scope

- **Automated collection.** Nothing here charges anyone. `C-01`'s redirect-first model has no provider chosen, and dunning that could charge a card would be a different decision with a different record.
- **Partial-payment behaviour.** An invoice with any outstanding balance is overdue or it is not; the ladder does not reward a partial payment with extra time. If that turns out to be wrong, it is a change to the policy function and its table, and nothing else.
- **Per-store dunning configuration.** One ladder for the whole platform. A per-store override would be a tenant fork of a platform rule, and that is exactly what this architecture refuses.
