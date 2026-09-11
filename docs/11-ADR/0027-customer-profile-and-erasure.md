# ADR-0027: Customers: profile, address book, account status, and erasure

- **Status:** Accepted (implemented in Phase 7), 2026-09-11.
- **Builds on:** [ADR-0019](0019-authorization-foundation.md) (permissions), [ADR-0021](0021-transaction-boundaries.md) (transactions), [ADR-0022](0022-tenancy-enforcement.md) (tenancy) and [ADR-0023](0023-sessions-and-credentials.md) (identity split, sessions). It changes none of them.

## Context

After Phase 3 a `Customer` was a thin commerce profile (user id, name, email) next to the login `User`:
- The order kept whatever address text was typed at checkout. There was no address book.
- There was no account status other than disabling the login.
- A store could not list or look up its customers.
- A customer could neither download nor delete their data.

The roadmap asks for:
- a self-service profile;
- an address book with default shipping and billing addresses;
- Active/Blocked status;
- an admin list and detail with order history;
- basic export and deletion;
- ownership and isolation proven by tests.

## Problem

Where do addresses live, and how does an order use one without a later edit rewriting history? What does "blocked" stop, and what does it leave the customer? How do we delete a person's data without breaking the store's financial records or the database's foreign keys?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Where addresses live | `CustomerAddress` entities inside the `Customer` aggregate (table `CustomerAddresses`, shadow FK, cascade). The aggregate keeps at most 20 addresses, and **exactly one default shipping and one default billing while any address exists**; removing a default moves it to the oldest remaining address | A separate aggregate: the "one default" rule would span aggregates. A JSON column: no id to choose an address by at checkout |
| Address shape | `PostalAddress` value object: recipient, phone, ISO-3166 alpha-2 country, city, line 1; optional region, line 2 and postal code. Trimmed, upper-cased and validated in the Domain (`InvalidCustomerData`) | Free text: unusable for shipping rates later. A country and city catalogue: premature |
| How an order uses it | Checkout sends `shippingAddressId`, which is resolved **inside the caller's own book** (otherwise `400 AddressNotFound`), or free text. The order stores a single-line snapshot (at most 500 characters, now guarded by `Order`) | A foreign key to the address: editing or deleting it would rewrite past orders. Structured order-address columns: Phase 12 decides the order's address shape together with shipping rates |
| What "blocked" means | `CustomerStatus` on the profile. A blocked customer cannot place orders or write reviews (`403 CustomerBlocked`), but can still sign in, see their orders and export their data | Disabling the login (`User.Status`): that is Identity's security lever, and it would also stop a blocked customer from exercising their data rights |
| Deleting a customer | **Anonymize in place**, in one save: <br>• the profile's name, email and phone are replaced, its addresses are removed, and the status becomes Blocked; <br>• the `User` gets an unusable email and an empty password hash and is disabled. <br>Then refresh tokens are revoked and the cached security stamp is forgotten, so existing access tokens stop working immediately. Orders and reviews stay, pointing to a profile that no longer identifies anyone | Hard delete: breaks the order, coupon and review foreign keys and the store's financial records. A soft-delete flag alone: the personal data would remain |
| The order's address after deletion | Kept: the snapshot is part of the invoice | Rewriting it: the store's accounting record would no longer match what was shipped and paid for |
| Who can delete | The customer, after re-entering their password, and a store admin with `customers.manage`. Both go through one `CustomerErasure` path and are audited | Platform staff only: each store handles its own customers' requests |
| Export | JSON with the profile, addresses, orders with their lines, and reviews. No credential material. Audited | CSV: the data is nested |
| Admin list | A read projection in Infrastructure (`ICustomerQueries`) with order count, spend (paid, shipped and delivered orders, less discounts) and last order date, as correlated subqueries in one statement. Order history comes from Ordering (`GET /api/orders?customerId=`) | A Customers → Ordering dependency in the Application layer |
| Permissions | `customers.view` (list, detail) and `customers.manage` (block, export, delete) | A single permission: support staff should be able to look customers up without being able to delete them |

## Decision

The options marked "Chosen" above. Details:
- `/api/account` has no customer id anywhere. The profile is the caller's (`cid`), and a staff account gets `403 CustomerAccountRequired`.
- An address id outside the caller's own book is a 404 on update, delete and the default endpoints, and `400 AddressNotFound` at checkout.
- Store isolation is the Phase 2 mechanism (query filter, write guard). Store B's admin gets 404 for store A's customer on every admin route.
- The deleted profile's email becomes `erased-{id}@erased.invalid`, so the original address is free to register again, as a new and unrelated account.

## Consequences

- **Positive:**
  - One address model for the account page, checkout and the admin views.
  - Past orders never change when an address is edited.
  - "Blocked" is a commercial decision that leaves the customer's data rights intact.
  - Deletion keeps financial records valid and ends every session at once.
- **Negative / limits:**
  - The retained order snapshot contains the recipient's name, phone and address. That is lawful accounting retention, but stores need a documented retention period, and automatic purging of old snapshots is not implemented (Phase 20, compliance).
  - Ordering and Reviews still load the `Customer` aggregate through `ICustomerRepository` (block check, address snapshot). The narrower `ICustomerDirectory` contract from [Modules.md](../04-MODULES/Modules.md) is deferred until a second consumer or an extraction needs it.
  - Changing the sign-in email needs a verification flow and is not self-service yet. Marketing preferences arrive with notifications (Phase 14).
  - The order has no billing address yet (Phase 12).

## Verification

- **Domain:** `CustomerProfileTests` (address rules, defaults, limit, blocking, erasure, `User.Erase`, the order-address guard).
- **Application:** `CustomerAccountHandlersTests` and `CreateOrderCustomerRulesTests` (a blocked customer cannot order; an address outside the book is rejected; the snapshot comes from the chosen address). `CreateReviewHandlerTests` covers the blocked-customer case.
- **Integration:**
  - `CustomerAccountTests`: profile and addresses; cross-customer 404s; an order placed with a saved address; the admin list, detail and blocking; export and self-deletion; admin export and deletion.
  - `TenantIsolationTests`: every admin customer route and every account address route, listings, and a state check after the attacks.
  - `AuthorizationMatrixTests` covers the new endpoints automatically.
