# ADR-0011: White-label architecture

- **Status:** Accepted, 2026-09-11. Backend in Phase 4, frontend runtime in Phase 15. Details: [WhiteLabel.md](../08-FRONTEND/WhiteLabel.md).
- **Date:** 2026-09-11
- **Related modules:** Platform (store settings and branding); Cross-cutting (frontend and email presentation)
- **Related ADRs:** backend implemented by [ADR-0024](0024-platform-administration.md) (settings, modules, contrast check); frontend runtime implemented by [ADR-0035](0035-white-label-runtime.md); branded localized email in [ADR-0034](0034-notifications-outbox.md); the host that selects the brand comes from [ADR-0006](0006-tenant-resolution.md)

## Context

- Branding ("Marka"), currency (JOD), contact details, and four palettes are hard-coded, and the palettes are picked by *visitors*.
- The product must serve many differently branded stores from one codebase.

## Problem

How can each tenant look and behave like its own brand without per-client code, builds, or forks?

## Options considered

1. **A build per client** (environment variables or branch per client). Simple at first; N builds and N deployments; drift. ❌
2. **Custom CSS/HTML injection per tenant.** Maximum flexibility; breaks upgrades; XSS risk. ❌
3. **Configuration-driven runtime:**
   - tenant settings (identity, branding, locale, currency, contact, SEO, domains, modules);
   - a public config endpoint resolved from the host;
   - semantic design tokens applied by a ThemeProvider;
   - a small registry of theme **presets** for layout variations. ✅

## Decision

**Option 3.**
- **Platform owner controls** what affects contracts and integrity: domains, currency (locked after the first order), plan, and modules.
- **Tenant admin controls** presentation and content after handover: name, logo, colours, fonts, contact, SEO, templates.
- Colours are validated for WCAG AA contrast.
- There is no arbitrary CSS or script.

## Why

- One build serves every tenant.
- Branding changes are data changes (instant, auditable).
- New looks are presets available to all tenants: a product feature, not a fork.

## Consequences

- Components must use semantic tokens only. A CI check forbids brand and currency literals (Phase 15).
- SEO for an SPA needs per-host `index.html` head injection (Phase 16).
- Emails and templates become per-tenant, per-language (Phase 14).

## Revisit when

- Clients pay for deeper customization that presets can't express. Consider sandboxed custom CSS as a premium tier, in a new ADR.
