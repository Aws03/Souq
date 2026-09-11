# ADR-0014: Money representation and precision

- **Status:** Accepted and implemented in Phase 1A, 2026-09-11
- **Date:** 2026-09-11
- **Related modules:** Cross-cutting (every monetary amount); Payments (conversion for the provider)
- **Related ADRs:** builds on [ADR-0007](0007-database-strategy.md); its default currency is removed by [ADR-0022](0022-tenancy-enforcement.md); applied by [ADR-0025](0025-catalog-model.md), [ADR-0028](0028-basket-and-pricing-pipeline.md), [ADR-0030](0030-coupon-redemptions.md) and [ADR-0032](0032-shipping-methods.md); the provider multiplier (P-05) is carried unchanged by [ADR-0031](0031-payments-and-refunds.md)

## Context

Phase 0 finding C5:
- Money columns are `decimal(18,2)`, but the Jordanian dinar (JOD) has **3** minor units (fils). The UI accepts `0.001` steps and prints 3 decimals.
- SQL Server silently rounded `12.345` to `12.35`.
- A 15% coupon produced `1.85175` in memory (sent to the payment provider) but `1.85` in the database (shown on the order). The customer-visible total and the charged total could therefore diverge.

## Problem

How should money be represented so that stored, displayed, and charged amounts always agree, for JOD and for future tenant currencies?

## Options considered

- **Storage:** `decimal(18,3)` (JOD only) · **`decimal(19,4)`** (every ISO-4217 exponent) · the SQL `money` type (implicit rounding, discouraged) · integer minor units (`long`, precise but awkward everywhere).
- **Rounding:** at display only · at persistence · **in the Domain, by construction**.
- **Mode:** banker's (`ToEven`) · **commercial (`AwayFromZero`)**.

## Decision

1. **Storage:** `decimal(19,4)` for every monetary column.
2. **The `Money` invariant:** an amount must be representable in its currency's minor units.
   - JOD, KWD, BHD, OMR, TND, LYD, IQD have 3.
   - JPY, KRW, VND, CLP, ISK, UGX have 0.
   - Everything else has 2.

   `new Money(12.3456m, "JOD")` throws `InvalidMoneyException` (a `DomainException`, so the API returns 400, not 500).
3. **Calculated amounts** (percentages, and tax later) use `Money.FromCalculation(amount, currency)`, which rounds to minor units with `MidpointRounding.AwayFromZero`: shoppers and accountants expect 0.0005 → 0.001.
4. The currency code is normalized to upper case and must be 3 letters.
5. **Payment provider conversion** is done by the adapter.
   - Stripe documents non-listed currencies as two-decimal, so JOD is sent ×100 rounded away from zero. This is the pre-existing behaviour, now explicit and tested.
   - ⚠️ **Must be verified with Stripe before JOD goes live**: if the account treats JOD as three-decimal, the multiplier becomes 1000.

## Why

- Enforcing precision in the value object makes the bug class impossible instead of relying on every caller to remember to round.
- `decimal(19,4)` gives headroom for every currency without future column changes.

## Consequences

- Existing prices with more than 3 decimals would fail to load as JOD; the seed and dev data have none.
- The default currency is still `JOD`. It is removed when tenants carry their own currency (Phase 2).
- Order snapshot totals arrive in Phase 9.

## Revisit when

- A tenant needs a currency with more than 4 decimals (practically none).
- Tax rules require line-level rounding strategies (a pricing module decision, Phase 8).
