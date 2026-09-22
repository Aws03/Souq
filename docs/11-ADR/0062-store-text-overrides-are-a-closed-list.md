# ADR-0062: A store may rename display text, from a closed list

- **Status:** Accepted 2026-09-22, implemented the same day, completing `C8`. Supersedes nothing.
- **Date:** 2026-09-22
- **Related modules:** Platform, storefront
- **Related ADRs:** [ADR-0011](0011-white-label-architecture.md) (customization is configuration, never a fork), [ADR-0060](0060-home-page-sections-as-an-ordered-registry.md) and [ADR-0059](0059-theme-presets-vary-form-not-colour.md) (`C8`'s other halves), [ADR-0052](0052-bounded-extension-model.md) (allowlists live in the Domain, published through the options endpoint)

## Context

[CommercialPlatformArchitecture.md §4.13](../12-ROADMAP/CommercialPlatformArchitecture.md) lists per-store string overrides as the last of the four "customization without forks" items. It is a real request for a white-label product: a perfume shop says *our collections* rather than *new arrivals*; a wholesaler says *order* rather than *basket*.

The mechanism is nearly free — i18next merges resource bundles, and the storefront already fetches a per-store configuration at boot. The design question is not *how*. It is **which strings**.

## Problem

1. Which translation keys may a merchant rewrite?
2. Where does the override apply, given that the language bundle is loaded lazily and reloaded on every language switch?

## Options considered

### A — Any key

**Rejected, and it is the option that looks most generous.** The translation file holds far more than display text: error messages, what the platform says on its own behalf, and accessibility strings that only a screen reader ever reads. A merchant who rewrites *"payment could not be completed"*, or empties the label of a button only a blind shopper hears, has not customised their store — they have broken it, possibly for the person least able to work around it.

### B — A prefix convention (anything under `store.*`)

**Rejected.** It reads as a rule but is enforced by naming discipline: the first time a genuinely load-bearing string is filed under a permitted prefix, it becomes overridable silently, and nothing fails.

### C — A closed list in the Domain

**Chosen.** Nine display-only keys today — section titles and calls to action. Extending it is a reviewed decision with a diff, not a line that slips through, which is exactly what makes it a list rather than a pattern. An architecture-level test also asserts the list contains no `errors.*`, no `storeClosed.*`, and nothing that looks like an accessibility label, so widening it carelessly fails.

## Decision

### The allowlist lives in the Domain and is published

`StoreTextOverrides.Allowed`, alongside `BrandPresets.Themes` and `StoreSections.Types`, and published through the same options endpoint so the editor can never offer a key the server would refuse. An unknown key is **refused, not ignored** — ignoring it means a merchant saved a rename that never appeared, with nothing to explain why.

Values reuse `LocalizedText.Normalize`: supported cultures only, 120 characters, and **empty means delete**, so clearing a rename restores the original string rather than leaving a button with no word on it.

### The override is a layer, re-applied whenever the bundle is (re)loaded

This is the part that is easy to get wrong, and the failure is ordering rather than logic. The language bundle loads lazily and **reloads on every language switch**, and `addResourceBundle` writes over whatever was layered on top. An override applied once at boot therefore disappears the moment the shopper presses the language toggle.

So the overrides are held in the i18n module and re-applied on three paths: when a bundle first loads, when the store configuration arrives, and after every language change. A test reproduces the failure directly — it reloads the bundle, asserts the original string is back, then re-applies and asserts the rename returns.

Keys are dotted, and each is expanded into its tree before merging, so renaming `store.newArrivals` does not replace the rest of `store.*`.

### No migration

The settings are a JSON document; a document written before this field reads as no overrides, so no existing store's wording changes.

## Consequences

**Good**

- A merchant can speak in their own words without a fork, a branch or a deployment.
- Nothing safety-bearing can be rewritten, and that is enforced rather than documented.
- A rename survives a language switch, which is where the naive implementation fails.

**Costs, honestly**

- Nine keys is deliberately conservative, and the first merchant request will probably be for a tenth. That is a small reviewed change, which is the point.
- The overrides live on the settings document, so a store with many renames carries them in every storefront config response. At 120 characters and a bounded key list this is small, but it is not free.
- **There is no editor for this yet.** The capability is complete through the API and the storefront; a merchant cannot set a rename from a screen. That is the honest state, and the next visible-value change in this area.

## Deliberately out of scope

- **Arbitrary copy editing** of the storefront, which is the authored-page capability `TD-42` defers with a named trigger.
- **Overriding platform-side text.** These are store settings; what the platform says on its own behalf is not a store's to rewrite.
- **Per-store translation of product data**, which already has its own per-language fields.
