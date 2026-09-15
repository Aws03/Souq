# Souq: the visual design system

> **Status:** delivered. The tokens and the light mode came with the white-label runtime ([ADR-0035](../11-ADR/0035-white-label-runtime.md)); dark mode, the inverted panel, the product-card variants, the opening experience and the chart primitives came with the storefront-experience work described here.
>
> This document is the *design* half of the frontend documentation. The *mechanical* half — folder layout, data fetching, the token list — is [FrontendGuide.md](FrontendGuide.md); how one build serves many stores is [WhiteLabel.md](WhiteLabel.md).

## 1. The design language, stated as constraints

The product is a commercial platform a merchant pays for and a shopper buys from. That sets the register: **premium, familiar, restrained, modern.** Those four words are useful only if they exclude things, so:

| We do | We do not | Why |
|---|---|---|
| One accent colour, used for what the visitor should act on | Neon palettes, rainbow gradients | The merchant's identity is the colour story. A second one competes with it |
| Flat surfaces separated by a hairline border and a soft shadow | Glassmorphism, blurred translucency | Translucency costs a compositing layer per surface and fails first on the cheap phone the shopper actually holds |
| Motion that explains a change of state (a card lifts, an image settles) | Motion for its own sake, entrance animations on every section | Animation that carries no information is a tax on every visit after the first |
| Type scale, spacing scale, radius scale — all tokens | Ad-hoc pixel values in components | A scale is what makes a hundred screens look like one product |
| Empty states that say what to do next | Zeroes presented as a report | A dashboard of zeroes tells a new merchant nothing; §21 of this system is [Dashboards.md](../04-MODULES/Reporting/Dashboards.md) |

**The store is the brand, not Souq.** Nothing in the UI announces the platform to a shopper. The design system's job is to be a competent, quiet frame around whatever palette and typeface the merchant chose.

## 2. Tokens

Every component reads tokens; no component reads a store's settings directly. The tokens live in two places and the order matters:

1. `frontend/src/styles.css` — the `:root` block holds neutral defaults, and a `html[data-theme='dark']` block holds neutral dark defaults. These are what the browser paints **before** the store's configuration arrives.
2. `applyStoreTheme` in `frontend/src/app/storeTheme.js` writes the store's derived values as **inline custom properties on `<html>`**, which beat any stylesheet rule.

That ordering is the reason dark mode is an *input to derivation* rather than a CSS override: a rule like `[data-theme="dark"] { --color-bg: … }` in a stylesheet would lose to the inline value that `applyStoreTheme` writes. So `themeVariables(branding, mode)` in `frontend/src/app/tenantModel.js` takes the mode and returns a complete token set for it.

### 2.1 The token groups

| Group | Tokens | Derived from |
|---|---|---|
| Identity | `--color-primary`, `--color-primary-strong`, `--color-on-primary`, `--color-secondary`, `--color-accent`, `--color-accent-soft`, `--color-on-accent` | The store's palette, adjusted for the mode |
| Page | `--color-bg`, `--color-surface`, `--color-surface-alt`, `--color-text`, `--color-text-muted`, `--color-border` | The store's background and text in light mode; derived from a cool dark base in dark mode |
| Inverted panel | `--color-panel`, `--color-panel-strong`, `--color-on-panel`, `--color-accent-on-panel` | §2.3 |
| Status | `--color-success`, `--color-info`, `--color-danger` and their `-soft` variants | Fixed hues, re-derived per mode for contrast. **Not tenant-configurable**: a merchant must not be able to make "out of stock" look calm |
| Elevation | `--shadow`, `--shadow-sm`, `--shadow-md`, `--shadow-lg` | A black shadow in light mode; a light top edge plus a deeper shadow in dark mode, because a black shadow is invisible on a dark surface |
| Rhythm | `--space-1` … `--space-16`, `--radius`, `--radius-pill` | Fixed scale |
| Motion | `--motion-fast`, `--motion-base`, `--motion-slow`, `--ease-out`, `--ease-in-out` | Fixed scale |
| Typography | `--tenant-font-heading`, `--tenant-font-body`, the `--fs-*` scale | The store's typography preset |

### 2.2 Contrast is derived, not trusted

`BrandColors` in the backend already refuses a palette whose text fails 4.5:1 against its background. That check is made against the **light** palette the merchant entered. Dark mode transforms those colours, so the guarantee has to be re-established:

- `readableAgainst(colour, background, target)` lightens a colour step by step until it clears the ratio. A near-black brand colour that would vanish on a dark background is lightened until it reads, rather than left as a dark smudge.
- `mutedText` keeps secondary text at 4.5:1, not the 3:1 that "muted" usually degrades into.
- `readableOn` picks black or white for text sitting on a filled colour.

`frontend/src/app/tenantModel.themes.test.js` asserts these as ratios, in both modes, including for deliberately hostile palettes (a near-black brand colour, a near-white one).

### 2.3 The inverted panel, and the bug that produced it

The footer, the storefront hero, the announcement bar, the drawer header and the admin sidebar are **deliberately dark surfaces with light text**, in both modes. They used to be built from two general tokens: `--color-primary` as the background and `--color-bg` as the text.

That is correct in light mode and broken in dark mode, because both tokens flip at once — the identity colour is *lightened* so it stays readable against a dark page, and the page background *darkens*. The pairing inverts: a pale grey sidebar carrying dark text, with the store's name barely legible on it.

No contrast test caught it, because each token was individually fine. The defect lived in their pairing. So the pair became its own token: `--color-panel` / `--color-on-panel`, dark in both modes, tinted with the store's identity rather than a neutral grey — plus `--color-accent-on-panel`, which is lightened toward white because the panel it sits on is always dark.

**The rule this leaves behind:** when a surface's background and its text must move together, give them a token pair. A component that picks two unrelated tokens and hopes they stay compatible will be right in one mode.

## 3. Light and dark

| Question | Answer |
|---|---|
| Who decides? | The visitor's explicit choice wins; then the store's `themeMode` setting (`light`, `dark`, `system`); then the visitor's operating system. `resolveThemeMode` in `frontend/src/app/themeMode.js` is that precedence, and it is the only place it exists |
| Where is the visitor's choice stored? | `localStorage`, key `souq_theme`, per origin. Each store has its own host and therefore its own storage, so a choice in one store cannot leak into another. Every read and write is wrapped: a blocked storage means "no saved choice", not a broken page |
| Does the store's choice override the visitor's? | No. A merchant sets the default their brand wants; a visitor who reaches for the toggle has said something more specific |
| What happens before the config arrives? | The mode is written to `<html>` immediately from the visitor's stored choice, and `styles.css` carries neutral dark tokens for that window. Without this, a dark-mode visitor saw a white flash on every load — the page painted with light `:root` tokens until the network answered |
| Does the OS switching mid-session follow? | Yes, while the resolved source is the system: `TenantProvider` listens to `prefers-color-scheme` |

`color-scheme` is set on the root alongside `data-theme`, so the browser paints its own form controls and scrollbars to match.

## 4. Motion

Three durations and two easings, and one rule: **motion shows a change, it does not announce a page.**

- Hover and keyboard focus lift a product card by 3px and deepen its shadow. Focus gets the same treatment as hover on purpose — a keyboard user should see what a mouse user sees.
- A product image scales to 1.04 inside a clipped frame, so nothing around it moves.
- `prefers-reduced-motion: reduce` removes transitions globally in `styles.css`. That is not enough on its own: a `transform` is an end state, not a transition, so a card would *jump* instead of sliding. Each component that transforms therefore also neutralises the transform under the same query and substitutes a non-moving signal — the card gets an accent outline instead of a lift.

## 5. Product presentation

A grid of identical cards treats every product as equally important, which is the same as saying none of them is. The card is one component, `frontend/src/components/product/ProductCard.jsx`, with four layouts:

| Layout | Shape | Used for |
|---|---|---|
| `grid` | Square image above the details | The catalogue, the default |
| `list` | Image beside the details, image width `clamp(96px, 30%, 180px)` | The catalogue's list view, and the three companions in the featured block |
| `compact` | Small type, hairline border, no add button | Narrow rails |
| `featured` | Horizontal above 861px, image at 55% of the card, larger name | The one product a store leads with |

All four share the price, availability, wishlist and add-to-cart behaviour, because they are one component with different CSS — not four cards that will drift apart.

`frontend/src/components/store/FeaturedProduct.jsx` composes the editorial block: one `featured` card at two thirds of the width beside three `list` cards at one third. It renders only when the store has at least two products, because a lead with nothing beside it is not a lead.

**A warning written from experience.** The featured card is a flex *row* on wide screens. Its first version removed the image's `aspect-ratio`, which left the image with no intrinsic height; the row then collapsed to the height of its text, and the section rendered as a thin strip beside a tall column and a large white void. If a flex row's height should come from an image, that image needs a definite size — a ratio or an explicit height.

## 6. Images

`ProductImage` handles the states a real catalogue has: no image, a broken URL, a slow one.

- A missing or failed image falls back to a neutral placeholder inside the same box, so the layout never shifts.
- Images are `loading="lazy"` and `decoding="async"` by default; the featured product's image is `priority`, which makes it eager with `fetchPriority="high"` — it is usually the largest element on the first screen.
- Images fade in when they decode, which hides the paint rather than drawing attention to it.
- Every media box has a fixed aspect ratio, so cards align whatever proportions the merchant uploads.

## 7. The opening experience

A store may open with a short reveal — a signature moment for a merchant who wants one. The rules are in `frontend/src/features/storefront/openingExperience.js`, as pure logic, because the interesting part is *whether* it plays.

It plays only when **all** of these hold:

1. The store enabled it (`branding.opening.enabled`, default **off**). A store that asked for nothing opens immediately.
2. The visitor has not seen it this session (`sessionStorage`). A flourish on the first visit is a toll on the fifth. Session-scoped, not permanent: a new tab next week is a new first impression, a click through the site is not.
3. The visitor has not asked for reduced motion. There is no "lighter" version — motion is exactly what was asked to be reduced.
4. The visitor arrived at the storefront root, not a deep link. Someone opening a product link wants the product.
5. The user agent is not a crawler. The regular expression is deliberately coarse; its errors fall on the safe side (no reveal).

It is 1400 ms, it can be skipped by a real focusable button, and the decorative panels are `aria-hidden` while the skip button is not. Getting that wrong — hiding the whole container from the accessibility tree — is what the component's own test caught first.

## 8. Charts

There is no charting library. `LineChart`, `BarList` and `StatusDonut` are about 300 lines of SVG over the pure scale functions in `frontend/src/features/reporting/chartScales.js`. The reasoning is in [Dashboards.md](../04-MODULES/Reporting/Dashboards.md) §6; the design consequences are:

- **Every chart is wrapped in `ChartFrame`**, which owns the four states (loading, error, no data, content) so no dashboard invents a fifth.
- **The drawing is `aria-hidden` and is paired with a visually hidden `<figure>` containing a real `<table>`** of the same numbers. A screen reader reads the figures, not "graphic". This is also what saves the chart when fonts fail or high-contrast mode replaces the palette.
- **Right-to-left is a mirror of the plot only.** The SVG is flipped with `transform: scaleX(-1)` so time runs right to left; the readout text sits outside the SVG so it is not mirrored with it.

## 9. Right-to-left is a layout property, not a translation

Arabic and English are not two string tables over one layout. The rules:

- **No physical direction in CSS.** `inset-inline-start`, `margin-inline-end`, `border-inline-start` — never `left`/`right`. `frontend/src/rtl.test.js` scans every stylesheet and fails on a physical property, with a short list of verified exceptions.
- **No compensating in JavaScript.** Components used to flip an icon by reading the language. They no longer do; the icon itself is direction-aware (`ChevronIcon` reads `document.documentElement.dir` and rotates accordingly), so callers say "start" and "end" and stop thinking about it.
- **Percentage offsets measured from a physical edge are a trap.** `inset-inline-start: 80px` with `transform: translateX(-50%)` centres a label in LTR and pushes it off-centre in RTL, because the translate is physical and the inset is logical. Size the box and centre its text instead.
- **Names from store data need bidirectional isolation.** An Arabic product name inside an English sentence makes the adjacent punctuation slide to the wrong end: `(كوب حراري).` renders as `.(كوب حراري)`. The `bidi` formatter in `frontend/src/i18n/index.js` wraps such values in `U+2068`/`U+2069`, and the translation string says `{{name, bidi}}` so the translator can see that the slot takes foreign text. `frontend/src/i18n/bidi.test.js` fails if a new key forgets it.
- **Plurals are a property of the language.** English has two forms, Arabic has six under CLDR. `frontend/src/i18n/translationKeys.test.js` requires both files to agree on *which keys are plural* and each file to carry the forms its own rule needs — `منتج(ات)` and `1 orders` were both symptoms of one missing rule.

## 10. Accessibility, as implemented

| Concern | What is done |
|---|---|
| Contrast | Derived and asserted, in both modes, for body text, muted text, text on filled colours, and text on the inverted panel (§2.2) |
| Keyboard | Focus receives the same visual treatment as hover; the opening reveal's skip button takes focus; drawers trap and restore it |
| Screen readers | Charts have table alternatives; decorative SVG is `aria-hidden`; toggles carry `aria-pressed`; alerts use `role="alert"` |
| Motion | `prefers-reduced-motion` removes both the transitions and the transforms (§4) |
| Structure | One `<h1>` per page, sections labelled, the range picker is a `role="group"` with a label |
| Automated checks | `frontend/src/a11y.test.jsx` runs axe-core over rendered pages |

Automated checks find perhaps a third of real accessibility defects. The rest came from opening the pages.

## 11. Responsive

The breakpoints are 560px, 767px and 861px, and management UI is the harder half:

- The admin sidebar becomes a bottom tab bar under 767px. With eleven sections it no longer divides into a phone's width, so it scrolls horizontally with a minimum tab width instead of truncating labels to three letters.
- The sidebar is `100vh` and its nav scrolls inside that, so the log-out control cannot be pushed off a short screen.
- Wide tables scroll inside their own container; the page itself must never scroll horizontally.
- `frontend/e2e/responsive.spec.js` runs against a real phone viewport and asserts exactly that: no page-level horizontal scroll, full labels, every section reachable, touch targets at least 40px, tables scrolling in their container, charts inside the viewport.

## 12. Performance budget

| Measure | Value | How it was measured |
|---|---|---|
| First load, Arabic | 136.5 kB gzip over 17 files | Production build served by `vite preview`, all JS/CSS/HTML responses gzip-measured |
| First load, English | 134.2 kB gzip | Same |
| Manager dashboard | 1 reporting request, 2.4 kB, page rendered in ~460 ms | Dev stack against SQL Server, timed from navigation to the last panel |
| Business overview | 1 reporting request, 1.0 kB, ~290 ms | Same |

Two decisions produced those numbers:

1. **No chart library.** The candidates cost roughly 90 kB gzip — about two thirds of the entire first load — to draw three chart types.
2. **One translation bundle per visitor.** Both languages used to be imported statically, which put 154 kB (53 kB gzip) of text into the first chunk — larger than React DOM, and half of it a language the visitor cannot read. They are dynamic imports now: the visitor's language at boot, the other only if they switch.

The measurements above include React StrictMode's duplicate effects in development (the storefront config and the unread count each appear twice); production mounts effects once.

## 13. What this system does not have

- **No component gallery.** There is no Storybook and no visual-regression suite. Changes are verified in a real browser and by the checks in `frontend/e2e`.
- **No theme editor.** A store's colours are set through the settings API; a visual branding editor is *planned*.
- **No layout switching by theme preset.** `data-preset` is on `<html>` and no stylesheet reads it yet ([WhiteLabel.md](WhiteLabel.md) §4).
- **No design tokens shared with e-mail.** `EmailComposer` carries its own inline styles, because an e-mail client cannot read custom properties.
