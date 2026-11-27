# R14 — Multi-Entity ID & Numbering Scheme (Design Draft)

Status: DRAFT — for owner sign-off. No code or migration accompanies this document.
Branch: `docs/r14-id-scheme-draft` (off `dev-sayeem` @ `6140606`), 2026-11-27.

R14 is the last planned wave ("R14 multi-entity (id-scheme design sign-off, runs last)" —
`docs/06_Plan.md:5`). This draft is the sign-off artifact: what the spec demands, what the
code does today, what breaks when a second legal entity goes live, and which fix to buy.

---

## 1. What R14 asks (spec cards)

Six cards in `Requirement plan/spec_snapshot.md` govern ids and numbering. Quoted text is
the cards' normative requirement lines.

**ERP-FINANCE-016 — Document numbering sequences** (snapshot line 854)
> "The system shall issue document numbers from **entity- and document-type-specific
> sequences** with concurrency safety and configurable gap-handling policy."

AC 1: concurrent requests draw distinct numbers from the *correct entity/type* sequence.
AC 2: a voided number stays recorded with status and reason and is not silently reassigned.
Security note: sequence configuration is privileged, versioned, and **tenant scoped**.

**ERP-PLATFORM-017 — Configurable business numbering** (line 17706)
> "The platform shall generate configured human-readable identifiers by tenant and
> document type **while retaining an immutable internal primary key**."

The rationale is explicit that business numbers "should not become database identity or
security tokens." AC 2: the format can include **entity, period, and prefix** components
with a documented reset policy. AC 3: changing display format never rewrites old ids.

**ERP-PLATFORM-012 — Organization and legal-entity hierarchy** (line 17586)
> "…a tenant can create **multiple legal entities** … with stable IDs and parent links."

**ERP-PLATFORM-027 — Immutable master-data identifiers** (line 17946)
> "Each master-data record shall have an **immutable, non-recycled system identifier
> separate from its editable business code** and translated display names."

AC 3: API responses must distinguish system ID, business code, and label.

**ERP-PLATFORM-028 — Tenant-scoped business code uniqueness** (line 17970)
> "…enforce uniqueness of configured business codes **within their declared tenant and
> object-type scope** … Application-only duplicate checks race under concurrent entry and
> **can incorrectly impose global uniqueness**."

**ERP-INVENTORY-009 — Legal entity stock attribution** (line 8539): inventory
transactions attribute to an owning legal entity and operating entity "independently of
tenant and warehouse, when multi-entity operation is enabled."

Read together: R14 must make numbering **per legal entity and per document type**, keep a
stable internal key **separate from** the human number, scope code uniqueness to
tenant+entity, and treat the human number as a display/business key — not the database
identity and not a security token. Today's scheme violates the "entity-specific" and
"separate internal key" halves; the rest of this draft is about closing that gap cheaply.

---

## 2. Current state inventory

### 2.1 The `doc_` sequences — instance-global

All document ids come from one helper: `NumberSeries.Next(prefix)` builds
`doc_<prefix>` and returns `{prefix}-{value:D4}` from a PostgreSQL sequence
(`apps/api/Data/Repositories/NumberSeries.cs:10-15`; P5 adds `List`/`Exists`/void support
on the same class, `feat/finance-deep`).

- A PG sequence is database-level state: it has no tenant or entity column, so **every
  `doc_` sequence is shared by every tenant and legal entity in the schema**. ~115
  sequences exist across migrations (`doc_j`, `doc_po`, `doc_so`, … `doc_fxr` from
  `20261127151812_AddFinanceDeepP5`).
- Prefixes are **not** per document type either — several unrelated tables draw from one
  sequence: `mrp` feeds MrpRun, MrpLine, ActionMessage and MrpPublication
  (`MrpService.cs:59,211,235,348`); `mav` feeds two event types (`MaintenanceService.cs:38,141`);
  `wkc` (`RoutingService.cs:167,188`), `srn` and `scn` (`SchedulingService.cs:42,77,156,182`),
  `sub` (`SubcontractService.cs:61,111`), `lbt` (`ShopFloorService.cs:33,244`), `POS` and
  `ORG` (`PeopleOrgServices.cs:90,108,147,210`), `TME` (`TimeService.cs:30`,
  `PeopleLeaveService.cs:164`).
- One id is client-controlled: the depreciation run uses the **Idempotency-Key as the row
  PK** when present (`AssetService.cs:98`).

Consequence: sequence-issued ids (`J-0007`, `SO-0012`) are globally unique *by accident of
the shared sequence*, not by design. They cannot collide across entities today, but they
carry no entity scope, and each entity's numbering pace leaks into every other's.

### 2.2 Literal composed ids — business code as global PK

A second family composes the primary key deterministically from business codes:

| Pattern | Where | Table family |
|---|---|---|
| `CUR-{code}` | `CurrencyService.cs:32` (feat/finance-deep) | currencies |
| `DS-{code}` / `DV-{set}-{value}` | `CurrencyService.cs:173,195` | dimension sets/values |
| `NSV-{number}` | `FinanceP5Controllers.cs:153` | number-series voids |
| `ACC-{code}` | `AccountService.cs:15` (dev-sayeem) | chart of accounts |
| `FP-{name}` | `FiscalPeriodService.cs:25` | fiscal periods |
| `TC-{code}-V{version}` | `TaxCodeService.cs:17` | tax codes |
| `FS-{key}` | `PeoplePayServices.cs:302` | finance settings |
| `PMG-{guid[..12]}`, `QRR-/DLG-{ticks}`, `EIV-{guid[..16]}` | `Customer360Service.cs:125`, `SupplierQualificationService.cs:43`, `RequisitionService.cs:231`, `SalesDeliveryService.cs:15` | party merges, questionnaire responses, delegations, e-invoice refs |

The configured model is consistent across all of them (e.g. `currencies` on
`feat/finance-deep`, `ErpDbContext.cs:2210-2216`): **PK = the composed Id, global**;
business uniqueness is a separate unique index on `(tenant_id, legal_entity_id, code)`.
So the database key is *more* restrictive than the business rule — exactly the inverted
situation PLATFORM-028 warns about. The `ACC-`/`FP-`/`TC-` rows predate P5: the collision
family is already on `dev-sayeem`, P5 extended it.

### 2.3 Scoping pattern

`EntityContext` (`apps/api/Common/EntityContext.cs`) carries `TenantId`/`EntityId`
resolved from the verified token by AuthMiddleware, defaulting to the pilot scope
`T-001`/`LE-001`. Every table carries `tenant_id` + `legal_entity_id` with plain indexes;
scoping is done by hand-written `Where` clauses in services/repositories — there are
**zero EF global query filters** (`HasQueryFilter` count in `ErpDbContext.cs` is 0), and
RLS has not landed. Only `LE-001` exists in seeds. `LegalEntity` is a plain record
(`apps/api/Models/Identity.cs:7`) with no per-entity settings of its own yet.

### 2.4 Recorded review findings (deferred to R14)

`reviews/finance-deep-p5-review.md` (2026-11-27, APPROVE WITH FIXES) deferred three
"Minor" findings to this wave, with the ruling that they "match the existing global doc_
sequence design":

- **Finding 5** — literal-id PK collisions across legal entities: `CUR-`, `DS-`,
  `DV-`, `NSV-` ids are global PKs; "second entity same void → PK collision 500."
- **Finding 6** — `NumberSeries.List` reads `pg_sequences` unscoped; `last_value`
  mixes tenants sharing the schema.
- **Finding 7** — void accepts never-issued numbers; issued-range validation is
  unreliable against global sequences (coupled to 5: two tenants voiding the same
  still-unissued number both insert `NSV-{number}` → PK collision).

---

## 3. What multi-entity breaks

1. **Cross-entity PK collisions → 500s (finding 5).** Entity B creating currency "EUR",
   account "1010", period "2026-01", dimension set "CC", or voiding a number entity A
   already voided inserts a PK that entity A already owns. The app-level duplicate checks
   are correctly scoped, so they pass; the global PK then rejects; the envelope contract
   degrades to an unhandled 500. Highest-probability victims are fiscal periods
   (`FP-2026-01` in two jurisdictions is *certain* on day one) and accounts.
2. **Cross-tenant leakage in the numbering admin view (finding 6).** `pg_sequences` is
   per-schema, not per-tenant: every tenant's admin sees every other tenant's issuance
   volume (`last_value`) — a security smell against the "tenant scope on every query"
   rule, and void counts (scoped) shown next to positions (unscoped) that aren't theirs.
3. **Void ledger ambiguity (finding 7).** With shared sequences there is no per-entity
   "issued range" to validate a void against, void PKs can collide, and after any
   per-entity split lands, the same human number (`PO-0001`) legitimately exists in N
   entities — `NSV-{number}` then collides on the first two voids by construction.
4. **The sequences don't match the spec's shape.** FINANCE-016 wants entity- AND
   document-type-specific sequences; today they are neither (entity: shared; type:
   several types share `mrp`/`mav`/`wkc`/…). Gap-handling policy is unimplemented
   (PG sequences burn numbers on rollback — acceptable default, but there is no policy
   hook or disclosure, which FINANCE-016's edge case requires).
5. **Human numbers leak pace and carry no entity segment.** Entity 2's first PO is
   `PO-0041` because entity 1 issued forty — cross-entity volume disclosure, no entity
   component in the number (PLATFORM-017 AC 2 expects one), and id-based URL enumeration
   across entities remains only blocked by service-layer discipline, since query filters
   and RLS are absent.
6. **Client-controlled PKs.** The idempotency-key-as-PK on depreciation runs (`AssetService.cs:98`)
   lets one tenant's key collide with another's — same 500 family.

---

## 4. Design options

**Option A — Per-entity sequence ranges/offsets.** Keep PG sequences but partition the
number space: either one sequence per (entity, prefix) (`doc_j_le001`) or arithmetic
slots (entity slot N draws `[N·10⁶, (N+1)·10⁶)`).
*Trade-offs:* smallest diff — no table, no format change, PKs stay unique, additive
migration only. But: numbers become sparse or wide (poor readability); no entity segment
in the human number; the admin view is still `pg_sequences` (fixing finding 6 needs a
naming-convention filter — fragile); void-range validation becomes possible per entity
(fixes 7 partially); entity count vs number-width needs a cap; and it does **nothing**
for the literal-PK family (CUR-/DS-/ACC-/FP-), so finding 5 survives.

**Option B — Per-entity series rows + composite document numbers `{entity}-{prefix}-{seq}`.**
Replace sequence consumption with a tenant-scoped `number_series` table
(`tenant_id, legal_entity_id, doc_type, prefix, last_value, format, reset_policy, …`),
issuing via an atomic single-row `UPDATE … RETURNING` inside the posting transaction;
the human number gains an entity segment (e.g. `LE1-J-0001`), unique per
`(tenant, entity, doc_type, seq)`.
*Trade-offs:* this is the FINANCE-016 shape — sequences become entity- and type-specific
rows, configurable, versioned, auditable (the card's "privileged and versioned" config
finally has somewhere to live), and the admin view becomes a scoped table query (finding
6 dies). Issuance is transactional: a rolled-back posting burns nothing, which changes
gap semantics (improvement, but must be documented per the card's exception clause).
PKs can stay as-issued strings (now globally unique because of the entity segment);
no PK-type migration. Cost: a real table + backfill, every `numbers.Next()` call site
gains the series context (~60 sites, mechanical), and the **literal-PK family still
needs its own fix** — B governs issued numbers only. Mixed old/new formats coexist in
one table (see open question 1).

**Option C — UUID/ULID internal PKs; human numbers as unique business keys.** The
spec-native end state: internal PK becomes a UUID/ULID, the human number is a scoped
unique business column, APIs expose both (PLATFORM-027 AC 3 verbatim; PLATFORM-017's
"internal primary key" separation verbatim).
*Trade-offs:* best long-term properties — ids never leak pace or scope, codes can change
without FK churn, integrations get stable handles. Highest cost of the three: every
table PK and FK changes, seeds, tests, web deep links and OpenAPI contracts all churn,
and history needs old-id aliasing. It also *still* needs B's series machinery to produce
the human numbers — C alone answers nothing about numbering. Paying C's migration before
B's exists buys nothing B doesn't.

---

## 5. Recommendation and phased path

**Recommendation: B as the target for R14, with a small additive fix for the literal-PK
family landing first; C is explicitly not recommended now** (its API-contract benefit is
available cheaper by exposing `id` and `number` as separate fields, which B plus a DTO
change already satisfies).

- **Phase 1 — stop the 500s (additive, no data rewrite).** Compose scoped ids for *new*
  rows in the literal family: fold the entity segment into the id (`CUR-{entity}-{code}`,
  `DS-…`, `DV-…`, `NSV-{entity}-{number}`, same for `ACC-`/`FP-`/`TC-`). Existing rows
  keep their ids — no UPDATE, uniqueness holds, the scoped unique indexes already in
  place keep enforcing business rules. Retire the idempotency-key-as-PK on depreciation
  runs (issue an id, store the key in the idempotency record instead). This can land as
  its own small wave and does not wait for the rest of R14.
- **Phase 2 — series table cutover (the core of R14).** Introduce `number_series`, seed
  one row per (entity, doc type) at each entity's current `doc_<prefix>` position (only
  `LE-001` exists today, so the backfill is trivial); route all `numbers.Next()` sites
  through it; human numbers for new documents gain the entity segment; retire the
  `pg_sequences` admin view in favor of the scoped table (fixes 6); validate voids
  against the issued range of the (entity, doc type) series and key void records on the
  scoped composite (fixes 5-residual + 7); version series config with audit. Split the
  multi-type prefixes (`mrp`, `mav`, `wkc`, `srn`, `scn`, `sub`, `lbt`, `TME`, `POS`,
  `ORG`) into per-document-type series rows. Old `doc_*` PG sequences are dropped only
  after a soak period — they are the one irreversible step, and per the house rule it
  needs its own owner confirmation.
- **Phase 3 — optional, spec-native polish (defer unless needed).** UUID/ULID internal
  PKs per option C, if/when external integrations or merge-lineage requirements make
  opaque stable ids worth the ERD churn. Everything PLATFORM-027 AC 3 demands is met at
  the DTO layer without it.

**Additive vs cut-over:** Phase 1 and the series-table introduction are purely additive
(new ids only for new rows, old ids remain valid, PLATFORM-017 AC 3 explicitly blesses
format evolution without rewriting old identifiers). What genuinely needs a cut-over
decision is only (a) dropping the legacy PG sequences and (b) whether new-document human
numbers may switch format mid-series. No historical renumbering is proposed anywhere.

**Open questions the owner must rule on:**

1. Mixed human-number formats in one live series after Phase 2 (`J-0007` then
   `LE1-J-0008`) — acceptable, or reset new-series-per-entity at cutover?
2. Entity segment in numbers: short entity **code** (human-friendly, but codes can change
   per PLATFORM-027 — needs a stable, non-recycled alias column) vs entity **id**
   (`LE-001`, stable but noisy)? Who assigns it?
3. Gap policy defaults: burn-on-rollback tolerated with disclosed gaps (FINANCE-016
   exception), or void-record every gap? Any jurisdiction requiring gapless sequences
   (config flag only)?
4. Do master-data rows (currencies, dimension sets/values, accounts) need issued human
   numbers at all, or is the scoped business code plus a system surrogate id enough?
5. Periodic reset policy per PLATFORM-017 AC 2 — do any document types reset annually
   (period segment in format), or is reset "never" the documented default?
6. Shared-prefix families: split into per-type series (recommended) or keep family
   sequences? Affects void semantics ("void `mrp-0007`" — which table?).
7. Does entity scoping of numbering land together with RLS (transaction-local context)
   or before it? The `number_series` table should be under whatever RLS regime arrives.
8. Phase 3 (UUID/ULID PKs): in or out of R14 scope, given the DTO-level alternative?
