# ADR-0016: Upload validation and media serving

- **Status:** Accepted and implemented in Phase 1A, 2026-09-11
- **Date:** 2026-09-11
- **Related modules:** Catalog (product media); Platform (branding uploads); Cross-cutting (the file storage port and the static file server)
- **Related ADRs:** storage keys become tenant-prefixed in [ADR-0022](0022-tenancy-enforcement.md); the product gallery and its endpoints arrive with [ADR-0025](0025-catalog-model.md); branding uploads reuse the same port in [ADR-0024](0024-platform-administration.md); rejections use the error shape of [ADR-0017](0017-error-contract.md)

## Context

Phase 0 finding B3:
- Uploads were accepted based on the client-supplied `Content-Type`.
- The stored file kept the client's extension.
- An `x.html` labelled `image/png` would be stored as `<guid>.html` and served from our origin, which is **stored XSS** able to read the JWT from `localStorage`.

Today only the single admin can upload. With tenant admins (Phase 2+), a malicious or compromised tenant admin could attack customers or the platform owner. Separately, `IFileStorage` and `IVideoStorage` were two identical implementations.

## Problem

How do we accept product images and videos safely, with one storage abstraction?

## Options considered

- **Trust the headers** (status quo). ❌
- **Allowlist extensions from the filename.** The client controls it. ❌
- **Detect the type from the file's magic bytes; choose the stored extension from the detected type; lock down the static file server.** ✅
- **Re-encode every image** (strips metadata, neutralizes polyglots). Strongest, but it needs an imaging dependency and CPU. Deferred to Phase 5 / media processing.

## Decision

1. `MediaFileInspector` (Application) detects:
   - **JPEG, PNG, GIF, WebP** (images);
   - **MP4 (ISO BMFF `ftyp`), WebM (EBML)** (videos)

   from the first bytes. Anything else, including HTML, SVG, and scripts, is rejected with `UnsupportedMediaType` (400).
2. The use case enforces **size limits** (images 5 MB, videos 50 MB). The controller keeps the HTTP-level request-size ceiling.
3. **The storage adapter never sees the client filename.** It receives the detected extension and saves `<guid><ext>` under a category folder. `IVideoStorage` is merged into `IFileStorage` (`SaveAsync(content, category, extension)`).
4. **The static file server for `/uploads`:**
   - serves **only** the allowlisted media extensions (a custom content-type map);
   - adds `X-Content-Type-Options: nosniff` and `Content-Security-Policy: default-src 'none'; sandbox`.

## Why

- Content sniffing plus a server-chosen extension closes the attack at its source.
- The locked-down static server is defence in depth: even a file that slips in by other means can't execute as a page on our origin.

## Consequences

- Previously uploaded files with non-media extensions are no longer served, which is intended.
- Tenant-prefixed keys and cloud blob storage arrive in Phase 5.

## Revisit when

- Adding new media types (e.g. PDF manuals). Each needs a signature and serving rules, and PDFs need a download disposition.
- A media-processing pipeline is introduced (re-encoding).
