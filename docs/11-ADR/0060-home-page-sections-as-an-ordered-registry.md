# ADR-0060: The home page is an ordered list of typed sections, not JSX

- **Status:** Accepted 2026-09-22, and **implemented by `C8`** on the same day, completing the phase. Supersedes nothing.
- **Date:** 2026-09-22
- **Related modules:** Platform, storefront
- **Related ADRs:** [ADR-0011](0011-white-label-architecture.md) (customization is configuration), [ADR-0052](0052-bounded-extension-model.md) (allowlists live in the Domain and are published through the options endpoint), [ADR-0059](0059-theme-presets-vary-form-not-colour.md) (the other half of `C8`: presets vary finish, this varies layout)

## Context

`Storefront.jsx` composed a fixed list: banner, featured product, new arrivals, offers, then the catalog — in that order, for every store on the platform. The section components themselves were already prop-driven and knew nothing about where they sat, which is to say they had been **renderers waiting for a descriptor list** since the day they were written.

[CommercialPlatformArchitecture.md §4.13](../12-ROADMAP/CommercialPlatformArchitecture.md) states the constraint this has to satisfy: *customization is configuration; anything else becomes a product feature for every tenant or is declined.* A merchant who wants offers above new arrivals must not cause a branch, a fork, or a per-tenant component.

## Problem

1. How does a merchant change the order and visibility of home-page sections without any per-tenant code?
2. What stops that from becoming an arbitrary, server-trusted list of component names?
3. What happens to existing stores, and to a store whose saved layout predates a new section type?

## Options considered

### A — A layout attribute per theme preset

**Rejected.** It couples two unrelated choices: a merchant who wants the `bold` finish would also be choosing someone else's section order. It also caps the space at three layouts forever.

### B — A free-form layout document (JSON blocks the merchant composes)

**Rejected.** It is the page-builder answer, and it fails the constraint above at the first step: the server would be storing and the client rendering structures nobody validated. It also invites arbitrary content, which [ADR-0011](0011-white-label-architecture.md) refuses, and it makes every future change to a section's props a migration over stored documents.

### C — A closed registry of section types, ordered and toggled

**Chosen.** The set of types is fixed in the Domain and published through the existing options endpoint, exactly as typography, theme presets and policy kinds already are. The merchant reorders and toggles; nothing else. A new section type is a product feature for every store, never a branch for one.

## Decision

### The types are a closed allowlist in the Domain

`StoreSections.Types` — `hero`, `featured`, `newArrivals`, `offers`, `catalog` — with the default order being **the page's existing order, character for character**. `StoreSettingsOptionsDto` publishes the list and which entries are required, so the editor never offers a choice the server would reject.

No component name ever arrives from a client. The server sends type keys; the storefront's registry maps a key to a renderer, and **a key it does not recognise is skipped**. That is deliberate: a frontend older than the server draws what it understands instead of dropping the whole page.

### Three rules, each for a failure that would otherwise be silent

- **An unknown type is refused, not ignored.** Ignoring it means a merchant saves a layout and gets a different one, with no error to explain the difference.
- **A duplicate type is refused.** "New arrivals" twice on one page is a mistake, not a preference.
- **The catalog cannot be turned off.** A home page without it has no products on it, and it is the first screen a visitor reaches. It can be moved anywhere; it cannot be removed. `StoreSections.Required` is a set rather than a special case, so a second mandatory section later is an entry, not a branch.

### Unmentioned types are appended disabled — and that is the interesting rule

A store that has saved a layout, and then a new section type ships, does **not** get that section on its home page. It is appended to the list turned off, for the merchant to enable.

This is the same principle `StoreModules` follows and for the same reason: no capability reaches a store by accident. The exception is a required type, which is appended *enabled* — a merchant whose older client omitted `catalog` must still have a catalog, and refusing their save would punish them for their client's age.

A store that has **never** configured sections is a different case entirely: it reads `StoreSections.Default`, the full list enabled. So the upgrade changes nothing for anybody.

### `sections` is absent-means-unchanged, unlike the rest of its contract

Every other field in `StoreSettingsInput` replaces what it finds — the editor always sends the whole document. `sections` deliberately does not: absent means *keep what is stored*.

The reason is a client older than the field. Under the replace rule, any save from a screen that does not know about sections would silently reset that store's layout to the default. A layout erased by saving an unrelated field is a defect, not a contract, and an integration test pins it.

### The storefront draws the server's answer, with two safety belts

`StoreSettingsDto` carries both `sections` (everything, including disabled, so the editor knows what can be turned on) and `enabledSections` (what to draw, in order, computed on the server — the frontend displays and does not decide).

The storefront keeps two belts anyway, because both cover a real window:

1. **Before the config arrives**, `enabledSections` is `undefined` and the page draws the default order. Drawing only the catalog and then popping the banner in is a flash every visitor would see. This is the same role `styles.css` plays for design tokens before branding arrives, and a test pins the frontend's fallback list against the Domain's.
2. **The catalog is drawn even if it is missing from the descriptor.** The server forbids its removal; a frontend that trusts only that would render a home page with no products if it ever received a short list.

Filtering still wins over everything: with a category, price, sort, page or search term in the URL, the page is a clean catalogue listing and the promotional sections are hidden, exactly as before this change.

### The editor reorders with two buttons, not drag-and-drop

Drag-and-drop needs a keyboard alternative and a screen-reader story before it is usable by everyone; a pair of move buttons with explicit accessible names (`Move Offers up`) works for every input from the start. The list is vertical, so there is no left/right to invert between Arabic and English. The ordinal is `aria-hidden`, because an `<ol>` already conveys position and saying it twice is noise.

## Consequences

**Good**

- A merchant reorders their home page, and no per-tenant code exists anywhere.
- Adding a section type is: a constant, an entry in `Types`, a renderer, and two translations.
- Nothing changed for any existing store, by construction.
- No migration: the settings are a JSON document, and an older document reads as the default.

**Costs, honestly**

- The default order now lives in two places — the Domain and the storefront's pre-config fallback. A test pins them together, which makes it safe rather than tidy.
- Sections have no per-store options (how many products a row shows, its title). That is a deliberate stopping point: those turn the registry into the layout document option B rejected, and no merchant has asked.
- `featured` and `newArrivals` still read the same queries they always did. Reordering does not re-source them, so a store could put two rows of the same products next to each other by enabling both with similar catalogues.

## Deliberately out of scope

- **Store-authored content sections.** That is `TD-42`'s authored-page half, deferred with a named trigger.
- **Per-section configuration**, as above.
- **Section order per theme preset**, which option A rejected.
