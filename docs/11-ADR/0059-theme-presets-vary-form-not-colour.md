# ADR-0059: Theme presets vary form, never colour

- **Status:** Accepted 2026-09-22, and **implemented by `C8`** on the same day, closing `TD-65`. Supersedes nothing.
- **Date:** 2026-09-22
- **Related modules:** Platform (the allowlist), frontend design system
- **Related ADRs:** [ADR-0011](0011-white-label-architecture.md) and [ADR-0035](0035-white-label-runtime.md) (identity is the store's, delivered at runtime), [ADR-0052](0052-bounded-extension-model.md) (allowlists live in the Domain and are published through the options endpoint)

## Context

Since Phase 15 a store has picked one of `classic`, `minimal` or `bold`. The Domain allowlists the value, the settings document stores it, the storefront config carries it, and `applyStoreTheme` writes it onto `<html>` as `data-preset`.

No stylesheet ever selected on that attribute. `TD-65` recorded the consequence in the only terms that matter: a merchant evaluating the product tries the three presets, sees no difference, and concludes the feature is broken. The settings hint had been rewritten to admit it — *"Presets do not change the storefront layout yet."* An offered choice that does nothing is worse than no choice, because it also costs the product's credibility.

What was missing was never code. It was the answer to **what the three presets are**.

## Problem

1. What may a preset change, given that the merchant has already chosen the store's colours and fonts?
2. Where does a preset's definition live, when some design tokens are written inline at runtime and others come from the stylesheet?

## Decision

### A preset varies form, never colour or type family

**Colour and font belong to the merchant.** They are chosen in the same editor, derived per mode for contrast, and are the substance of a white-label product. A preset that imposed a palette would be competing with the store for its own identity.

So the three presets are three answers to one question — **how much does the interface assert itself?**

| | Surfaces | Radii | Heading weight | Display scale |
|---|---|---|---|---|
| `classic` | float on a soft, brand-tinted shadow | 8 / 14 / 20 | 600 | base |
| `minimal` | defined by a 1px hairline; nothing floats | 4 / 6 / 10 | 500 | smaller |
| `bold` | defined by a 2px rule; corners squared | 0 / 2 / 4 | 800 | larger |

`--radius-pill` is untouched by every preset. A pill is a functional shape — badges, chips, status markers — not a matter of taste, and a 2px "pill" is not a pill.

### The shadow becomes a ring, which is what makes this free

`minimal` and `bold` do not *remove* elevation; they **redefine `--shadow` as `0 0 0 1px var(--color-border)`**. Every surface in the product already writes `box-shadow: var(--shadow)`, so each one acquires a hairline instead of a drop shadow with **no component change anywhere**.

It also inherits dark mode for nothing. The ring is drawn with `--color-border`, which is already derived per mode, so a preset needs no second set of values for dark.

`--shadow-lg` keeps a real shadow underneath its ring in both presets: it is the elevation of dialogs and menus, and a surface floating above the whole page is lost against what is behind it if a line is all it has.

### The definition is split in two, deliberately, and a test holds the halves together

Most preset tokens — radii, heading weight, type scale — live in `styles.css`, because nothing writes them inline.

**The shadow cannot.** In light mode it is tinted with the store's primary colour (`rgba(primary, 0.08)`), so it is derived rather than constant, and `applyStoreTheme` writes it **inline** on `<html>` — where it beats any stylesheet rule. This is the same reason the theme *mode* is already an argument to `themeVariables` rather than a CSS override, and the preset now joins it as a third argument.

That split is a real cost, and the honest mitigation is not a comment. `styles.presets.test.js` reads the stylesheet, derives the runtime tokens, and asserts the two agree — that both halves turn shadow into a ring for the non-classic presets, that `classic` shifts nothing away from `:root`, that no preset touches a `--color-*` or `--font-*` token, and that no two presets render identically. It was mutation-checked in both directions: softening the ring in the sheet fails one test, and disabling the branch in the deriver fails three.

The precedent is explicit. `styles.darkTokens.test.js` exists because exactly this kind of split silently came apart once before and stayed apart.

### The selector is the bare attribute

`[data-preset='minimal']`, not `html[data-preset='minimal']`, so the **settings preview** picks it up too. The preview is a `<div>` carrying the attribute, and it is the one place a merchant compares the three before saving. A preset that changed nothing in the preview would reproduce the defect this record exists to close.

### Incidental fix

`themeVariables` emitted only `--shadow` and `--shadow-lg`; `--shadow-sm` and `--shadow-md` fell through to the stylesheet's light-mode values and stayed there in dark mode — a black shadow on a dark surface, which is no elevation at all. All four rungs are now derived per mode.

## Consequences

**Good**

- The choice does what it says, in the storefront and in the preview, and the hint no longer apologises for it.
- No component changed. The whole capability is token redefinition.
- Dark mode came free, because the ring reads a token that is already mode-aware.
- A fourth preset is a block of tokens and a string in `BrandPresets.Themes`.

**Costs, honestly**

- The definition lives in two files. The test makes that safe, not tidy.
- The presets differ in form only, so a merchant expecting three *layouts* will find three finishes. Layout variation is the section registry — the other half of `C8` — and it is not built.
- `bold`'s squared corners and heavy weight will not suit every catalogue. It is a choice offered, not a default.

## Deliberately out of scope

- **Custom CSS injection**, which [ADR-0011](0011-white-label-architecture.md) refuses and this does not reopen.
- **Layout switching by preset** — header style, card style, section order. Those need the descriptor list, not an attribute.
- **Per-preset colour.** Not a deferral: it is the one thing this record forbids.
