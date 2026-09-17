# ADR-0041: The shopper chooses a variant, and the storefront prices from what can be bought

- **Status:** Accepted, 2026-09-17. Implements V3 of [ProductVariants.md](../04-MODULES/Catalog/ProductVariants.md) and **removes the temporary storefront gate** of [ADR-0040](0040-product-option-model.md). Roadmap Phase 16 closes with it.
- **Date:** 2026-09-17
- **Related modules:** Catalog; Shopping; Ordering; Inventory; Notifications
- **Related ADRs:** carries out P-08b and P-08c from [ADR-0039](0039-product-variants-order-identity.md); removes the gate added by [ADR-0040](0040-product-option-model.md); reads stock per variant from [ADR-0026](0026-inventory-reservations.md); prices through the single pipeline of [ADR-0028](0028-basket-and-pricing-pipeline.md); freezes order lines per [ADR-0029](0029-orders-lifecycle.md); projections per [ADR-0008](0008-cqrs-strategy.md); error codes per [ADR-0017](0017-error-contract.md)

## Context

V1 made the order and checkout carry the variant; V2 let merchants define options and manage variants. Both were invisible to shoppers, and V2 deliberately kept a product with more than one active variant **out of the storefront**, because the storefront could only add to the basket by product id and would have failed with `VariantRequired`.

The owner's P-08 answers bind this phase: an explicit choice is required, sold-out options stay visible but disabled (P-08c), and a multi-variant product shows "**From** the lowest price among variants that can currently be purchased" (P-08b).

Two presentation questions were recorded as open in [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) and had to be answered before the picker could be built.

## Problem

1. **What does "purchasable" mean**, and which price does a list, a filter, a sort and structured data use?
2. **Are deactivated variants hidden from shoppers, or shown disabled?** (open question 1)
3. **Does a product whose variants are all sold out stay listed?** (open question 2)
4. **How does the picker keep the shopper from reaching a combination the server would refuse**, without ever silently changing a value they chose?
5. **What happens to a selection that a shared link, a reload, or a stock change has made stale?**
6. **Where does the variant label come from** in the cart (live) and on an order (frozen), and in which language?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Purchasable | **Active *and* available now** (`OnHand − Reserved > 0` for that variant's stock row) | Active alone: "From 20" for a size nobody can buy is the lie P-08b exists to prevent |
| The price a product shows | **The cheapest purchasable variant**, with `PriceIsFrom` when purchasable prices differ. With nothing purchasable, the cheapest **active** variant, and the product reads as unavailable | The default variant's price: it may be sold out or not the cheapest. An average or a range as the single number: neither is a price anyone can pay |
| Compare-at | **The same variant as the displayed price** | The best discount across variants: it would strike a price that doesn't belong to the shown one |
| Where the number is computed | **In SQL, in the read model** (`CatalogQueries`, `WishlistQueries`), so display, filter and sort agree by construction | In the frontend: prices would differ between the card and the sort, and the client would compute money ([ADR-0028](0028-basket-and-pricing-pipeline.md) forbids it) |
| Price filter | **One variant inside the whole range** — a bound satisfied by different variants is not a match. A product with nothing purchasable is matched by its active variants, so it stays in its price band while sold out | Independent bounds (today's shape): variant A satisfying the minimum and B the maximum would match an empty range |
| On-sale filter | **A purchasable variant with a compare-at above its price**, with the same fallback | Any variant: a discount only on a withdrawn or sold-out size is not an offer |
| **Deactivated variants (open question 1)** | **Hidden.** A deactivated variant is the merchant withdrawing it from sale, like unpublishing a product, which disappears from the storefront (BR-CAT-08/09). A value no shown variant uses disappears with it | Shown disabled: it advertises a size the store has decided not to sell, and it can never come back by itself. P-08c's "sold-out options stay visible" is about **stock**, which is temporary |
| **Everything sold out (open question 2)** | **Stays listed, marked unavailable** — exactly what an out-of-stock simple product does today (`StockQuantity = 0`, "sold out" badge), with the cheapest active variant's price. The default variant is always active, so "no active variant" cannot happen | Removing it: the page would 404 for a product the merchant still sells, losing links, search results and wishlist entries over a stock level |
| Preselection | **Nothing is preselected when there is a choice** (P-08c). The only variant of a product that has options *is* preselected: there is nothing to choose | Preselecting the cheapest or the default: the shopper buys a size they never picked |
| A value that can't be bought with the current selection | **Shown disabled with its reason** — `soldOut` when the combination exists but has no stock, `unavailable` when no variant combines it with the current selection. Never hidden, never auto-switched | Hiding it: the shopper can't see the store sells it in another size. Switching another option for them: the mission's "never silently switch" and a real risk of buying the wrong thing |
| Reaching an impossible combination | **Impossible by construction:** a value with no compatible variant is disabled, so the UI can never submit a combination the server would refuse. The server still re-validates every id | Letting the UI submit and relying on the error: a shopper should not meet a rejection they were led into |
| A stale `?variant=` | **Ignored, and removed from the URL.** A withdrawn or unknown id selects nothing; a sold-out but real variant is selected and reads as sold out | Selecting the nearest variant: an invented choice. Leaving the id in the URL: the link would keep re-proposing something that is gone |
| Stock changing after the page loaded | **The server refuses** (`InsufficientStock`), and the toast says so. The quantity is also clamped to the selected variant's availability before sending | Trusting the page's availability: overselling |
| The cart label | **Live, in the shopper's language** — the server sends the label per language, as it already does for product names | The store's default language only: a bilingual store would show the wrong language in the cart |
| The order and email label | **The frozen snapshot in the store's default language** ([ADR-0039](0039-product-variants-order-identity.md)) | Recomposing from today's options: renaming a value would rewrite history |
| Structured data | **`AggregateOffer` with `lowPrice`/`highPrice` over purchasable variants** when more than one can be bought; otherwise a single `Offer` whose availability follows what is actually purchasable | A price range over all variants: it would advertise a price that cannot be paid |
| A list card for a product that needs a choice | **A link to the product page** (`VariantChoiceRequired` from the server), not an add button | An add button: the server answers `VariantRequired` and the shopper meets an error for pressing the obvious thing |
| SKU on the storefront | **Not exposed** in `ProductDto`. The buyer still sees the SKU snapshot on their own order | Showing it on the product page: a warehouse code published for every visitor, which the storefront has never done |

## Decision

The options marked "Chosen" above. In particular:

- **`CatalogQueries.VisibleProducts` loses the single-active-variant condition**, and so does `WishlistQueries`. BR-CAT-24 is retired.
- **`ProductDto`** gains `PriceIsFrom`, `VariantChoiceRequired`, `Options` (with values) and `Variants` (active only, with `Available`). For a product without options, `Options` and `Variants` are `null` and the contract is byte-for-byte what it was before V3.
- **`WishlistItemDto`** gains `PriceIsFrom` and `VariantChoiceRequired` and prices the same way.
- **`PricedLine`/`BasketLineDto`** gain `VariantLabels` (per language) beside the store-language `VariantLabel`.
- **`EmailLine`** gains `Variant`, and the composer prints it after the product name, escaped.
- **Frontend:** `frontend/src/features/catalog/variantSelection.js` (the algorithm), `variantLabel.js` (the shared label rule, also used by the admin model), `VariantPicker.jsx`, and the product page, cards, cart, checkout summary, order pages and structured data.

## Consequences

- **Positive:**
  - A shopper can buy a size or colour: choose it, see its price, stock and label, and find it on the basket, the order and the email.
  - A card never advertises a price nobody can pay, and a filter, a sort and a card agree because one SQL expression produces all three.
  - The UI cannot offer a combination the server would refuse, and the server still refuses stale, foreign, withdrawn and out-of-stock ids.
  - Simple products are untouched: no picker, no label, the same contract and the same UX.
  - Roadmap Phase 16 (Storefront) closes.
- **Negative / limits:**
  - `ProductDto` for a product with options carries its variants and their exact availability, so a visitor can read per-variant stock. That is the same disclosure `StockQuantity` has always made (Catalog README, limitation 8).
  - The detail read adds two queries (options, variants) for products with options.
  - "From" hides the spread: a product whose sizes cost 20 and 200 shows "From 20". P-08b chose that.
  - A product with a discount only on a sold-out variant leaves the Offers page until it is restocked.
  - Variant images are still not built, so the gallery does not follow the selection.
  - Reporting still counts products, and the dashboard's stock figures count variants (V4).

## Revisit when

- The owner wants a different "From" presentation (a range, or the default variant's price).
- Variant images arrive: the gallery should follow the selection.
- A store needs the storefront to show a deactivated variant, or to hide a product that is entirely sold out — both are reversals of the two decisions above and belong to the owner.

## Verification

- **Frontend:**
  - `variantSelection.test.js` — 19 cases: the initial selection from a link, a stale id, no preselection when there is a choice, preselection of the only variant, narrowing without hiding, sold-out versus impossible, the purchase state and its blocked reasons, an all-sold-out product.
  - `ProductDetail.test.jsx` — the picker on the page: the explicit choice, a disabled sold-out value, the price/availability/URL following the selection, the variant id sent to the basket, a stale id cleared, an unreachable impossible combination, the quantity clamped, and a product without options unchanged.
  - `ProductCard.test.jsx`, `basketModel.test.js`, `structuredData.test.js`.
- **Integration (SQL Server), `StorefrontVariantTests`:**
  - the product contract, with the deactivated variant and its exclusive value absent;
  - "From" pricing that follows what can be bought, and the filter, sort and Offers agreeing with it;
  - selection → two basket lines with labels → order → payment → order and email;
  - every refused id: no choice, another product's variant, an unknown id, a sold-out variant, stock draining after load, a variant deactivated after load;
  - a simple product's contract and add-to-basket unchanged;
  - the wishlist priced from what can be bought.
  - `ProductOptionAdminTests` asserts the storefront now shows a multi-variant product.
- **Browser** (`frontend/e2e/storefront-variants.spec.js`, live stack): the six journeys listed in the commit, in English and in Arabic right-to-left with dark mode, at desktop and phone widths, axe-clean.

## Migration

None. V3 changes read models, contracts (additively) and the frontend; no schema change and no data movement.
