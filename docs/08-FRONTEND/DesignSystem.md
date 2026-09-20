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
- Text colours are derived against **the harder surface of the mode**: the page background in light mode (cards are white, lighter than it), the card surface in dark mode (cards are raised with light, so lighter than the background). Deriving against the background alone left muted text and brand-coloured links under 4.5:1 on dark cards.
- Text on the accent colour is `--color-on-accent`. `Button`'s accent variant used `--color-primary-strong`, which is dark only in light mode.
- Status colours are made readable against **their own soft surface**, not the page background. A badge is status text on its soft tint; the tint is darker than the background in light mode and lighter in dark mode, so text readable on the tint is readable on the background too. The earlier derivation checked the background only, and green on its badge measured 4.20:1.
- A highlighted row on the inverted panel keeps `--color-on-panel` for its text and uses the accent only for its edge. Accent text on a lightened overlay fell to 3.39:1, because `--color-accent-on-panel` is computed against the panel, not against what is drawn over it.

`frontend/src/app/tenantModel.themes.test.js` asserts these as ratios, in both modes, including for deliberately hostile palettes (a near-black brand colour, a near-white one).

### 2.3 The inverted panel, and the bug that produced it

The footer, the storefront hero, the announcement bar, the drawer header and the admin sidebar are **deliberately dark surfaces with light text**, in both modes. They used to be built from two general tokens: `--color-primary` as the background and `--color-bg` as the text.

That is correct in light mode and broken in dark mode, because both tokens flip at once — the identity colour is *lightened* so it stays readable against a dark page, and the page background *darkens*. The pairing inverts: a pale grey sidebar carrying dark text, with the store's name barely legible on it.

No contrast test caught it, because each token was individually fine. The defect lived in their pairing. So the pair became its own token: `--color-panel` / `--color-on-panel`, dark in both modes, tinted with the store's identity rather than a neutral grey — plus `--color-accent-on-panel`, which is lightened toward white because the panel it sits on is always dark.

**The rule this leaves behind:** when a surface's background and its text must move together, give them a token pair. A component that picks two unrelated tokens and hopes they stay compatible will be right in one mode.

## 2.1 Status tones — five, defined once (M10)

Every badge in the product — storefront, admin and platform — reads its colour from one place.
`frontend/src/features/statusTone.js` maps a domain status to a tone; `StatusBadge` turns a tone into
pixels. There is no third place, and a screen cannot invent a sixth tone.

| Tone | Pair | What wears it |
|---|---|---|
| `success` | `--color-success` on `--color-success-soft` | delivered, succeeded, approved, active, in stock, done |
| `info` | `--color-info` on `--color-info-soft` | paid, shipped, provisioning, needs attention |
| `warning` | `--color-warning` on `--color-warning-soft` | pending, draft, reserved, invited, low-ish stock |
| `danger` | `--color-danger` on `--color-danger-soft` | cancelled, failed, rejected, archived, blocked, out of stock |
| `neutral` | `--color-text-muted` on `--color-surface-alt` | blocked-by-something, unknown — **and it was the member missing entirely before M10**, so every status was forced into a colour that meant something |

- **What TD-28 actually was.** Not five maps: nine named maps, eight inline ternaries and six implicit
  `status.toLowerCase()` lookups, across four CSS modules — three of them byte-identical copies of the same
  five rules, and a fourth with the same five names at two different values because a contrast fix had been
  applied to only one. Twenty-two classes for five tones. The drift was visible: `Succeeded` was blue for a
  payment and green for a refund **in the same drawer**; `invited` was amber on one screen and blue on another
  **from the same helper**; "enabled" was blue on two tables and green on two others.
- **Unifying is not redesigning.** M10 fixed every case where two screens gave different answers for the same
  value — those are defects by definition — and left every self-consistent status alone. The four that are
  defensible but unexamined (`Draft`, `Released`, payment `Cancelled`, and the Stripe key-mode badge, which is
  not a domain status at all) are TD-54, with the argument against each written at its line.
- **A tone must derive per theme, or it is not a tone.** The amber had no token: two hex literals, so it never
  followed dark mode — a near-white pill on a dark surface. `--color-warning` and `--color-warning-soft` now
  join the other three and are derived by `tenantModel` per tenant *and* per mode, with the foreground chosen
  by measured contrast rather than by eye. Verified live: the pair moves from `#8A5A0E on #EDE4D6` to
  `#AD8C56 on #28261C` between modes.

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
| Labels | `FormField` gives its label and message to the control it wraps, including `PasswordInput` |
| Automated checks | `frontend/src/a11y.test.jsx` runs axe-core over rendered pages; `frontend/e2e/store-administration.spec.js`, `frontend/e2e/platform-provisioning.spec.js` and `frontend/e2e/back-office.spec.js` run it in a real browser, the latter in dark mode too, where colour contrast can actually be measured |

Automated checks find perhaps a third of real accessibility defects. The rest came from opening the pages.

## 11. Responsive

The breakpoints are 560px, 767px, 861px and **960px**, and management UI is the harder half:

- **960px was added by M4, from a measurement rather than a preference.** A signed-in storefront navbar needs **941px** in English (brand + search + "My orders" + "Account" + the greeting + "Log out" + five icon buttons), but the only rule that thinned it out was `max-width: 767px`. Between 768px and ~940px the navbar's `overflow-x: hidden` — a safety net against spilling — was silently **clipping the cart, wishlist and notification buttons**: no horizontal scrollbar revealed it, the controls were simply not there. Below 960px the text links now move into the menu the hamburger opens (where they already live on phones) and the search box and icon actions stay. A clipped control is worse than a visible overflow, because nothing points at it.

- The admin sidebar becomes a bottom tab bar under 767px. With fourteen entries in `ADMIN_NAV` (fewer for an account whose permissions or store modules hide some) it no longer divides into a phone's width, so it scrolls horizontally with a minimum tab width instead of truncating labels to three letters.
- The sidebar is `100vh` and its nav scrolls inside that, so the log-out control cannot be pushed off a short screen.
- Wide tables scroll inside their own container; the page itself must never scroll horizontally.
- `frontend/e2e/responsive.spec.js` runs against a real phone viewport and asserts exactly that: no page-level horizontal scroll, full labels, every section reachable, touch targets at least 40px, tables scrolling in their container, charts inside the viewport.
- **The 40px rule covers two controls, and M7 added the second.** Until M7 the only target this file measured was the bottom tab bar, while the sentence above read as though it covered every target. The one it did not cover was `RowActionsMenu`'s trigger — a flat `32px` square with no media query — which is the **only** way to act on a table row anywhere in the admin area: view an order, refund a payment, correct stock. It was found on the inventory screen at Pixel 7 width during M7's browser pass and fixed in the shared component, so every admin table gained it at once, with a second assertion in `responsive.spec.js` that walks inventory, products and orders.
- **The query is `@media (pointer: coarse)`, not a width.** A target's size should follow the *input device*, not the window: a narrow desktop window is driven by a precise mouse and needs no enlargement, while a large tablet is touched by a finger and does. Width only guesses at that; `pointer: coarse` asks directly — which is also why the assertion lives in the `phone` project, the only one that emulates a real device and therefore the only one where the query applies at all. (`ProductZoom` already used the sibling query `(hover: none)` for the same reason.)
- **A responsive test must not be able to pass vacuously.** The touch-target assertion skips a table that has no rows — a demo store may hold no orders yet — but then asserts that **at least one** table was measured, because "no rows found" is not "all targets are fine". The same pass also found that the platform-area phone test had hard-coded `http://admin.localhost:5173`, so it could never run against the container stack at all; it now derives the platform origin from `baseURL`.
- **The dark-mode sweep had `color-contrast` switched off, and four real defects were behind it (M10).** It is
  the only axe pass that visits every route in dark mode, so it was the only thing that could catch a colour
  not following the theme — and the rule it disabled was exactly that one. Turning it on found: the **store's
  own wordmark at 2.15:1 on every page** (the accent was readability-checked in dark mode and used raw in
  light); secondary text at 4.42:1 on `--color-surface-alt`, which is *darker* than the background it was
  measured against — the same mistake already corrected for status colours in `tenantModel.js`, never
  corrected for muted text; the 404 code, because a colour readable on white is not readable on a slightly
  darker surface, so the reference is now the hardest surface rather than the lightest; and the product "New"
  badge at **1.06:1 in dark mode**, invisible, because it took its text from the brand colour while sitting on
  the accent. All four are fixed and the full matrix is clean with the rule enabled. **The lesson is about
  guards, not colours:** a rule disabled in the only sweep that can exercise it converts a red test into a
  green one and changes nothing else.
- **Accent as text needs its own token.** `--color-accent` is the merchant's colour and stays exactly as
  chosen for borders, focus rings, chart strokes and button fills. `--color-accent-on-surface` is the readable
  derivation for when it is *text* — the same shape as the existing `--color-accent-on-panel`. Borders keep the
  raw accent: non-text contrast is a different rule.
- `frontend/e2e/responsive-storefront.spec.js` (M4) covers the other axis — **breadth instead of depth**: every storefront route, at 320/768/1280/2560, in both languages, asserting no page-level horizontal scroll, no interactive control outside the viewport, and no target under 24px (WCAG 2.5.8); plus axe across every route in both languages and both themes. Every defect it was written for — the navbar clipping above, a checkout grid track that could not shrink below its content so "Continue to payment" left the viewport, a five-column cart row that did not fit 320px, and 16×19px remove / 22×22px rating-star targets — appeared at **one** width in **one** language. The matrix is the test; a single viewport would have found none of them.

## 12. Performance budget

This section is the canonical place for first-load size; other pages link here rather than repeat the figures.

**M16 re-measured and the budget is now enforced in CI** (`frontend/scripts/bundle-budget.mjs`, run after the build). It measures the way the figures below were measured — a real browser loading the built app, summing what crossed the network gzipped — not by adding up `dist`, which counts lazily-loaded admin and platform chunks no visitor fetches. Both languages are measured, because the translation bundle is imported dynamically for the visitor's language alone and measuring one hides growth in the other.

| Measurement | Result | Method |
|---|---|---|
| **First load, Arabic (M16)** | **156.8 kB gzip over 19 files** | The built app in a real browser, `encodedBodySize` summed over the document, scripts and styles |
| **First load, English (M16)** | **152.7 kB gzip** | Same |
| **Budget** | **170 kB gzip**, CI fails above it | About 8% headroom — tight on purpose, because what it exists to catch is a step change, not a feature |

First load grew about 20 kB from the 136.5 kB below, across the phases that came after that measurement (M3's search bar and suggestions, M10's badges, M13's tabs). There is no single culprit, and that is exactly what a budget is for: growth nobody notices because every individual step is small.

**One thing M16 measured and deliberately did *not* change.** The icon set is a single module and the whole of it — 24 kB gzip, 15% of first load — ships to every visitor, because admin chunks import from the same barrel. That looks like an obvious win until it is measured: only **8 of 45 icons are admin-only, about 21% of the source**, so splitting the barrel would recover roughly 5 kB of 157. That does not justify touching the import in dozens of files, and the measurement is what says so rather than intuition.

The figures below are the previous measurement, kept for comparison.

| Measure | Value | How it was measured |
|---|---|---|
| First load, before this work | 142.1 kB gzip over 17 files, identical in both languages | Production build served by `vite preview`, every JS/CSS/HTML response of a cold load measured gzipped |
| First load, Arabic | **136.5 kB gzip** | Same method |
| First load, English | **134.2 kB gzip** | Same method |
| Manager dashboard | 1 reporting request, 2.4 kB, page rendered in ~460 ms | Dev stack against SQL Server, timed from navigation to the last panel |
| Business overview | 1 reporting request, 1.0 kB, ~290 ms | Same |

Two decisions produced those numbers:

1. **No chart library.** The candidates cost roughly 90 kB gzip — about two thirds of the entire first load — to draw three chart types.
2. **One translation bundle per visitor.** Both languages used to be imported statically, which put 154 kB (53 kB gzip) of text into the first chunk — larger than React DOM, and half of it a language the visitor cannot read. They are dynamic imports now: the visitor's language at boot, the other only if they switch.

The net is that the first load **fell** while this work added dark mode, three chart primitives, two dashboards, four card layouts and the opening reveal.

The measurements above include React StrictMode's duplicate effects in development (the storefront config and the unread count each appear twice); production mounts effects once.

## 13. What this system does not have

- **No component gallery.** There is no Storybook and no visual-regression suite. Changes are verified in a real browser and by the checks in `frontend/e2e`.
- **No free-form theme editor.** Branding is edited in `/admin/settings` (and by the platform in `/platform/stores/:id/settings`) through `StoreSettingsEditor`, with readability checks as the merchant types and a live preview (`StorePreview`) in both modes; colours are entered, and there are no selectable colour presets or custom CSS.
- **No layout switching by theme preset.** `data-preset` is on `<html>` and no stylesheet reads it yet ([WhiteLabel.md](WhiteLabel.md) §4).
- **No design tokens shared with e-mail.** `EmailComposer` carries its own inline styles, because an e-mail client cannot read custom properties.
