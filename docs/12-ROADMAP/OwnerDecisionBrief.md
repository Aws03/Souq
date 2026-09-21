# Owner decision brief

> **What this page is.** The seven blocked items that only the owner can decide, written so that they can be
> answered **without reading the codebase first**. Each one says what the decision is in plain language, which
> parts of the product are waiting on it, what the realistic options actually are, what each option costs, what
> is already built and therefore not up for decision any more, and ends with **one precise question**.
>
> **Seven items, eight questions.** `C-15` (which currency Souq invoices merchants in) and `P-06` (tax on what
> shoppers buy) are carried together in the register, but they are two different decisions answered by two
> different people, so §5 splits them and asks each separately rather than forcing one answer to cover both.
>
> **What this page is not.** It is not a new plan, and it does not replace the registers. The canonical
> register of owner decisions is [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md); the phases these
> decisions unblock are in [CommercialPlatformPlan.md](CommercialPlatformPlan.md); the design they execute is
> [CommercialPlatformArchitecture.md](CommercialPlatformArchitecture.md). This page is the **owner-facing
> summary** of those three, and where they disagree with the code, the code wins and this page is wrong.
>
> **Nothing here has been implemented.** Writing this brief changed no product behaviour. Every phase named
> below as blocked is still blocked.
>
> **Last verified against the code:** 2026-09-21, branch `phase/17-production-hardening`, at `8faf2f4`.

---

## 0. How to use this page

**You do not need to understand the architecture to answer these.** Each section is self-contained. Read
section 1 to see which questions are urgent, then answer them in the order section 10 recommends.

**How to answer.** Reply with the decision id and the letter, for example:

```
C-08: A
C-17: B
TD-42: B
```

An answer of "not yet" is a real answer for seven of the eight, and it costs nothing except delay. **It is not a
real answer for `C-08`** — for that one, delay destroys data permanently, and section 2 explains exactly why.

**Three kinds of statement appear below, and they are labelled wherever they could be confused:**

| Label | Means | Who is the authority |
|---|---|---|
| **Engineering fact** | Something that is true of the code today, checked by reading it | This repository |
| **Business decision** | A commercial choice with no single right answer | The owner |
| **Legal / accounting decision** | A question with a legal or tax answer | A lawyer or an accountant, **not engineering** |
| **External-provider constraint** | Something a payment provider, tax authority or registrar imposes | The provider — and it must be confirmed with them directly |

**Where research is quoted, it is quoted as research, not as law.** Several statements below about payment
providers, Jordanian e-invoicing and data-protection regimes were recorded in this repository on 2026-09-20
from provider and public documentation. They are written here as *what was found*, and every one of them should
be re-confirmed with the provider, the tax authority or a lawyer **before money or a contract depends on it**.
Engineering cannot decide a legal question and does not try to below.

---

## 1. The eight questions at a glance

| # | id | In one line | Blocks | Truly blocking? | Needs anyone else? |
|---|---|---|---|---|---|
| 2 | **C-08** | Do we store an anonymous identifier for signed-out shoppers? | `C9`, then `C10` | **Yes — and the cost rises every week** | A lawyer, for the "yes" branch |
| 3 | **C-17** | What does a suspended storefront actually do? | `C3`, then `C6` | Yes | No |
| 4 | **TD-42** | Can a store publish its own privacy policy and terms, or does it link out? | `M2`, part of `C8` | Yes | No |
| 5a | **C-15** | Which currency does Souq invoice its merchants in? | `C5`, then `C6` | Yes | An accountant, to confirm |
| 5b | **P-06** | Are shopper prices tax-inclusive, tax-exclusive, or is no tax collected? | selling in a taxed jurisdiction; `C5` | Yes | **An accountant** |
| 6 | **D-13** | Who is legally the seller, and how does Souq collect its own revenue? | `C12`, then `C13` | Yes | **A lawyer** |
| 7 | **C-01** | Which payment provider does Souq go live with? | `C12` | Yes | The provider; answer **after** D-13 |
| 8 | **C-19** | What does the merchant agreement say the platform operator can see? | **nothing technical** | **No** | A lawyer, to draft the wording |

**Read the "truly blocking" column carefully.** Seven of these stop engineering work. **`C-19` stops nothing** —
the capability already exists and is already audited; what is open is only what merchants are *told* about it.
It is on this list because it ages badly, not because anyone is waiting on it.

---

## 2. `C-08` — a visitor identifier for signed-out shoppers

> **This is the only decision on this page that costs something every week it is unanswered.** Everything else
> can be answered later at the same price. Read this section even if you read nothing else.

### 2.1 What is the decision?

When somebody browses a store **without signing in** — which is most shoppers, most of the time — Souq
currently has no way to know that the person who searched for "حذاء رياضي" is the same person who, two minutes
later, opened a product and added it to a basket. Every action is an unconnected event.

**A visitor identifier is a random number Souq gives that browser**, stored in a cookie, so that those actions
can be recognised as belonging to one visit and one person-shaped thread. It is not a name, an email, a phone
number or an advertising id. It is meaningless outside Souq.

The decision is: **do we start storing one?** And if yes, three things come with it that are part of the same
decision: on what legal basis, for how long, and may those rows be processed outside Jordan.

### 2.2 Signed-out versus signed-in attribution, in plain terms

| | Signed-out shopper | Signed-in shopper |
|---|---|---|
| **Who they are to Souq today** | Nobody. Each request is anonymous and unlinked | A customer account with an id |
| **What can be measured today** | Nothing connected. Only totals: "there were 400 searches" | Also nothing connected — there is no event table for signed-in shoppers either |
| **With a visitor identifier** | The whole visit links together: search → list shown → click → product view → basket → order | The same, **plus** their visits before and after they signed in join up through one separate table |

The important part: **most of the valuable journey happens before sign-in.** A shopper searches, browses and
decides while signed out, and signs in only at checkout. Attribution that starts at sign-in starts at the end
of the story.

### 2.3 What data would actually be stored

If the answer is yes, a **new table** is created. Each row is one thing that happened, and carries:

- a **random visitor id** (an opaque value minted by the Souq server, e.g. a 128-bit random number);
- a **session id** (which visit this was — a new one after 30 minutes of inactivity);
- **what happened** — searched, saw a list, clicked position 3 in that list, viewed a product, added to
  basket, began checkout, purchased;
- **the context that cannot be recovered later** — which search produced this list, what position the product
  occupied, and the price, currency, category and stock status **as the shopper saw them at that moment**;
- the time, the store, the language.

A **second, separate table** maps visitor id → customer id, and only for shoppers who signed in.

### 2.4 What would NOT be stored

This is as much a part of the proposal as what is stored, and it is the design in
[ADR-0050](../11-ADR/0050-behavioural-event-foundation.md), not a reassurance invented here:

- **no name, email or phone number** on the event rows;
- **no IP address** — the identifier is not derived from one;
- **no device fingerprint** and **no third-party advertising identifier**;
- **no customer id on the event row itself** — it lives only in the separate link table, which is what makes
  an erasure request cheap;
- **nothing sent to an external analytics vendor.** [ADR-0050](../11-ADR/0050-behavioural-event-foundation.md)
  rejects a vendor as the system of record outright, partly because that would be a cross-border transfer of
  personal data and partly because a vendor holding the only copy can never be removed.

### 2.5 What it enables that nothing else can

Four capabilities, and **only a visitor identifier produces them**:

1. **Search-to-purchase attribution** — which search query actually produced a sale. Today a merchant can see
   that a term was searched 200 times and found nothing 40 times, and nothing about whether it sold anything.
2. **Funnel analysis** — where shoppers abandon: at the list, at the product page, at the basket, at payment.
3. **Click-through rate on any list or recommendation slot** — which is what makes it possible to tell whether
   a change to search ranking made things better or worse, instead of arguing about it.
4. **Behavioural recommendations** — "bought together", "viewed together", and any ranking evaluated against
   real outcomes.

**What stays possible with a "no":** aggregate counts (how many searches, how many views), and
**attribute/content similarity** recommendations — "related products" based on category, price band and
attributes. That last one needs no behavioural data at all and is the only recommender that is correct on day
one for a brand-new store, so "related products" is **not** lost under a "no".

### 2.6 What is lost permanently if the answer is "No"

**The four capabilities above, for the entire period the answer is "no", with no way to backfill.**

This is the part that is unlike every other decision here. If you answer `C-17` in six months, engineering
builds suspension in six months and nothing was lost in between. If you answer `C-08` in six months, the six
months of shopping that happened meanwhile can **never** be analysed — not by a better tool, not by a vendor,
not by paying more later. The rows do not exist and cannot be reconstructed, because the facts they would have
carried (which list, which position, which search) were never written down anywhere.

So **"not yet" is not a neutral hold on this one.** It is a decision to lose that period's data, and if that is
the choice it should be made deliberately rather than by silence.

### 2.7 Retention and privacy implications — plainly

| Today | With a visitor identifier |
|---|---|
| The search log holds **no personal data by construction** — no customer id, no IP, no session, no visitor token. This is enforced by a test, not by a promise | The new event table **becomes personal data** under GDPR-style regimes and under Jordan's data-protection law |
| Its 90-day retention was an **engineering** decision, because there was nothing personal to regulate | Retention becomes a **legal** question with a legal answer |
| No consent question arises | A **lawful basis** is needed, and whether consent is collected per store or once per platform is a question the merchant agreement has to answer |
| No cross-border question arises | Whether the rows may be processed outside Jordan becomes an explicit question |

**Engineering fact, and a caveat that must not be lost:** "no personal data" is a statement about *columns*,
not *content*. The existing search log already stores the shopper's raw typed text for 90 days, and a shopper
can type an email address into a search box. That is true today, with or without this decision.

**Legal decision, not engineering's:** the lawful basis, whether consent is per store or per platform, the
retention period you are willing to state publicly, whether events may leave Jordan, and what the merchant
agreement says about who owns a store's behavioural data. Engineering will implement whatever is decided and
must not pick any of them. For reference only, and to be confirmed with a lawyer rather than relied on: the
repository's own research recorded that 13 months matches one regulator's stated tracker lifetime and 14 months
is a common industry retention figure.

### 2.8 Can the design be made reversible? Yes — and it already is

Every one of these exists in [ADR-0050](../11-ADR/0050-behavioural-event-foundation.md) specifically so that a
later "stop" is cheap:

| Mitigation | What it buys |
|---|---|
| The identifier is **opaque and Souq-minted** | It has no value to anyone else, and it can be rotated or dropped without touching the rest of the schema |
| The **identity link is a separate table** | Erasing a customer is a delete in one place, not a rewrite of an append-only store |
| **Roll up before you purge**, and keep the rollups | Shortening retention later costs detail, not history — the identifier-free aggregates survive indefinitely |
| **Retention is configuration, not schema** | Changing 14 months to 6 months is a setting, not a migration |
| **No external processor by default** | No cross-border transfer happens unless a separate, explicit decision adds one |

So a "yes" is reversible in the sense that matters: you can stop, shorten, or erase later. A "no" is **not**
reversible, because the data was never written.

### 2.9 The smallest useful implementation

If the answer is "yes" but you want the smallest possible version, this is it — and it is genuinely useful on
its own:

1. One opaque visitor cookie, minted server-side.
2. One event table with the envelope, and **four** event kinds only: search executed, list impression (with
   position and list identity), product viewed, order placed.
3. The search-execution id minted at query time and echoed back — this is the single field that makes
   attribution work at all, and the one that can never be added retroactively.
4. The separate visitor → customer link table.
5. One rollup job and a retention setting.

Recommendations, dashboards, co-occurrence and the rest are later work on top of the same rows. **What must
happen now is the capture, not the analysis.**

### 2.10 What has already been implemented

**Engineering facts, verified in the code at `8faf2f4`:**

- **No visitor identifier exists anywhere.** There is no `VisitorId`, `SessionId` or `AnonymousId` in the
  codebase, and no session concept at all.
- **No behavioural event table exists.** No product view, click, impression, add-to-cart or purchase event is
  recorded anywhere. No `SearchExecutionId` is minted or echoed.
- **`SearchQueryLog` exists and holds exactly nine columns** — id, tenant, culture, the raw term, the
  normalised term, the result count, the search time, and the two audit timestamps. An integration test reads
  the *migrated database model* and fails if that list changes, precisely so a personal-data column cannot be
  added quietly. **That guard is working as designed and must not be weakened.**
- **Its retention is 90 days**, a configurable setting validated at startup to stay between 7 and 730 days.
- **The write path is the right pattern and already proven** — it returns nothing, never throws, drops rather
  than waits when the buffer is full, and writes in the background inside each store's own scope. The event
  store would generalise this, not replace it.
- **The only anonymous identifier that exists today is the guest basket token**, and it is *not* usable as a
  visitor id: it is stored only as a hash, lives in an `HttpOnly` cookie scoped to the basket endpoints,
  expires in 30 days, and is **destroyed the moment the shopper signs in**.
- **There is no guest checkout.** Placing an order requires an account, so today every purchase already has a
  customer id on it — the gap is everything that happened *before* the order.
- **The only table that stores IP addresses is the audit log**, for administrative write actions only, and it
  has no stated retention policy. Browsing is not logged with an IP anywhere.
- **Erasure today anonymises in place** — the customer record is blanked, the account disabled, sessions
  revoked, basket and wishlist deleted; orders and reviews are kept as commercial records. There is currently
  **no hook that could reach a behavioural event store**, which is exactly why the separate link table is the
  cheap path.

**Nothing about this decision has been pre-empted by the code.** The design exists on paper and is explicitly
marked *not implemented*.

### 2.11 What the current architecture recommends preserving

These are **existing architectural constraints**, not business preferences, and they hold under either answer:

- **Do not widen the search log.** A behavioural event is a new table. The test pinning the search log's
  columns stays.
- **Keep the write path off the shopper's critical path** — never blocking, never throwing, dropping under
  load. Measurement must not be able to slow or break shopping.
- **Never bill from this pipeline.** It is deliberately at-most-once. Billing needs durable rows with their own
  idempotency key, which is a different mechanism in a different phase.
- **Keep the identity link separate from day one** if the answer is yes. Retrofitting that separation onto an
  append-only table that already embeds customer ids is the documented expensive failure.
- **Write the rollup jobs before the first purge**, because an aggregate cannot be recomputed from rows that
  have been deleted.
- **No external analytics vendor as the system of record**, under either answer.

### 2.12 Exact owner decision

> **`C-08` — Does Souq store an opaque, Souq-minted visitor identifier for signed-out shoppers?**
>
> - **A — Yes.** Store it, with the reversible design above. Engineering proceeds with `C9`, and you supply the
>   lawful basis, the retention period and the residency answer (with legal advice) before the first row is
>   written.
> - **B — No.** Do not store one. This is recorded as a decision, and the four foreclosed capabilities —
>   search-to-purchase attribution, funnel analysis, click-through rate, behavioural recommendations — are
>   written into the register as permanently unavailable for the period the answer stands. `C9` is cancelled,
>   `C10` is limited to attribute similarity for ever, and nothing is captured.
> - **C — Signed-in shoppers only.** Record events only once a shopper has an account. No cookie for signed-out
>   visitors. This keeps the pre-sign-in journey — the majority of it — permanently unmeasurable, but avoids the
>   anonymous-tracking question entirely.
>
> **Answer A, B or C. "Later" is not available here without accepting the loss described in 2.6.**

---

## 3. `C-17` — what a suspended storefront actually does

### 3.1 What is the decision?

A store can be **suspended** — switched off temporarily, because the merchant has not paid, has broken the
contract, or is being investigated. It is meant to be reversible, unlike **archived**, which is the end of the
relationship.

The decision is: **when a store is suspended, what should actually happen to it?** Specifically, what does a
shopper see, what can the merchant still do, and what happens to orders that were already placed and paid for.

### 3.2 Why does Souq need this decision?

- **`C3` — "suspension that actually suspends"** is blocked on it. Today suspension is a status column and a
  gate; it is not a coherent behaviour.
- **`C6` — dunning and automated suspension** is blocked on `C3`. This is the phase where the platform first
  takes an irreversible-looking action against a paying customer **automatically**, on a schedule, with no
  human in the loop.

That ordering is the whole point. **While a human types "suspend", today's behaviour is merely rough. The
moment a billing job types it, today's behaviour is dangerous** — because of what §3.6 shows it does not do.

### 3.3 The realistic options

Each option below is a complete, coherent position, so a single letter is a complete answer.

**A — Read-only.** The storefront stays up. Shoppers can browse the catalogue and see their existing orders,
but cannot add to basket or check out. The merchant's admin still works, read-only or fully. Nothing is hidden.

**B — Admin-only (the storefront goes dark, the merchant does not).** Shoppers get a branded "temporarily
unavailable" page. The merchant can still sign in and see their orders, customers and settings — and, above
all, can still act on the reason they were suspended (pay the invoice, fix the violation). Existing customers
can still track orders they already paid for.

**C — Fully dark.** Nothing answers except the unavailable page. Nobody signs in, no order tracking, nothing.

### 3.4 What changes technically for each option

| | A — read-only | B — admin-only | C — fully dark |
|---|---|---|---|
| **Backend** | The storefront read endpoints gain a "store closed" exemption; basket and checkout refuse with a specific reason | The permissioned admin surface gains the exemption it already has during provisioning; order tracking gains one | Smallest change — today's behaviour, plus the fixes in §3.6 |
| **Frontend** | The boot screen gains a third mode, and every purchase control needs a disabled state and copy | The boot screen must stop blocking the admin routes (see §3.6 — it currently blocks them) | Copy fix only: three different states share one message today |
| **Sessions** | Keep merchant sessions | Keep merchant sessions | Revoke them |
| **Email** | Transactional email about existing orders continues; marketing stops | Same | All store email stops except administrator account recovery |
| **Operational** | Highest support load — shoppers see a shop that will not sell | Lowest: the merchant can self-serve their way out of suspension | Highest risk: a merchant suspended by a billing job cannot log in to pay the invoice that would un-suspend them |

### 3.5 What becomes impossible or more expensive

- **Choosing C makes automated dunning genuinely hazardous.** If a suspension for non-payment also locks the
  merchant out, the only recovery path is a support ticket, for every single incident. That is a permanent
  operational cost, not a one-off.
- **Choosing A means the catalogue of a suspended store stays publicly visible and indexed by search engines.**
  For a suspension over a contract dispute or abuse, that may be exactly wrong, and it is hard to reverse —
  search engines have already cached it.
- **None of the three is irreversible in the code.** Changing the answer later is a modest change in two
  places. The expensive-to-reverse part is the *customer-facing promise*, not the implementation.
- **Orders already placed are the sharp edge under every option.** See §3.6.

### 3.6 What has already been implemented

**Engineering facts, verified in the code at `8faf2f4`. Several of these are defects rather than behaviour, and
they are why `C3` exists:**

- **The four store states exist** — provisioning, active, suspended, archived — and the transitions are guarded
  in the store aggregate. Suspension is reversible; archiving is terminal and nothing leaves it.
- **A closed store answers `503`** with a store-unavailable code, and the storefront configuration endpoint is
  deliberately still served, so the shopper sees a *branded* unavailable page rather than a generic error.
- **Suspended and archived behave identically today.** They fall on the same branch of the same check. There is
  no behavioural difference between "temporarily off" and "gone".
- **Five endpoints are exempt from the gate**: login, refresh, logout, "who am I", and the storefront
  configuration. Everything behind a permission answers `503`.
- **The backend deliberately lets a suspended store's administrator sign in — and the frontend then blocks
  them.** The browser app treats *any* status other than active as "closed" and refuses to mount the
  application at all, including the login and admin routes. **So today, in a real browser, a suspended
  merchant cannot reach their own admin.** The intent recorded in the architecture and the actual behaviour
  disagree, and option B cannot be delivered without fixing this.
- **A customer who already paid loses sight of their order.** Public order tracking carries no exemption, so
  the moment a store is suspended, a shopper with a paid, unshipped order can no longer see it. **This is
  arguably the sharpest question inside `C-17`** and every option must answer it.
- **Suspending does not revoke sessions or refresh tokens.** An archived store's administrators keep sessions
  that can be refreshed indefinitely, because the refresh endpoint is exempt. A test pins this as today's
  behaviour, deliberately written so that it goes red when it is fixed.
- **A suspended — or archived — store keeps sending email.** The outbox dispatcher selects messages with no
  reference to store status at all, so order confirmations and invitations keep going out in the merchant's
  name.
- **Suspension cannot be applied to a store that is still being provisioned.** The guard refuses it, so the
  only lever against a provisioned-but-never-opened store is archiving, which is irreversible. That is a real
  trap once dunning is automated.
- **Four background jobs cannot see provisioning or archived stores** — including stock-reservation expiry, so
  stock reserved in an archived store is never released, and the search-log retention purge, so a privacy job
  silently stops running for archived stores.
- **There is no store-level data export and no hard delete.** The state enum's own comment says an archived
  store's data is "kept for export"; **no such export exists.**
- **The shopper-facing copy says "temporarily closed — we'll be back soon"** for all three of provisioning,
  suspended and archived. One message for three very different situations, one of which is permanent.

### 3.7 What the current architecture recommends preserving

**Existing architectural constraints** (not business preferences):

- **The store-status gate stays in one middleware.** It is one check in one place, and it must not be
  duplicated into individual endpoints.
- **A closed store keeps serving its own branding**, so the unavailable page is the merchant's, not Souq's.
  That was a deliberate decision and it holds under all three options.
- **Suspension must be driven by Souq's own invoice state**, never by a payment provider's webhook — so the
  behaviour is identical for a merchant paying by bank transfer with no provider involved.
- **Archived stays terminal.** Whatever `C-17` decides about suspended, it should not make archived reversible.
- **Automated suspension must not ship before cross-instance cache invalidation** exists, or a suspension can
  take up to a minute to reach a second server. That is a separate phase, not part of this decision.

**A boundary worth stating: data export, retention after offboarding, and hard delete are a *different*
decision.** They belong to archiving and offboarding, not to suspension, and this brief does not fold them in.

### 3.8 Exact owner decision

> **`C-17` — When a store is suspended, which of these is the behaviour?**
>
> - **A — Read-only.** Storefront browsable, purchasing refused, merchant admin available, order tracking
>   available.
> - **B — Admin-only.** Storefront shows the branded unavailable page; the merchant can still sign in and act;
>   customers can still track orders they already paid for.
> - **C — Fully dark.** Nothing but the unavailable page; merchant sessions revoked; no order tracking.
>
> **Answer A, B or C.** Engineering will additionally fix, under every answer, the defects in §3.6 that are
> not behaviour choices: suspended-versus-archived being indistinguishable, the outbox ignoring store status,
> sweeps not seeing archived stores, and the single message shown for three different states.

---

## 4. `TD-42` — store-authored pages, or links to pages hosted elsewhere

### 4.1 What is the decision?

A store selling online normally needs a published **privacy policy**, **terms of service**, a **returns
policy**, a **shipping policy** and often an **FAQ**. Souq has no way to give a merchant any of these today.

Two shapes would close that gap, and they differ by roughly an order of magnitude in what gets built:

- **Pages written inside Souq** — a small content-management feature: the merchant writes the text in the
  admin, in Arabic and English, and it appears at an address on their own store.
- **Links to pages hosted elsewhere** — a few optional address fields in the store's settings. The merchant
  puts the policy on their own website (or any document host), pastes the link, and the footer shows it.

### 4.2 Why does Souq need this decision?

- **Phase `M2` of the master plan is blocked on exactly this one deliverable** and nothing else. It is the only
  reason that phase has not closed.
- **Phase `C8` (customisation a merchant can see)** carries it as one of three parts.
- It is named in the roadmap's own first-sellable-release discussion as a gap that affects **whether a store
  can legally represent itself** in jurisdictions that require a reachable privacy policy and terms.

**Legal note, not an engineering claim:** whether a published privacy policy and terms are *required* for a
given store depends on where that store sells and what it collects. That is a question for a lawyer. What is
certain is the engineering fact: **Souq cannot produce one today in either shape.**

### 4.3 The realistic options

**A — Authored pages inside Souq.** A new content-page concept owned by the catalogue area: a database table,
an admin editor with a per-language body, a public address per page, and footer links that appear only when a
page exists.

**B — Policy links on the store's settings.** Five optional address fields on the settings the merchant
already edits. The footer shows a link only where an address is set. No new table, no new editor screen, no new
public route.

**C — B now, A later if a customer actually asks.** Ship the links immediately; treat authored pages as a
separate, separately-decided capability if a real merchant asks for it.

### 4.4 What changes technically for each option

| | A — authored pages | B — policy links |
|---|---|---|
| **Database** | A new table, a migration, tenant isolation from day one, a unique address per store | **None.** Store settings are stored as a single JSON column, so new optional fields need **no migration at all** |
| **Backend** | A new aggregate with its own rules, admin create/edit/delete behind a permission, a public read endpoint, an architecture decision record | A handful of optional fields, validated as web addresses |
| **Frontend** | A new public page that renders the body in both languages and both reading directions, a rich-ish editor in the admin, conditional footer links | Five text boxes in a settings section that already exists, plus conditional footer links |
| **Lifecycle questions it opens** | Can a page be unpublished? Is there approval? A length limit? Versioning? **The repository has no opinion on any of these yet**, so each is new product design | None |
| **Security** | Merchant-authored content rendered to the public — it must be bounded and sanitised, because the white-label rules forbid arbitrary HTML or script | The merchant's content lives outside Souq entirely; Souq renders only a link |
| **Rough size** | A genuine new capability | A few fields |

### 4.5 What becomes impossible or more expensive

- **Neither option is irreversible.** Choosing B does not prevent building A later; the link fields would
  simply be superseded or kept alongside. This is the least-committing decision on this page.
- **Choosing A first is the expensive order**, because it means designing a miniature content-management system
  — with its own publishing lifecycle — before any merchant has asked for one, while the legal gap it exists to
  close could have been shut in a day.
- **Choosing A also imports questions nobody has answered:** approval, versioning, unpublishing, length limits.
  Each is a small product decision that will come back to the owner.
- **The one thing B genuinely does not give a merchant** is content they author and maintain *inside* Souq.
  Their policy lives on their own site, and if that site disappears the link dies. For a merchant with no
  website at all — which is plausible for exactly the small merchants Souq targets — **B is not a real answer**,
  and that is the strongest argument for A.

### 4.6 What has already been implemented

**Engineering facts, verified in the code at `8faf2f4`:**

- **No content-page capability exists in either shape.** No table, no aggregate, no admin screen, no public
  route. This was confirmed by reading the code, not inferred.
- **The footer's dead links were deliberately removed.** It used to render six links pointing nowhere; they
  were deleted on the reasoning that an absent link is more honest than a broken one. The footer today shows
  the brand, the store description, social icons, home/wishlist/login/register, and contact details **only
  where they are set** — which is exactly the conditional pattern option B would reuse.
- **Store settings already hold seven groups of merchant-editable fields** — display name, enabled languages,
  branding, contact, social links, search-engine title and description, and an announcement — and they are
  **stored as a JSON column, so adding optional fields requires no database migration**.
- **Per-language content already has an established shape.** Several settings are already stored as
  language → text maps, normalised on the server, and displayed in the visitor's language falling back to the
  store's default. **Any page body should reuse exactly this shape** rather than invent one.
- **One settings editor screen serves both the merchant and the platform operator**, driven by a small
  adapter, with the allowed values supplied by the server — so a new field surfaces in both places at once.
- **The theme presets are a related but separate gap.** Three presets exist, are validated on the server, are
  delivered to the browser and are selectable in the editor — and **no stylesheet reads them**, so all three
  look identical. That is part of `C8` but it is **not** part of this decision, and it needs no owner input.

### 4.7 What the current architecture recommends preserving

**Existing architectural constraints** from the white-label decisions, which bind option A if it is chosen:

- **Customisation is configuration.** There is no arbitrary CSS and no arbitrary script per store — this was
  decided explicitly, on upgrade-safety and cross-site-scripting grounds, and it is not reopened here.
- Therefore **any authored page body must be bounded and sanitised data**, validated on the server, rendered by
  the one shared browser build through the existing design tokens.
- **Reuse the existing language → text shape** for any page body.
- **Never render a dead link.** The footer shows a link only when there is something behind it — the rule that
  caused the original links to be deleted.
- **Platform-versus-merchant authority is already drawn:** the platform operator controls what affects
  contracts and integrity; the merchant controls presentation and content after handover. Store-authored pages
  fall on the merchant's side of that line under either option.

### 4.8 Exact owner decision

> **`TD-42` — How does a store publish its privacy policy, terms, returns, shipping and FAQ?**
>
> - **A — Authored inside Souq.** Build the content-page capability: a new table, an admin editor with a
>   per-language body, and a public page per policy.
> - **B — Links only.** Add optional address fields to the store's settings; the footer links out to pages the
>   merchant hosts elsewhere.
> - **C — B now, and revisit A when a merchant actually asks for authored pages.**
>
> **Answer A, B or C.** The repository's existing recommendation, recorded before this brief and unchanged by
> it, is **C** — it closes the legal gap immediately at near-zero risk and forecloses nothing.

---

## 5. `C-15` and `P-06` — invoicing currency, and tax

> **These are two decisions, not one, and they are answered by two different people.** `C-15` is a business
> decision about how Souq bills its *merchants*. `P-06` is an accounting and legal decision about tax on what
> *shoppers* buy. They are presented together because the same phase (`C5`, platform invoices) waits on both.
> Each ends with its own question.

### 5A · `C-15` — which currency Souq invoices its merchants in

#### 5A.1 What is the decision?

When Souq charges a merchant its monthly or annual fee, **what currency is on that invoice?** Jordanian dinars,
US dollars, or does it depend on the merchant.

This is separate from, and must not be confused with, the currency a **store** sells in. A store already picks
its own selling currency and that is unaffected by this decision.

#### 5A.2 Why does Souq need this decision?

- **`C5` — platform invoices and manual collection** is blocked on it. An invoice cannot be issued without a
  currency, and the currency is frozen onto the invoice at the moment it is issued and can never be edited
  afterwards.
- **`C6` — dunning** follows `C5`, so it is blocked transitively.

**One part of the original question has already been answered by the plan and is not yours to decide again.**
The question as recorded also asked "must it support merchants with no card on file?" — and the architecture
already commits to **manual/offline collection** (bank transfer, with invoices, reminders and grace periods
operating with no payment provider in the loop) as part of `C5`, on the reasoning that this is the mainstream
case in the target market rather than a fallback. That is built into the phase regardless of your answer, so
**the only open part is the currency.**

#### 5A.3 The realistic options

**A — Jordanian dinar for everyone.** One currency, matching the launch market.

**B — US dollars for everyone.** One currency, conventional for software subscriptions sold across borders.

**C — Per-merchant, chosen when they subscribe and then frozen.** A Jordanian merchant is invoiced in dinars, a
foreign one in dollars; the choice is fixed for that subscription.

#### 5A.4 What changes technically for each option

| | A — JOD only | B — USD only | C — per merchant |
|---|---|---|---|
| **Database** | One currency on the invoice; plan prices carry one currency | Same | Plan prices need a price **per currency**, and the subscription records which one |
| **Rounding** | JOD has **three** decimal places. Every percentage calculation has to round to fils, and there is an external constraint (see 5A.5) that may force prices to multiples of 10 fils | USD's two decimals are the well-trodden case | Both rounding rules must be right, in one system |
| **Reporting** | Platform revenue is one number | One number | **Never a single total.** Amounts in different currencies cannot be added without dated exchange rates, which Souq does not have and will not invent |
| **Collection** | Local bank transfer is natural | A dollar invoice collected by local bank transfer creates a conversion difference somebody has to absorb | Both |

#### 5A.5 What becomes impossible or more expensive

- **The invoice number series and the currency are frozen at issue and can never be corrected.** A correction
  is a separate credit note, never an edit. So a currency chosen wrongly is visible in the books permanently —
  this is the least forgiving part of the decision.
- **Choosing C is the expensive one**, and it is expensive in the reporting layer more than the billing layer:
  every platform revenue figure becomes a list of amounts rather than a number, for ever.
- **Choosing A imports a three-decimal rounding problem into the platform's own billing**, on top of the one it
  already has for shopper payments.
- **External-provider constraint, to be confirmed with the provider before relying on it:** repository research
  recorded that at least one regional provider documents a card-scheme rule requiring three-decimal amounts to
  end in a zero — effectively a 10-fils minimum increment. If that holds, it reaches back into *pricing*, not
  just formatting. This is tracked separately as `C-07` and is not part of this question, but it is the reason
  option A is not simply "the local currency, obviously".

#### 5A.6 What has already been implemented

**Engineering facts, verified in the code at `8faf2f4`:**

- **There is no invoice concept anywhere in the codebase**, and no receipt or document generation of any kind —
  not for merchants and not for shoppers.
- **The plan and subscription model exists and contains no money at all.** Plans carry entitlements (what a
  store may use) and numeric limits (how many), and **no price, no amount and no currency field**. The billing
  controller's own comment says so: there is no money here yet.
- **Money handling is solid and already understands three-decimal currencies.** The money type refuses
  negatives, refuses mixed currencies, refuses amounts that cannot be represented in the currency's minor
  units, and funnels every rounding through a single sanctioned point. Seven currencies are recorded as
  three-decimal, Jordanian dinars among them, and storage keeps four decimal places so they round-trip exactly.
- **A store has exactly one currency**, set when it is created and **refused thereafter once it has products or
  orders**. Multi-currency stores are a recorded non-goal.
- **The one revenue figure that already crosses stores never sums across currencies** — deliberately, because a
  combined number would have no unit. Whatever `C-15` decides, that rule already exists and should be kept.

#### 5A.7 What the current architecture recommends preserving

- **Souq's own invoice number series, allocated at issue and immutable** — an unbroken series per issuer is a
  legal requirement in many jurisdictions, and it must not come from a payment provider or it would fork if the
  provider changed.
- **Corrections are credit notes, never edits** to an issued invoice.
- **One rounding point, rates expressed in basis points**, and a named owner for the remainder.
- **Never sum across currencies** in any report.
- **Souq's own subscription and invoice states, never a provider's** — the same word means different things in
  different providers' vocabularies, and one of those disagreements is dangerous.

#### 5A.8 Exact owner decision

> **`C-15` — Which currency does Souq invoice its merchants in?**
>
> - **A — Jordanian dinar (JOD) for every merchant.**
> - **B — US dollars (USD) for every merchant.**
> - **C — Per merchant, chosen at subscription and frozen for that subscription.**
>
> **Answer A, B or C.** Manual/bank-transfer collection is built either way and is not part of this question.

---

### 5B · `P-06` — tax on what shoppers buy

#### 5B.1 What is the decision?

When a shopper sees a price of 25 JOD on a product page, **does that 25 already include tax, or is tax added at
checkout?** And is tax collected at all at launch?

This is the shape question, and it is the one engineering cannot proceed without. The **rates** — who pays what
percentage — are data that can be supplied later; the **shape** changes the code.

#### 5B.2 Why does Souq need this decision?

- **Selling in a jurisdiction that requires tax to be shown or collected is blocked on it.** This is recorded
  as a release blocker, not a missing feature.
- **`C5` needs it too**, because a platform invoice freezes a **tax snapshot** at the moment it is issued —
  country, tax identifier, business-to-business or business-to-consumer, the rate and the literal legend text.
- The liability, if any, accrues from the first sale rather than from the first audit.

**This is an accounting and legal decision.** Engineering has an explicit standing rule not to invent a tax
rule, "not even a reasonable default", because a wrong rate is worse than an obvious gap. Nothing below is tax
advice.

#### 5B.3 The realistic options

**A — No tax collected at launch.** Prices are what the shopper pays; no tax line exists. This is a legitimate
answer only if an accountant confirms it for the launch jurisdiction and the stores in question — for example
because sellers are below a registration threshold.

**B — Tax-inclusive prices.** The merchant enters 25 JOD and that is what the shopper pays; the tax component
is calculated out of it and shown for transparency. This is the common convention in consumer retail in much of
the world.

**C — Tax-exclusive prices.** The merchant enters 25 JOD and tax is added on top at checkout, so the shopper
pays more than the displayed price.

#### 5B.4 What changes technically for each option

| | A — none | B — inclusive | C — exclusive |
|---|---|---|---|
| **Pricing** | Nothing changes — this is exactly today's behaviour | The tax stage computes the component **out of** the price; the total does not move | The tax stage **adds** to the total; the displayed price and the paid price differ |
| **Orders** | Nothing | **A new tax column on the order**, and a change to how the order total is computed | The same new column and the same change |
| **Shopper-facing** | Nothing | Prices unchanged; a tax line appears on the basket and confirmation | **Every displayed price becomes provisional** until checkout — a real user-experience change, and in some jurisdictions a disclosure requirement |
| **Email and documents** | Nothing | The confirmation email needs a tax line, in both languages | Same |
| **Merchant configuration** | Nothing | Rates per store, and whether a store is registered for tax | Same |
| **Platform invoices (`C5`)** | A snapshot recording that no tax applies | A full tax snapshot frozen at issue | Same |

#### 5B.5 What becomes impossible or more expensive

- **Switching between B and C after real orders exist is expensive and visible.** Every historical order was
  computed one way; reports, refunds and any reissued document must keep using the rule that was in force at the
  time. Choosing late is more expensive than choosing now, and choosing wrongly and reversing is the most
  expensive of all.
- **Under-collecting tax is a liability that accrues from the first sale.** That is an accounting exposure, not
  an engineering one, and it cannot be fixed by a later code change.
- **Legal/accounting matter, recorded as research and needing confirmation:** the repository recorded that
  Jordan's national electronic invoicing operates as a **clearance** model — the invoice is validated by the tax
  authority *before* it is legally issued. If confirmed, two consequences follow that are worth knowing before
  answering `D-13`: it is a **separate outbound integration** sitting between "invoice issued" and "invoice
  legally valid", and it is a strong argument that **the store must remain the invoice issuer**. **Confirm this
  with an accountant or the tax authority — this brief does not assert it as fact.**

#### 5B.6 What has already been implemented

**Engineering facts, verified in the code at `8faf2f4`:**

- **The pricing pipeline has an explicit, deliberate zero where tax belongs** — stage five of six, with a
  comment naming `P-06` as the open decision. A test asserts that it is zero, so it cannot drift silently.
- **The tax slot already exists in the quote and in the basket the browser receives**, and always reads zero.
- **Orders have no tax field at all.** The order total is (subtotal − discount) + shipping, with no tax term,
  and it is frozen onto the order when it is placed. **This is the most important correction to make to any
  earlier impression that tax is a drop-in change:** it is not. A non-zero tax in the pricing pipeline alone
  would quote one figure and charge another. The existing tests would catch it rather than let it ship, but
  closing `P-06` needs a new column on the order, not just a line in a table.
- **The money type has no decimal multiplication** — only whole-number multiplication — so there is literally
  no way to apply a percentage rate today without adding one, through the single sanctioned rounding point.
- **The order confirmation email has no tax line and no tax label**, in either language. Adding one is a code
  change, not a template edit.
- **There is no invoice or receipt for shoppers either** — no document generation of any kind exists.

#### 5B.7 What the current architecture recommends preserving

- **The tax stage keeps its fixed place in the pricing pipeline**, so introducing tax changes one stage rather
  than the shape of the calculation.
- **The order total stays frozen at placement.** Whatever tax rule applies, the figure the shopper agreed to is
  the figure the order records — a rule that already holds for prices, names and costs.
- **One rounding point**, unchanged.
- **The tax snapshot on a platform invoice is frozen at issue** and never recomputed, so a later rate change
  cannot rewrite history.
- **Engineering will not invent a rate, a threshold or an inclusive/exclusive convention.** If the answer is
  not supplied, the zero stays.

#### 5B.8 Exact owner decision

> **`P-06` — At launch, how is tax handled on what shoppers buy?**
>
> - **A — No tax is collected**, confirmed with an accountant for the launch jurisdiction. The explicit zero
>   stays and is documented as a decision rather than a gap.
> - **B — Prices are tax-inclusive.** The displayed price is what the shopper pays; the tax component is shown
>   for transparency.
> - **C — Prices are tax-exclusive.** Tax is added at checkout and the shopper pays more than the displayed
>   price.
>
> **Answer A, B or C.** If B or C, the rates, the registration thresholds and the required invoice wording
> follow as data from your accountant — they are not part of this question and do not block starting.

---

## 6. `D-13` — who is legally the seller, and how Souq collects its own revenue

> **Read §6.1 first.** Seven words get used interchangeably in this area and they mean different things. Almost
> every expensive mistake in platform payments comes from conflating two of them.

### 6.1 The vocabulary, separated

| Term | What it means | Who it is in Souq, today |
|---|---|---|
| **Legal seller** | The business whose name is on the sale, who owes the goods to the buyer, and who is responsible if the goods never arrive | **The merchant.** Each store sells its own physical goods under its own name |
| **Merchant of record (MoR)** | The entity that appears on the **card statement**, holds the acquiring relationship, owes the card networks their fees, and carries **chargeback liability** when a buyer disputes a charge | **It varies per store, and nobody chose that.** A store that has connected its own payment account is its own MoR; a store that has not is paid into the **deployment account**, making the platform the MoR for that store |
| **Payment processor / provider (PSP)** | The company that actually moves the money — takes the card details, authorises, settles to a bank account. Stripe, MyFatoorah, PayTabs and so on are PSPs | **Stripe only**, plus a fake gateway used in development and tests. Nothing has ever run against a real Stripe account |
| **Platform revenue** | The money **Souq** earns, as distinct from the money a *store* earns from selling goods | **Zero mechanism exists.** No price, no invoice, no commission, no collection |
| **Application fee / platform fee** | A share of each shopper transaction, taken automatically at the moment of payment by the PSP and paid to the platform. Requires the PSP to support splitting | **Not possible today.** The payment adapter sets no fee, destination or on-behalf-of field |
| **Connected account** | A sub-account the platform creates and manages at the PSP on a merchant's behalf, so the platform can onboard merchants, split payments and see their settlement | **Does not exist.** Stores hold their *own* independent accounts, with their own keys, which is a different model |
| **Platform subscription billing** | Souq charging the merchant directly — a monthly or annual fee, invoiced and collected — independently of what the store sells | **Does not exist.** Designed as phase `C5`, blocked on `C-15` and `P-06` |

**The one relationship that matters most:** *how Souq gets paid* depends on *who is in the funds flow*. If Souq
never touches shopper money, it cannot take a slice of a sale — it has to invoice the merchant instead. If Souq
is in the funds flow, taking a slice becomes nearly free, and Souq acquires obligations it does not have today.

### 6.2 What is the decision?

Two questions that are correlated but genuinely separate, and both need an answer:

1. **Who is the merchant of record** — the merchant, or the platform?
2. **How does Souq collect its own revenue** — a subscription billed to the merchant, or a share of each
   transaction?

Answer 1 constrains answer 2: if the merchant is MoR and Souq never touches shopper funds, **a subscription is
the only available revenue mechanism.**

### 6.3 The realistic options

**A — Every store is its own merchant of record.** Connecting a payment account becomes mandatory before a
store can sell. Shopper money goes straight to the merchant; Souq never touches it. Souq earns by **invoicing
the merchant** a subscription.

**B — Souq is in the funds flow.** Shopper money passes through an account the platform controls; Souq can net
its commission at the moment of the sale, and pays the merchant out.

**C — Today's unchosen state.** A store *may* connect its own account; one that has not is paid into the
deployment account. **The merchant of record therefore varies per store.**

**C is not a neutral "do nothing".** It is an unchosen commercial and legal position that differs per customer,
and it has never been checked against any provider's requirements. It is listed because it is what is running,
not because it is a candidate.

### 6.4 What changes technically for each option

| | A — merchant is MoR | B — Souq in the funds flow |
|---|---|---|
| **Payment routing** | Always the store's own account; no deployment fallback | A platform-controlled account, with the merchant as a sub-account or payee |
| **Prerequisite defect that must be fixed first** | **Recording which account took each payment** (see §6.6). Mandatory store accounts make replacing an account routine, which turns a latent defect into an everyday one | The same fix, plus a whole ledger |
| **Store readiness** | A **fifth** readiness item — "has a payment account" — plus a decision on whether activation is blocked or merely warned | Onboarding the merchant at the provider instead |
| **Souq's revenue** | **A complete billing and dunning subsystem becomes mandatory**, not optional: invoices, reminders, grace periods, suspension | Commission netting becomes nearly free |
| **Ledger, payouts, reconciliation** | Not needed for commission — there is none | **All of it needed**: an append-only ledger, commissions, payouts, chargebacks with a liable party, reconciliation against the provider's settlement reports |
| **Liability** | The merchant carries chargebacks and fees | **Souq acquires chargeback liability**, know-your-customer obligations, and — per repository research — a probable licensing conversation with the central bank |
| **Refunds** | The merchant's account refunds its own sales | Refunds must also reverse the commission, which can fail independently |

### 6.5 What becomes impossible or more expensive

- **This is the hardest decision here to reverse.** Once stores have been onboarded under one model, moving
  them to the other means re-onboarding every merchant at the provider and dealing with in-flight payments,
  pending refunds and disputes under the old model. Changing after two or three merchants is awkward; changing
  after fifty is a project.
- **Choosing A forecloses transaction-share revenue entirely**, unless you later move to B. If the commercial
  model is "we take 2% of sales", A cannot deliver it.
- **Choosing B is the one that acquires legal obligations.** Receiving and remitting other people's money is a
  regulated activity in many jurisdictions. **Legal decision — a lawyer must answer whether it is licensable in
  Jordan. Engineering cannot and must not.**
- **Leaving it as C is the worst of the three commercially**, because the platform is silently the merchant of
  record for every store that has not connected an account — meaning Souq already carries chargeback liability
  for those stores today, without having decided to.

**Two doors that repository research on 2026-09-20 recorded as closed. Both should be re-confirmed with the
providers before you rely on them, but both are load-bearing for this decision:**

- **Stripe Connect is not available in the home market.** The research recorded that Stripe does not operate in
  Jordan — the UAE being its only country in the region — and that a Jordan-registered connected account is
  limited to a recipient agreement that **cannot process payments at all**. If that holds, "adopt Stripe
  Connect" is an expansion option for other markets, not a Jordanian one.
- **Handing the problem to a merchant-of-record vendor is also not available.** Every mainstream one checked
  excluded **physical goods** *and* independently forbade a platform reselling on behalf of third-party
  sellers. Souq is physical-goods commerce for many stores, so it fails both tests.

### 6.6 What has already been implemented

**Engineering facts, verified in the code at `8faf2f4`. Note carefully: the *mechanism* for both answers is
built. What is missing is the choice.**

- **Both routing paths exist and are tested.** A store that has connected its own account is charged through
  it; a store that has not is charged through the deployment account. Switching the model is configuration and
  policy, not a redesign.
- **Store payment secrets are properly protected** — encrypted with the store's own identity bound into the
  encryption, never returned to any screen, with only a four-character hint shown. Test keys are refused unless
  explicitly permitted. If the encryption key is missing, the system refuses the payment with a clear error
  rather than silently falling back.
- **A payment records the *kind* of account that took it, never *which* account.** So if a store replaces its
  payment keys, every payment the old account took can never be refunded in the application — and a failed
  refund cannot be retried. This is a known defect, rated high priority, and it is a **prerequisite** of option
  A rather than a follow-up, because mandatory accounts make replacement an ordinary event.
- **Store readiness has no payment item.** Provisioning checks four things — domain, domain verified,
  administrator, status — and **nothing checks for a payment account**, so a store can be activated and sell
  with none, taking money into the deployment account.
- **A store can hold only one payment account** — enforced by a unique constraint — and the account record has
  no concurrency protection, so two administrators editing keys simultaneously silently overwrite each other.
- **The payment routing has no test of its own**, because the store gateway is constructed inline with no seam.
  Whichever answer is chosen changes routing, and routing has no regression net today.
- **There is no fee, destination or on-behalf-of field anywhere**, no connected-account identifier, no
  gross-versus-net distinction on a payment, and **no payout, ledger or settlement concept at all.**
- **Verified live on 2026-09-20:** neither store on the QA stack had a payment account, so the platform was the
  merchant of record for both.
- **Stripe Connect is not a recorded non-goal.** Choosing it would need a new architecture decision record, not
  the reversal of an existing one.

### 6.7 What the current architecture recommends preserving

- **The two money paths stay separate.** Shopper → store and platform → merchant are different ports, different
  records, different idempotency namespaces. This was verified rather than asserted: the existing payment path
  *cannot* bill a merchant — it refuses to run outside a store's scope, and a payment cannot exist without an
  order, so a subscription charge has no row to live in.
- **Who is liable and who is the seller is stored data on the store, not a code branch** — because one platform
  can legitimately have stores on both sides of that line at once.
- **Souq owns the money, the lifecycle and the decision; the provider's adapter owns only the wire.**
- **Money never becomes a negative number.** A ledger gets an explicit direction instead, the same way stock
  movements already do.
- **Card data never touches Souq's servers**, under every option.

### 6.8 Exact owner decision

> **`D-13` — Who is the merchant of record, and how does Souq collect its own revenue?**
>
> - **A — Every store is its own merchant of record.** Connecting a payment account becomes mandatory. Souq
>   never touches shopper funds and earns by invoicing merchants a subscription.
> - **B — Souq sits in the funds flow** and nets its commission from each sale, accepting chargeback liability,
>   know-your-customer obligations and a licensing question a lawyer must answer first.
> - **C — Keep today's position**, where it varies per store, and accept explicitly that Souq is already the
>   merchant of record for every store without its own account.
>
> **Answer A, B or C.** If B, do not answer it without a lawyer. **Answer this before `C-01`.**

---

## 7. `C-01` — the payment provider for the launch market

### 7.1 What is the decision?

**Which payment company will Souq actually go live with** in the launch market — the one that takes a shopper's
card and settles the money to a bank account.

### 7.2 Why does Souq need this decision?

- **`C12` — the reshaped payment port — is blocked on it**, together with `D-13`. The reason is specific:
  **the first real adapter sets the port's vocabulary.** A port built against a provider Souq cannot go live
  with would validate the wrong shape, and the shape is the expensive part.
- **`C13` — commissions, payouts, ledger and reconciliation** follows `C12`.

**Answer `D-13` first.** Whether Souq needs a provider that can *split* a payment at the moment of sale depends
entirely on whether Souq is in the funds flow. Choosing a provider before that is choosing a tool before
knowing the job.

### 7.3 The realistic options

**This brief does not choose a provider and must not.** Selecting one is a commercial and contractual process —
pricing, settlement terms, a merchant or partner account application, and a contract — not an engineering
comparison.

What the repository *can* offer is the list of candidates that repository research on 2026-09-20 recorded as
supporting Jordan and Jordanian dinars, together with the single capability that distinguishes them:

| Candidate | What the research recorded |
|---|---|
| **MyFatoorah** | The **only** one verified to combine Jordan, JOD, splitting a payment at transaction time, and a native fixed-plus-percentage commission |
| **Amazon Payment Services** | Prices natively in JOD and documents three-decimal handling — including a card-scheme rule that such amounts must end in zero |
| **PayTabs** (via MEPS), **HyperPay**, **Telr**, **N-Genius** | Support the market; most are redirect-first, and several cannot split a payment at all |

So the realistic options are:

**A — A provider that can split at transaction time.** Required if `D-13` is answered **B**. On the research
above, the candidate list here is short.

**B — A redirect-first provider that cannot split.** Perfectly adequate if `D-13` is answered **A**, because
there is no commission to net — Souq invoices the merchant instead.

**C — Do not choose yet, and do not build `C12` either.** The honest hold. Nothing is lost except time, and
unlike `C-08`, nothing is destroyed.

### 7.4 What changes technically for each option

| | A — splitting provider | B — redirect-first, no splitting | C — defer |
|---|---|---|---|
| **The port's shape** | Must express a fee, a destination and an on-behalf-of party, plus refund reversal | Simpler: start a payment, get a redirect, come back, verify | Unchanged |
| **Return flow** | Usually still redirect-first in this market | The shopper leaves Souq and comes back, so the payment attempt must be findable when they return, when the webhook arrives, and when a reconciler polls — **possibly all three at once, in any order** | Unchanged |
| **Webhooks** | An inbox keyed by provider and event id, verification over **raw bytes and headers** (at least one regional provider *decrypts* the body rather than checking a signature), and the store carried in the route because a callback carries no store address | Same | Unchanged |
| **Ledger** | Needed, with three provider reference columns per movement — payment, fee, payout — because collapsing them makes "why did the bank deposit differ from the sales total?" unanswerable | Not needed for commission | Unchanged |
| **Reconciliation** | Required | Advisable | — |

### 7.5 What becomes impossible or more expensive

- **Building `C12` against Stripe would be building against a provider that — on the recorded research — cannot
  be used in the launch market.** The current payment port is shaped around Stripe's model: a two-step intent
  with a client secret. A redirect-first provider **has no client secret to return**, so that shape does not
  merely need extending, it needs replacing.
- **Choosing a provider that cannot split forecloses transaction-share revenue** with that provider, so it must
  match the `D-13` answer.
- **Changing provider after launch is expensive but not catastrophic**, *provided* the port is built
  provider-agnostically — which is exactly what the design is for. Changing the **port's shape** after several
  adapters exist is the expensive move.
- **External-provider constraints, recorded as research and to be confirmed with each provider directly:**
  several of these are unusual enough to be worth naming, because they are the ones that break naive designs —
  a provider may deliver each webhook exactly once, unsigned, with no retries; a provider may implement
  idempotency keys incorrectly, returning the previous response for a different amount; a merchant's available
  currencies may follow that merchant's own profile rather than the provider's capabilities; and "can process
  JOD" does not imply "can settle JOD".

### 7.6 What has already been implemented

**Engineering facts, verified in the code at `8faf2f4`:**

- **Stripe is the only real provider implemented**, alongside a fake gateway. The fake gateway is chosen
  implicitly only in development and testing; anywhere else the application **refuses to start** unless it is
  selected deliberately — so it can never become the live provider by accident.
- **Nothing has ever been run against a real Stripe account.** Every payment state has been exercised only
  against the fake gateway. This is the single largest money risk on the release list.
- **The existing port is intent-shaped, not flow-agnostic** — it returns a client secret and a publishable key.
  It is genuinely account-agnostic (it can route to any store's account) and genuinely **not** flow-agnostic.
- **The replacement design already exists on paper** and is explicitly marked *not implemented and not
  implementable* until `D-13` and `C-01` are answered.
- **Three decimal places are handled, and the behaviour is switched off deliberately.** The converter carries
  an explicit switch for whether to honour the international standard's decimal count; it is currently off, so
  Jordanian dinars are sent in hundredths. **Both hypotheses are already covered by tests**, including one
  proving they differ only on three-decimal currencies, so answering the related question `P-05` is a one-value
  change in front of a green test — not new arithmetic.
- **`P-05` is a separate, non-blocking item** on this page's list but worth naming here: it is *discovered*,
  not decided. Somebody with the real payment account runs one test charge in a three-decimal currency and
  reports what the provider actually did. **It must not be marked resolved from documentation or a sandbox.**

### 7.7 What the current architecture recommends preserving

- **The core owns the money, the lifecycle and the decision; the adapter owns the wire.** Anything that changes
  *who is liable* or *who is the seller* is neither — it is stored data on the store.
- **Souq mints its own idempotency keys**, recorded *before* the outbound call, because most regional providers
  document no idempotency mechanism and at least one implements it wrongly.
- **An authoritative status query and a scheduled reconciler are part of the design from day one**, not an
  optimisation — because for at least one provider, polling is the only ingestion path with a real guarantee.
- **A return redirect never means success.** It is the least trustworthy of the three ways a payment result
  arrives.
- **Never persist a provider's status as domain state.** Souq's own vocabulary, always.
- **Payment behaviour is never changed silently** — amounts, capture, cancellation, refunds, retries and
  idempotency are commercial behaviour, changed deliberately with a recorded decision and tests, or not at all.

### 7.8 Exact owner decision

> **`C-01` — Which payment provider does Souq apply to go live with in the launch market?**
>
> - **A — A provider that supports splitting a payment at transaction time** (required only if `D-13` = B). On
>   the recorded research, **MyFatoorah** was the only verified candidate combining Jordan, JOD, transaction-time
>   splitting and a native fixed-plus-percentage commission.
> - **B — A redirect-first provider without splitting** (sufficient if `D-13` = A). Candidates recorded:
>   Amazon Payment Services, PayTabs via MEPS, HyperPay, Telr, N-Genius.
> - **C — Not yet.** `C12` and `C13` stay unbuilt. Nothing is lost but time.
>
> **Answer A, B or C — and name the provider if A or B.** Answer `D-13` first; this question cannot be
> answered well before it. Engineering is not choosing a provider and the repository records no preference.

---

## 8. `C-19` — what the merchant agreement permits the operator to see

> **This is the only decision on this page that blocks no engineering work at all.** The capability already
> exists, is already restricted, and is already audited. What is open is what merchants are **told**.

### 8.1 What is the decision?

Souq's operator — you — can see how much money each store made in a period. A merchant signing up is entitled
to know that. The decision is **what the merchant agreement says about it**: whether the operator's visibility
is stated plainly, narrowed, or left unmentioned.

### 8.2 What revenue data Souq now sees, exactly

**Engineering facts, verified in the code at `8faf2f4`.** One endpoint returns, for a chosen window:

- **Per store:** the store's id, slug, name, currency, **gross revenue** and **order count**.
- **Per currency:** a total revenue and order count across the stores using that currency.
- **A "counted order"** means an order that was placed in the window and is paid, shipped or delivered. It uses
  the same single definition the merchant's own dashboard uses, so the two cannot disagree.

**What it deliberately does not return:** no refunds figure, no net revenue, no commission or fee, no customer
names, no product-level detail, no order contents, and **no single total across currencies** — because adding
amounts in different currencies produces a number with no unit, and converting would need dated exchange rates
that do not exist here.

The window is in UTC rather than any store's local day, because the query spans stores in different time zones
and picking one store's day would make the number right for it and wrong for the rest.

### 8.3 Why billing requires it

A platform that **bills its merchants** cannot compute a commission without knowing what was sold. The
commission basis question (`C-03`) — goods only, goods plus shipping, after discounts, or gross including tax —
already presumes this figure exists. So under `D-13` option B, this visibility is not optional; it is the input
to the invoice.

**Under `D-13` option A** (subscription billing only) the figure is not strictly required for billing — which
is why option (b) below is a real option rather than a token one.

### 8.4 What would be visible to the platform operator

Three things, and **two of them predate this and are not new**:

| Capability | Since | Restricted by | Audited |
|---|---|---|---|
| Each store's revenue and order count for a period | C11, 2026-09-21 | A platform-only reports permission | Yes, with its own action name and the period in the record |
| Enumerating a store's **customer list** | Long before | Platform permissions | Yes |
| The **audit log of actions taken inside a store** | Long before | Platform permissions | Yes |

The third one deserves emphasis: the operator can already see *what was done* inside a store. The revenue view
added *how much was sold*. The disclosure question applies to all three together, not only the newest.

### 8.5 What should remain tenant-private

This is where the answer is **contractual**, but the architecture already draws a line that the contract can
simply adopt:

- **Anything the merchant owns is the store's own data** — its catalogue, its orders, its customers, its
  behavioural data if `C-08` is answered yes. It lives in store-owned tables behind an automatic isolation
  filter.
- **Anything the platform owns *about* the merchant** — subscription, invoices, ledger — is platform data.
- **That line is also the answer to "who owns the data" in the contract**, and it is enforced structurally
  rather than by policy.

**What is technically visible today but arguably should not be stated as routinely available:** the contents
of individual orders, individual customers' details, and anything read for reasons other than billing, support
or fraud. Narrowing the *stated purpose* costs nothing technically.

### 8.6 The realistic options

**A — State it plainly in the merchant agreement.** Name what the operator can see — revenue totals, customer
list, in-store audit log — and for what purposes: billing, support, fraud. Nothing changes in the code.

**B — Narrow the capability to aggregates only.** Remove the per-store breakdown and keep only per-currency
totals. **This weakens per-store billing**, and would have to be reversed if `D-13` is answered B.

**C — Leave it unstated.** The current position.

### 8.7 What becomes impossible or more expensive

- **Option C ages badly and gets more expensive the longer it runs.** Telling a merchant at signup that the
  operator can see their sales totals is ordinary. Telling them after two years, following a support
  conversation in which it came up, is a trust problem and possibly a contractual one.
- **Option B is the only one that costs engineering anything**, and it is not much — but it conflicts with
  billing on a commission basis, so it should not be chosen if `D-13` might be answered B.
- **Nothing here is irreversible.** The capability can be narrowed or widened later. What cannot be undone is a
  merchant learning about it from somewhere other than the agreement.
- **Legal note:** whether disclosure is *required* in the agreement, and in what words, is a question for a
  lawyer drafting the merchant agreement. This brief does not assert a legal requirement.

### 8.8 What has already been implemented

**Engineering facts, verified in the code at `8faf2f4`:**

- **The revenue endpoint exists**, behind a platform-only reports permission, served only on the platform host.
- **It is audited with its own action name**, and the period looked at is recorded in the audit entry — so
  whatever the agreement ends up saying, **"who looked at what, and when" is already answerable**.
- **The read goes through the single reviewed type permitted to cross the store-isolation filter**, with the
  rule that a read concerning one store carries an explicit store predicate and a read spanning stores returns
  aggregates only. This is an existing sanctioned pattern, not a new precedent.
- **The window is validated** to at most 366 days, so the endpoint cannot be used to scan the whole order
  history in one request.
- **This deliberately reversed an earlier documented claim.** The Reporting module's document previously said
  no store's commercial figures reached the platform "by design". That was right for a platform that sells
  nothing and impossible for one that bills its merchants. The reversal is recorded rather than quietly made.

**What engineering did *not* do, on purpose: it did not write anything into a merchant agreement, and did not
assume what one would say.** That is why this question exists.

### 8.9 Which parts are engineering and which are contractual

| Part | Whose |
|---|---|
| That the capability exists at all | **Engineering — already decided**, and required by billing under `D-13` = B |
| That it is restricted to a platform permission and audited | **Engineering — already done** |
| That it never sums across currencies and never returns customer detail | **Engineering — already done** |
| **What the merchant is told**, in which document, in what words | **Contractual — the owner's, with a lawyer** |
| Whether the stated purpose is limited to billing, support and fraud | **Contractual** |
| Whether the operator may use aggregated, de-identified cross-store data for product work | **Contractual**, and it overlaps `C-09` (pooling behavioural data across stores), which is a separate open question |

**No contractual assumption has been implemented.** Engineering built the capability the billing model
requires, restricted and audited it, and stopped.

### 8.10 Exact owner decision

> **`C-19` — What does the merchant agreement say about what the platform operator can see?**
>
> - **A — State it plainly:** the operator can see each store's revenue totals and order counts, its customer
>   list and the audit log of actions taken in the store, for billing, support and fraud prevention.
> - **B — Narrow the capability to cross-store aggregates only**, accepting that per-store billing on a
>   commission basis then becomes impossible without reversing this.
> - **C — Leave it unstated** for now, and accept that it is disclosed later or not at all.
>
> **Answer A, B or C.** Nothing is waiting on this, and it can be answered today without consulting anyone —
> though the wording, if A, should be drafted by whoever writes the merchant agreement.

---

## 9. What is truly blocking, and what is not

| Decision | Blocks which phase | What happens if it stays unanswered |
|---|---|---|
| **`C-08`** | `C9`, then `C10` | **Data is permanently lost, every week.** Everything else on this table merely waits |
| **`C-17`** | `C3`, then `C6` | Suspension stays a status column with the defects in §3.6. Automated billing cannot safely ship |
| **`TD-42`** | `M2`, part of `C8` | `M2` cannot close. Stores cannot publish a privacy policy or terms in any form |
| **`C-15`** | `C5`, then `C6` | No invoice can be issued, so Souq cannot charge a merchant through the product |
| **`P-06`** | `C5`, and selling where tax applies | The pricing pipeline's tax stays zero. A liability may accrue from the first sale |
| **`D-13`** | `C12`, then `C13` | The platform stays the merchant of record for every store without its own account — **by default, not by choice** |
| **`C-01`** | `C12` | The payment port cannot be reshaped against a real provider |
| **`C-19`** | **nothing** | The capability keeps working, undisclosed. The cost is trust, not engineering |

**Which can be answered independently, with nobody else involved, today:**

- **`C-17`** — a product call, entirely yours.
- **`TD-42`** — a scope call, entirely yours.
- **`C-19`** — a disclosure call; the wording needs a lawyer but the *decision* does not.
- **`C-08`** — the **direction** is yours today; the "yes" branch then needs legal input on basis, retention
  and residency before the first row is written.

**Which need somebody else:**

- **`P-06`** — an accountant. Start this conversation early; it has the longest lead time of anything here.
- **`D-13`** — a lawyer, especially for option B.
- **`C-01`** — the providers themselves, and it should follow `D-13`.
- **`C-15`** — answerable alone, but worth confirming with whoever will do the bookkeeping.

## 10. Recommended order for answering

This order is chosen so that each answer unblocks the most work for the least effort, and so that the one
decision with a running cost is settled first.

| Order | Decision | Why here |
|---|---|---|
| **1** | **`C-08`** | The only one where delay destroys something. Answer it **even if the answer is no** |
| **2** | **`C-17`** | Pure product judgement, no external input, and it unblocks two phases |
| **3** | **`TD-42`** | The cheapest visible credibility fix, and it closes a stalled phase |
| **4** | **`C-19`** | Costs nothing, needs nobody, and only gets more awkward with time |
| **5** | **`P-06`** | **Start the accountant conversation now**, even though the answer arrives later — it has the longest lead time |
| **6** | **`C-15`** | Follows naturally once the tax conversation is under way |
| **7** | **`D-13`** | The heaviest, and it needs a lawyer. Everything about money downstream waits on it |
| **8** | **`C-01`** | **Must follow `D-13`** — whether you need a provider that can split depends entirely on that answer |

**Items 1–4 need nobody but you and could be answered this week.** Items 5–8 involve other people, which is
exactly why 5 should be *started* early even though it finishes late.

## 11. What happens after you answer

For each answer, engineering will: record it in
[OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) as a decision with its date, update the blocked-decision
list in [CommercialPlatformPlan.md](CommercialPlatformPlan.md), write an architecture decision record where the
answer changes how the system is built, and then implement the unblocked phase under the existing completion
protocol.

**If an answer is "no" or "none", that is recorded as a decision with its consequences listed** — not left as
an open question that quietly costs something every week. That rule exists specifically because of `C-08`.

## 12. Related

[OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) (the canonical register) ·
[CommercialPlatformPlan.md](CommercialPlatformPlan.md) (the phases these unblock) ·
[CommercialPlatformArchitecture.md](CommercialPlatformArchitecture.md) (the design) ·
[CommercialReadiness.md](CommercialReadiness.md) (what exists today) ·
[SouqMasterPlan.md](SouqMasterPlan.md) ·
[ProductRoadmap.md](ProductRoadmap.md) ·
[TechnicalDebt.md](TechnicalDebt.md) ·
[ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) ·
[Billing module](../04-MODULES/Billing/README.md) ·
[Payments module](../04-MODULES/Payments/README.md) ·
[Reporting module](../04-MODULES/Reporting/README.md) ·
[ADR-0050](../11-ADR/0050-behavioural-event-foundation.md) (behavioural events) ·
[ADR-0048](../11-ADR/0048-payment-provider-abstraction.md) (the payment port) ·
[ADR-0031](../11-ADR/0031-payments-and-refunds.md) (payments as built) ·
[ADR-0011](../11-ADR/0011-white-label-architecture.md) · [ADR-0035](../11-ADR/0035-white-label-runtime.md) (customisation limits) ·
[ADR-0014](../11-ADR/0014-money-precision.md) (money and minor units) ·
[ADR index](../11-ADR/README.md)
