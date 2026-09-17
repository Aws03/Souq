# ADR-0042: A local, deterministic, bilingual catalog search engine — normalized text in SQL Server, not a search service

- **Status:** Accepted, 2026-09-18. Implements M3 of [SouqMasterPlan.md](../12-ROADMAP/SouqMasterPlan.md). Supersedes nothing; it replaces the unranked substring match that [ADR-0008](0008-cqrs-strategy.md) left in `ICatalogQueries`.
- **Date:** 2026-09-18
- **Related modules:** Catalog; Platform (store default culture)
- **Related ADRs:** extends the read port and paging of [ADR-0008](0008-cqrs-strategy.md); keeps the per-language text model of [ADR-0025](0025-catalog-model.md) (D-10); ranks on the purchasable price rule of [ADR-0041](0041-storefront-variant-selection.md) so a card, a sort and a search hit never disagree; same-store keys and query filter per [ADR-0022](0022-tenancy-enforcement.md); no new runtime dependency per [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) §12

## Context

Before M3, storefront search was one expression in `CatalogQueries.SearchProductsAsync`:

```csharp
query = query.Where(p => p.Translations.Any(t =>
    t.Name.Contains(keyword) || (t.Description != null && t.Description.Contains(keyword))));
```

One contiguous substring, matched against raw stored text, with no ranking. Three consequences, all real:

1. **Arabic barely worked.** `مكنسه` did not find `مَكْنَسَة`. Diacritics, tatweel, and the alef/ta-marbuta/alef-maksura variants are separate Unicode code points, not accents, so **no database collation folds them** — and the collation was never pinned anyway (measured: the server default `SQL_Latin1_General_CP1_CI_AS`, case-insensitive and accent-sensitive). Two spellings of the same word were two different words.
2. **Multi-word queries mostly failed.** `مكنسة سامسونج` could not find *مكنسة كهربائية سامسونج*, because the whole query had to appear contiguously.
3. **No ranking.** Results came back newest-first, so an exact name match could sit on page four.

[SouqMasterPlan.md](../12-ROADMAP/SouqMasterPlan.md) M3 named SQL Server Full-Text Search as the likely index, and explicitly required verifying that the actual image ships it before committing.

## Problem

1. Is SQL Server Full-Text Search actually available in this repository's SQL Server, and is it the right fit?
2. Where does text normalization live — the database, or application code?
3. How is a multi-word query matched, and what makes one result rank above another?
4. Is a separate search service (Elasticsearch/OpenSearch) or a runtime AI call justified by measurement?
5. How do rows written before the normalized column exists become searchable?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Index technology | **A stored, indexed normalized text column, matched with LINQ** | **SQL Server Full-Text Search: measured unavailable.** In `mcr.microsoft.com/mssql/server:2022-latest` — the exact image [docker-compose.yml](../../docker-compose.yml) pins — `SERVERPROPERTY('IsFullTextInstalled')` is `0`, `sys.fulltext_languages` is empty, and `CREATE FULLTEXT INDEX` fails with *Msg 7609: Full-Text Search is not installed*. Worse, `CREATE FULLTEXT CATALOG` **succeeds** (metadata-only DDL), so a migration that only created a catalog would pass CI and fail at the first query. Installing `mssql-server-fts` means forking the official image into a Dockerfile the repo does not have (`db:` uses `image:`, not `build:`). And even installed it would not fit: per-store merchant-editable synonyms cannot be expressed in a full-text thesaurus (a **server-level** XML file per language), and full-text offers **no edit-distance typo tolerance** at all, so the `مكلسة` → `مكنسة` requirement would need the correction/fuzzy layer regardless |
| A separate search service | **None** | Elasticsearch/OpenSearch is a named non-goal ([ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) §12) and no measurement asks for it. See **Measured evidence** below: the indexed path is ~72× faster than what it replaces |
| Runtime AI for spelling/intent | **None** | M3 requires the recovery to be inspectable and testable. A model's guess is neither, it adds latency and cost to every query, and it makes search depend on an external API being up. The correction data is explicit rows and a pure edit-distance function instead |
| Where normalization lives | **`SearchText.Normalize`, a pure function in the Domain**, applied to indexed text and to the query | In the database (a computed column or collation): T-SQL cannot express FormD decomposition and Unicode-category folding cleanly, and a second implementation of the same rule would drift from the first — producing indexed text that the query text no longer matches, a silent failure only a shopper's complaint would surface |
| Which letter folding | **The standard Arabic IR folding (as Lucene's `ArabicNormalizationFilter`)**: drop non-spacing marks and tatweel, ة→ه, ى→ي, alef forms→ا, ؤ→و, ئ→ي, Arabic-Indic and Persian digits→ASCII, lowercase, punctuation→separator | Also dropping the standalone hamza (ء): more recall but it folds *ماء* onto *ما*, creating collisions that are worse than the problem |
| How the normalized form is kept current | **Computed in `CatalogTranslation.Apply`**, the single write path for all catalog text | A `SaveChanges` interceptor (works, but invisible — the entity is where "this is derived from that" belongs and is Domain-testable); a background reindexer (adds staleness for no gain) |
| Multi-word matching | **Every word is a separate AND condition**, each matched in the name, the description, **or the category name**, in any language | Requiring the contiguous phrase (today's behaviour — the defect); OR semantics (a query's rarest word would stop narrowing anything) |
| Ranking | **A score computed in SQL** (`ProductSortBy.Relevance`) | Scoring after the fetch: pagination would then order only the rows already selected, so page 2 would not continue page 1 |
| Pre-existing rows | **`SearchIndexBackfill` at boot, through the same C# function** | `migrationBuilder.Sql` with a T-SQL re-implementation of the normalizer (the drift hazard above); copying `Name` verbatim (silently wrong index data) |

## Decision

The options marked "Chosen" above.

- **Domain (Catalog).** `SearchText.Normalize(string?)` folds text to its matchable form and `SearchText.Tokenize(text, take?)` splits it into words (`MaxQueryTokens = 8` for a query, unbounded for indexed text). `SearchDistance.Between(a, b, maxDistance)` is a **bounded Damerau–Levenshtein (OSA)** distance — transposition of two neighbours costs one step, because that is the commonest keyboard error. Both are pure, dependency-free and exhaustively table-tested. The normalization invariant that the storage relies on — *it never lengthens ar/en text* — is itself a test.
- **Domain (`CatalogTranslation`).** `NameNormalized` and `DescriptionNormalized` are written by `Apply`, which the constructor and `Replace` both call, so no code path can set a name and leave its normalized form stale. `RebuildSearchText()` is `internal` and exists only for the backfill; it never touches the text itself.
- **Application.** `ProductSortBy.Relevance` joins the existing sort allowlist. With no keyword it falls back to `Newest` rather than erroring.
- **Infrastructure.** `CatalogQueries` normalizes the incoming keyword, applies one `Where` per word, and orders by the score. Everything is LINQ: `TenancyRuleTests.لا_SQL_خام_في_Infrastructure_خارج_الهجرات` bans raw SQL outside migrations with no allowlist, which rules out hand-written `CONTAINS`/`FREETEXT`/trigram SQL independently of availability. The admin product list normalizes the name too; slug and SKU stay raw.
- **Schema** (`SearchNormalizedCatalogText`, additive). Two columns per translation table, each the length of its source column, plus `IX_ProductTranslations_TenantId_NameNormalized` (and the category twin) including the owner key and culture so the `EXISTS` clause is answered from the index. The now-redundant single-column `TenantId` indexes are dropped, because the new index leads with `TenantId`; `Down()` restores them. `DescriptionNormalized` is deliberately **not** indexed: `nvarchar(4000)` exceeds SQL Server's 1700-byte index key limit, so no index on it is possible at all.
- **Ranking scale.** Whole normalized name equals the query (100) → name starts with it (90) → name contains it as a phrase (80) → name contains the query's first word (60) → the category name contains it (40) → otherwise the match came from a description or from scattered words (20). Ties break on id, as every other sort here does.
- **Recovery, in three ordered attempts.** (1) The shopper's own words — the hot path; results found means no further work happens at all. (2) Nothing found: each word is corrected to the closest word in the **store's own catalogue vocabulary** within the bounded edit distance, and the search is re-run. This is why the `مكلسة` → `مكنسة` case works with **no seeded correction data** — the vocabulary is the catalogue itself. (3) Still nothing: a category whose normalized name matches is offered instead of a dead end.
- **The correction is named, never silent.** A successful recovery returns `search.searchedInstead`, and the storefront renders "results for X instead of Y" with a control to insist on the original (`exact=true`, which disables recovery and accepts an empty result). Telling the shopper without letting them refuse would be half a decision; this is the same principle as never auto-selecting a variant ([ADR-0041](0041-storefront-variant-selection.md)).
- **Determinism is part of the contract.** The closest word is chosen by distance, then by how often the word occurs in the catalogue, then ordinal — a three-way tie-break with no early exit, because candidates come from a dictionary with no inherent order and any shortcut would make the same query answer differently between requests. A test asserts the same query four times.
- **Recovery is bounded to at most 3 words of at least 3 characters each.** Not cosmetic: the endpoint is anonymous with no rate-limit policy, and recovery costs a vocabulary read plus edit-distance work, so the input bound is what prevents a repeated nonsense query from amplifying cost. Correcting a two-letter word is meaningless anyway.
- **Suggestions suggest destinations, not words.** `GET /api/products/suggestions` returns visible **products** then active **categories**, ranked by the same expression the results page uses, so the dropdown and the results page never disagree for the same word. A suggested word would cost the shopper a second keystroke and a page of results; a suggested product *is* the destination, and it confirms the thing exists before they finish typing. Suggestions deliberately do **not** correct typos — the shopper is still typing, so "correcting" a half-written word would jump under their hands. Recovery belongs to the executed search, where the word is final. Below 2 characters no request is made at all.
- **The suggestion list is a real combobox.** `role="combobox"` with `aria-expanded`/`aria-controls`/`aria-autocomplete`, a `role="listbox"` of `role="option"` items, and **focus that stays in the input** while `aria-activedescendant` names the active option — moving focus onto the option would break typing. Arrow keys wrap, Home/End jump, Escape closes without navigating, and Tab closes **without** selecting, because leaving by keyboard is not a confirmation. Ids come from `useId` because the search box is mounted twice (desktop and the mobile sheet) and duplicate ids would point `aria-controls` at the wrong list.
- **Frontend.** `GET /api/products` gains the optional `search` object; the storefront shows a `role="status"` banner only when the server reports a correction, and offers the suggested category as the empty state's action. The relevance sort becomes the **default while searching** (newest-first buries an exact match) and its button appears only then. The vocabulary shown to the shopper is always the server's — the client never invents a correction.

## Measured evidence

Measured 2026-09-18 on the pinned image, 50,000 products across 20 tenants, index `(TenantId, NameNormalized)`; the fuzzy figures use a deliberately oversized 20,000-word-per-tenant vocabulary as a ceiling, since natural-language vocabulary saturates. 50 iterations each.

| Strategy | Per query | Note |
|---|---|---|
| The substring match M3 replaces (`LIKE N'%…%'`, leading wildcard) | **14.4 ms** | Index unusable; cost grows with catalog size (≈290 ms at 50,000 products in one store) |
| Normalized prefix match (index seek) | **0.2 ms** | **~72× faster** than the above |
| Fuzzy candidate fetch, length band, 20,000-word vocabulary | 4.9 ms | Only on the no-result path, never on a normal query |
| Fuzzy candidates narrowed by shared first character (seek) | 0.1 ms | |

This is the whole case against adding a search service: the thing being replaced was the slow part, and replacing it with an ordinary index made it faster than any network hop to another process could be.

## Consequences

- **Positive:** Arabic search works as speakers actually type — unvocalized, with ه for ة and ي for ى. Multi-word queries work. Category names are a match path. Results are ranked, and the ranking is computed next to the same purchasable-price rule the cards and sorts use ([ADR-0041](0041-storefront-variant-selection.md)), so nothing disagrees. **Search no longer depends on the database collation at all**, which also removes an unpinned-collation risk the release checklist had been carrying. No new dependency, container, service or credential.
- **Negative / limits:** the normalized form is **derived data**, so changing `SearchText.Normalize` invalidates every stored row and requires a reindex — the backfill makes that a boot-time operation, but it is a real coupling, and the normalizer's tests are therefore a compatibility contract, not just unit tests. Two extra columns per translation row. Matching a word *inside* a name is still `LIKE '%word%'`, i.e. a scan of a narrow index rather than a seek — acceptable at the measured sizes, recorded as **TD-45**. There is **no linguistic stemming**: Arabic root extraction (كتاب/كتب) is not attempted, so morphological variants do not match unless a synonym or correction row says so — recorded as **TD-46**.

## Revisit when

- A store's measured search latency exceeds its budget with a catalog large enough that the narrow-index scan dominates — then an inverted-term table (one row per product/word, making word matching a seek) is the next step, **before** any external service. TD-45 records the design.
- M13's no-result logs show a recurring pattern that normalization and edit distance cannot recover but stemming would — then reconsider a stemmer, still locally.
- Someone proposes Full-Text Search again: it needs a forked database image **and** it still would not give per-store synonyms or typo tolerance. Re-run the probe in this ADR before arguing from the documentation.

## Verification

- **Domain** — `SearchTextTests` (folding, digits, punctuation, invisible format characters, the never-lengthens invariant, idempotence), `SearchDistanceTests` (the `مكلسة`→`مكنسة` case by name, transposition as one step, symmetry, bounds), `CatalogSearchProjectionTests` (creating, editing, adding and removing a language all keep the normalized form current; the backfill repairs a pre-column row and leaves a legitimately empty one alone).
- **Integration** — `CatalogSearchTests`: unvocalized queries find vocalized names, alef/alef-maksura folding, scattered words, Arabic digits, category-name matching, the full relevance order, `Relevance` without a keyword falling back to newest, drafts/archived/inactive-category products staying hidden, the admin's normalized name search with raw slug/SKU, an edit changing what search matches, and two stores with identically named products each finding only their own.
- **Integration (recovery)** — the named `مكلسة` → `مكنسة` case end to end with no seeded data; missing/extra/substituted letters and transposed neighbours; **no** correction when the shopper's own words match; no invented correction beyond the distance bound; the word-count and word-length bounds; determinism over repeated identical requests; and a category offered when nothing matches at all.
- **Frontend** — `SearchBar.test.jsx` (18 tests): the declared combobox state, no request below 2 characters, arrow-key wrapping, Home/End, one active option at a time, Enter on an option navigating instead of searching, Enter with nothing active searching exactly as before suggestions existed, Escape and Tab closing without navigating, mouse selection surviving the blur race, a category opening the filtered catalog rather than a product page, the mobile sheet being told to close, distinct ids across two mounted instances, and **axe-clean with the list open**. `Catalog.test.jsx`: the banner names both words, is absent when the server says nothing, the insist control puts `exact=1` in the URL *and* sends it to the server, the category suggestion is an action rather than prose, and relevance is the default sort only while searching. `searchRouting.test.js`: a new search term drops a previous `exact`.
- **Release gate** — `dotnet build -warnaserror`, Domain, Application, Architecture and Integration suites, frontend lint/typecheck/vitest/build, and the regenerated inventories.

## Migration

`SearchNormalizedCatalogText` — **additive**: two nullable-or-defaulted columns and one index per translation table, and it drops two indexes the new one supersedes. It moves no data and loses nothing, so it is not registered in `MigrationSafetyTests.RecordedDestructive`. `Down()` drops the new columns and indexes and restores the two it replaced; what `Down()` loses is only the derived normalized text, which the next boot rebuilds. Rows written before the upgrade are normalized at first boot by `SearchIndexBackfill`, which is idempotent and self-repairing.
