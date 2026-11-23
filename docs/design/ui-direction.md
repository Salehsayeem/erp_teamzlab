# UI/UX Design Direction — TeamZlab ERP Pilot

> Status: APPROVED DIRECTION for Wave 1 (procure-to-receive + journals pilot).
> Sources: `docs/02_AppFlow.md`, `docs/03_UI_Spec.md` (token values — nothing below invents a color).
> Scope: written spec only. No application code was written; nothing under `apps/` was read.
> The CSS in §4 is written against the `components/uikit.tsx` class names given in the task
> (`.btn`, `.input`, `.badge`, `.table`, `.tile`, `.skeleton`); apply it as plain CSS — no Tailwind, no component library.

---

## 1. Design principles

1. **Tables and forms are the product.** Every pilot screen is a list or a form first; chrome is a top bar plus role-aware nav, nothing else. Decoration budget on this pilot: zero. Density defaults to 40px rows, 14px body, 4px-grid spacing.
2. **Every screen renders four states: loading / empty / error / success** (02 §1). These are designed regions with fixed placement (see each blueprint in §3), never improvised spinners.
3. **WCAG 2.2 AA is the floor, not the goal.** Text ≥ 4.5:1, UI boundaries and meaningful graphics ≥ 3:1, focus ring never removed (§5.1), targets ≥ 24px (≥ 44px on touch), reflow at 320px width, no keyboard traps.
4. **Status = color + label, never color alone.** Badges always carry a text label with the color in a dot + border; deltas carry ▲/▼ plus words; a disabled Approve button shows *why* in visible text ("You submitted this PO"), never a tooltip-only reason.
5. **RTL-safe by construction.** Only logical CSS properties (`margin-inline-start`, `padding-inline`, `inset-inline-start`, `text-align: start`, `border-block-end`). Layout mirrors automatically under `dir="rtl"`. Icons never carry direction meaning alone; paired with text.
6. **Tokens only, three layers.** Primitive → semantic → component (§2). Components may reference layers 2–3 only; no raw hex anywhere below the primitive layer. Dark mode = `prefers-color-scheme` swap at the semantic layer; components never branch on theme.
7. **Irreversible actions state consequences before submit.** Reverse/post dialogs say exactly what will happen ("posts a reversing entry, cannot be undone, original stays in the audit trail"). Every destructive action confirms; posting failure preserves the draft and shows an inline error.
8. **Maker–checker is enforced in the UI, not just the API.** The user who submitted a PO never sees an enabled Approve button on it (§3.5).
9. **Numbers are data.** IDs, amounts, quantities render in `mono`, locale-formatted via `Intl` (never `toFixed` by hand), end-aligned (`text-align: end`), tabular figures.
10. **Stat tiles are text.** Value + label + delta in text/border tokens; no colored blocks, no sparklines in the pilot (per dataviz guidance: tiles are typography, not charts).

---

## 2. Token layers and verified contrast

### 2.1 Three layers

| Layer | Name pattern | May be referenced by | Content |
|---|---|---|---|
| 1 — Primitive | `--p-*` | Layer 2 only | The 20 raw hex values of 03_UI_Spec §2 (10 light + 10 dark), under neutral, theme-free names |
| 2 — Semantic | `--bg`, `--surface`, `--border`, `--text`, `--text-muted`, `--accent`, `--success`, `--warning`, `--danger`, `--info` | Layers 3 and components | Exactly the token names of 03_UI_Spec §2; swapped wholesale under `prefers-color-scheme: dark` |
| 3 — Component | `--btn-filled-fg`, `--control-border`, `--focus-ring`, font shorthands | Components | Composition decisions derived from §2.3 rules; colors always point at layer 2 |

### 2.2 Verified contrast pairs (computed from 03_UI_Spec hex — do not re-derive)

| # | Foreground | Background | Ratio | Verdict / use |
|---|---|---|---|---|
| 1 | `text` #1A1D23 | `bg` #FFFFFF (light) | 16.88 | body text — pass |
| 2 | `text` | `surface` #F6F7F9 | 15.75 | text on cards/table head — pass |
| 3 | `text-muted` #5A6070 | `bg` | 6.28 | secondary text — pass |
| 4 | `text-muted` | `surface` | 5.86 | captions on cards — pass |
| 5 | `accent` #1F5EFF | `bg` | 5.12 | links — pass |
| 6 | white | `accent` | 5.12 | primary button label (light) — pass |
| 7 | white | `danger` #C21F2E | 5.95 | danger button label (light) — pass |
| 8 | #0F1115 | `accent` dark #5B8CFF | 5.97 | primary button label (dark) — pass |
| 9 | #0F1115 | `danger` dark #F07178 | 6.60 | danger button label (dark) — pass |
| 10 | `text` dark #E8EAEF | `bg` #0F1115 | 15.70 | body text dark — pass |
| 11 | `text-muted` dark #9AA0AC | `bg` | 7.20 | secondary text dark — pass |
| 12 | `accent` dark | `bg` | 5.97 | links dark — pass |
| 13 | `success` #0E8345 | `bg` | 4.83 | delta-up text (light) — pass |
| 14 | `danger` | `bg` | 5.95 | delta-down text (light) — pass |
| 15 | `success` dark #3DBB6E | `bg` | 7.68 | delta-up text (dark) — pass |
| 16 | `danger` dark | `bg` | 6.60 | delta-down text (dark) — pass |
| 17 | `warning` #B26A00 | `bg` **as text** | **4.24** | **FAIL 4.5:1 — warning is never a text color in light theme** |
| 18 | `warning` | `surface` as text | **3.95** | **FAIL — reinforces rule above** |
| 19 | `warning` | `bg` as dot/border | 4.24 | pass (non-text needs 3:1, WCAG 1.4.11) |
| 20 | `warning` | `surface` as dot/border | 3.95 | pass (3:1) |
| 21 | `warning` dark #E0A24A | `bg` as text | 8.49 | passes — but rule below forbids it everywhere for consistency |
| 22 | `border` #D9DDE3 | `bg` | 1.36 | dividers/zebra only — **never a control boundary** (fails 1.4.11) |
| 23 | `info` #0B6E8A | `bg` | 5.81 | info banner text/border — pass |
| 24 | `info` dark #58B7D4 | `bg` | 8.22 | info banner dark — pass |
| 25 | `text-muted` as control border | `bg` / `surface` | 6.28 / 5.86 | **control boundaries use `text-muted`**, not `border` — pass 1.4.11 |
| 26 | `danger` | `surface` as text | 5.55 | field error text on cards — pass |

### 2.3 Derived rules (the only interpretations this spec makes)

1. **`warning` is never applied to text, in either theme.** The spec's blanket claim "all text pairs ≥ 4.5:1" does not hold for warning in light theme (4.24 / 3.95). Warning appears only as a badge dot + border and a banner edge (≥ 3:1 non-text); the label next to it is always in `text`. Uniform across themes so no per-theme branches.
2. **Badges are outline style, never filled.** `.badge` = text label in `text` + 8px status dot + 1px status border. Filled warning badges cannot pass AA with either white (4.24) or ink (3.98) labels; one pattern that always passes beats two that sometimes fail.
3. **Control boundaries (inputs, secondary buttons, modals) use `text-muted`, not `border`.** #D9DDE3 is 1.36:1 on white — fine for dividers, table lines and zebra, invisible as an input edge. `border` stays reserved for decoration.
4. **Filled button labels flip by theme, not by component.** Light: white on `accent`/`danger` (5.12 / 5.95). Dark: `bg` (#0F1115) on `accent`/`danger` (5.97 / 6.60). One token `--btn-filled-fg` does the flip; buttons never hardcode a label color.
5. **Hover/active shift luminance, not hue.** `filter: brightness(.94)` / `(.88)` on filled buttons — no new colors are introduced, and label contrast can only improve as the background darkens.

---

## 3. Screen blueprints

### 3.0 Shared chrome (all authenticated screens)

```text
TOP BAR — block-size 48, border-block-end 1px --border
┌──────────────────────────────────────────────────────────────────────────────┐
│ [☰]  TeamZlab ▸ TZ Manufacturing Ltd ▾          [EN|BN]   A. Karim (Admin) ▾ │
└──────────────────────────────────────────────────────────────────────────────┘
  ☰ = nav toggle (mobile only)     entity switcher always visible (02 §1)
```

- First tabbable element on every page: **skip link** ("Skip to main content"), visible on focus, top inline-start.
- Toast region: fixed, `inset-block-end`/`inset-inline-end` 16px; mirrors automatically in RTL.
- Keyboard entry point order on every page: skip link → top bar → nav → main content.

---

### 3.1 Login — 3-user picker (`/login`)

Pilot-mode shim over IdP auth (02 §1 says IdP, D-13 TBD — flagged in §8). Three seeded role cards act as a native radio group.

**Desktop (≥1280)**

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│                                    EN | BN                    (inline-end)   │
│                                                                              │
│                               TeamZlab ERP                                   │
│                    Pilot sign-in — choose an account                         │
│                                                                              │
│            ┌──────────────────────────────────────────────────┐              │
│            │ (•) Admin            — full access               │  radio card  │
│            ├──────────────────────────────────────────────────┤              │
│            │ ( ) Procurement Officer — maker: POs, receipts   │              │
│            ├──────────────────────────────────────────────────┤              │
│            │ ( ) Finance Controller  — checker: approvals,    │              │
│            │                           journals               │              │
│            └──────────────────────────────────────────────────┘              │
│            [                  Continue                    ]  .btn--primary   │
│            Production sign-in uses the IdP (D-13, TBD).   caption, muted     │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Mobile (<768)**

```text
┌──────────────────────────┐
│ EN | BN                  │
│                          │
│ TeamZlab ERP             │
│ Pilot sign-in            │
│ ┌──────────────────────┐ │
│ │ (•) Admin            │ │
│ ├──────────────────────┤ │
│ │ ( ) Procurement Off. │ │
│ ├──────────────────────┤ │
│ │ ( ) Finance Contr.   │ │
│ └──────────────────────┘ │
│ ┌──────────────────────┐ │
│ │       Continue       │ │  .btn--primary .btn--block, sticky bottom
│ └──────────────────────┘ │
│ Uses IdP in production.  │
└──────────────────────────┘
```

**State placement**
- Loading: the three cards render as `.skeleton--row` bars; Continue disabled with `aria-busy` on the form.
- Error: `.banner--error` above the card list — "Couldn't load pilot accounts. [Retry]" (retry is a `.btn--secondary .btn--sm` inside the banner).
- Empty: not a valid state here — the picker is seeded; a zero-account seed renders the error state, not an empty one.
- Success: route to `/[locale]/select-entity`.

**Keyboard / a11y**: locale toggle → radio group (Tab in, ↑/↓ to move, Tab out) → Continue. Cards are native radios visually styled — no divs-with-click. `lang` on `<html>` follows chosen locale.

---

### 3.2 Select entity (`/[locale]/select-entity`)

**Desktop**

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│  Set up your workspace                                     Step 2 of 2       │
│  ┌────────────────────────────────────────────────────────┐                  │
│  │ ORGANIZATION (tenant)                                  │                  │
│  │ [ TeamZlab Holdings Ltd                         ▾ ]    │  .input (select) │
│  │                                                        │                  │
│  │ LEGAL ENTITY                                           │                  │
│  │ [ TZ Manufacturing Ltd — Dhaka                  ▾ ]    │  filtered by org │
│  │                                                        │                  │
│  │ Currency BDT · Timezone GMT+6 · Period 2026-11 [● OPEN]│  badge--success  │
│  │                                                        │                  │
│  │ [                Continue                       ]      │  .btn--primary   │
│  └────────────────────────────────────────────────────────┘                  │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Mobile**: identical stack, full-width selects, Continue sticky bottom (`.btn--block`).

**State placement**
- Loading: two skeleton bars in place of the selects.
- Error: `.banner--error` above the form — "Couldn't load your organizations. [Retry]".
- Empty: "No legal entities under this organization — ask an admin to create one in Admin ▸ Entities." (blocked state; no create CTA for non-admins).
- Success: context preview populates; Continue routes to dashboard. Deep-link target (02 §1 `?next=`) is honored after context is set.

**Keyboard**: org select → entity select (disabled + hint "Choose an organization first" until org picked) → context preview (static) → Continue. Period status is text + dot, never color alone (principle 4).

---

### 3.3 Dashboard (`/[locale]/dashboard`) — role-aware nav + 4 stat tiles

**Desktop**

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ top bar (§3.0)                                                               │
├────────────┬─────────────────────────────────────────────────────────────────┤
│ NAV (240)  │ Dashboard                                    role: Admin        │
│ ▸ Dashboard │ ┌───────────┐ ┌───────────┐ ┌───────────┐ ┌───────────┐        │
│ ▸ Procure… │ │ OPEN POS  │ │ PENDING   │ │ AWAITING  │ │ JOURNALS  │        │
│ ▸ Finance  │ │           │ │ APPROVALS │ │ RECEIPT   │ │ POSTED    │        │
│ ▸ Inventory│ │    12     │ │     3     │ │     5     │ │    41     │ tile__value
│ ▸ Admin    │ │ ▲ 3 vs wk │ │ ▲ 1 vs wk │ │ ▼ 2 vs wk │ │ — no data │ tile__delta
│            │ └───────────┘ └───────────┘ └───────────┘ └───────────┘        │
│            │ Recent purchase orders (5)                     [ View all → ]  │
│            │ ┌ PO# · Supplier · Amount · Status ──── 5 rows, links ┐        │
│            │ Recent journals (5)                            [ View all → ]  │
│            │ ┌ JRNL# · Period · Amount · Status ──── 5 rows, links ┐        │
└────────────┴─────────────────────────────────────────────────────────────────┘
```

Tiles per role (labels never change per role; counts filter):
- Admin: all four, org-wide.
- Procurement (maker): Open POs (mine), Pending approvals (all), Awaiting receipt (mine), Journals posted (org, read-only).
- Finance (checker): Open POs (org), Pending approvals (queue — this is their primary number), Awaiting receipt (org), Journals posted (mine).

Nav per role: Admin sees all modules; Procurement sees Dashboard + Procurement; Finance sees Dashboard + Finance + Procurement (read-only badge next to module name). Hidden-by-role, not disabled — disabled nav items teach wrong mental models.

**Mobile (<768)**: nav collapses into the ☰ drawer (full-screen sheet, focus-trapped, Esc closes); tiles render 2×2; the two mini-tables render as 3-card lists with View all links.

**State placement**
- Loading: each tile shows `.tile__label` + `.skeleton--tile` value bar; each table region shows 5 × `.skeleton--row`. Label shells render immediately — layout never jumps.
- Error: **per-region**, not per-page — a tile-region `.banner--error` and one banner per table region, each with its own Retry. One failing widget never blanks the page.
- Empty: tile value `0` is data, not an empty state. Each mini-table uses its module empty state ("No purchase orders yet — create your first PO").
- Success: as drawn.

**Keyboard**: skip link → top bar → nav → tiles (each tile is one focusable summary element with a full text alternative, e.g. "3 purchase orders pending approval, 1 more than last week") → View all links → table row links.

---

### 3.4 Suppliers — list + create form (`/[locale]/procurement/suppliers`)

**Desktop list**

```text
Procurement ▸ Suppliers                              [ + New supplier ] .btn--primary
┌──────────────────────────────────────────────────────────────────────────────┐
│ [ Search suppliers…                          ]  [Status ▾] [Type ▾]  (Reset) │
├────────┬────────────────────┬──────────────┬────────────┬──────────┬─────────┤
│ Code ⇅ │ Name ⇅             │ Phone        │ BIN        │ Status   │ Updated │
│ S-001  │ Meghna Textiles    │ +880 1712…   │ 1234567890 │ ● Active │ 21 Nov  │
│ S-002  │ Dhaka Chemicals    │ +880 1898…   │ 2345678901 │ ● On hold│ 20 Nov  │
│  …rows 40px, sticky thead + sticky first column (Code), zebra, row hover     │
└────────┴────────────────────┴──────────────┴────────────┴──────────┴─────────┘
```

**Mobile list**: table collapses to cards (03 §4): `S-001 · Meghna Textiles · ● Active` + phone line; tap opens 360.

**Create form — right drawer, 480px on desktop; full-screen sheet on mobile**

```text
SUPPLIERS PAGE                  ┃  New supplier                        [Esc] ✕
(list dims, inert)              ┃  ────────────────────────────────────────
                                ┃  IDENTITY
                                ┃  * Name     [ ____________________ ]
                                ┃    Code     [ S-003 ]  auto — hint: generated
                                ┃  TAX
                                ┃  * BIN      [ __________ ]  hint: 9–12 digits
                                ┃  CONTACT
                                ┃    Phone    [ __________ ]
                                ┃    Email    [ __________ ]
                                ┃  ADDRESS
                                ┃    [ ____________________________________ ]
                                ┃  ────────────────────────────────────────
                                ┃  [ Cancel ]            [ Save supplier ]
                                ┃   .btn--secondary        .btn--primary
                                └──────────────────────────────────────────
```

**State placement**
- Loading: n/a for the drawer itself (opens instantly); the auto Code field shows a `.skeleton` bar until the next code resolves.
- Error: banner at the drawer top — "Supplier couldn't be saved. Fix the highlighted fields and save again." Field-level `.field-error` under each invalid input (`aria-describedby`, `aria-invalid`); focus moves to the first invalid field on failed submit. Save button enters `.is-loading` while submitting.
- Empty: n/a (form). 
- Success: drawer closes, toast "Supplier S-003 created", list row highlighted via row anchor, focus moves to the new row.

**Keyboard**: Name → Code (readonly) → BIN → Phone → Email → Address → Cancel → Save. Esc closes → triggers unsaved-changes guard if dirty (§5.6). Background is `inert` while open.

---

### 3.5 Purchase orders — list with per-status actions (`/[locale]/procurement/purchase-orders`)

**Canonical status → action map (the one table to implement)**

| Status | Badge | Row actions | Visible to |
|---|---|---|---|
| Draft | `badge--draft` "Draft" | **[Submit]** `.btn--primary .btn--sm` | maker (owner) |
| PendingApproval | `badge--pending` "Pending approval" | **[Approve]** primary sm + **[Reject]** danger sm — **disabled with visible reason if current user = submitter** | checker |
| Approved | `badge--success` "Approved" | **[Receive]** primary sm | storekeeper |
| Sent | `badge--success` "Sent" | **[Receive]** primary sm | storekeeper |
| Rejected | `badge--danger` "Rejected" | **[Edit]** secondary sm (rejection reason shown as caption under status) | maker |
| Received | `badge--success` "Received" | — (row links to detail) | all |

**Desktop**

```text
Procurement ▸ Purchase orders                        [ + New PO ] .btn--primary
┌──────────────────────────────────────────────────────────────────────────────┐
│ [ Search… ] [Status ▾] [Supplier ▾] [Period ▾]                       (Reset) │
├───────┬──────────────┬────────┬──────────────┬─────────────────┬─────────────┤
│ PO# ⇅ │ Supplier     │ Date ⇅ │ Amount (BDT) │ Status          │ Actions     │
│ PO-101│ Meghna Tex.  │ 21 Nov │  1,240,000.00│ ● Draft         │ [Submit]    │
│ PO-102│ Dhaka Chem.  │ 21 Nov │     86,500.00│ ● Pending appr. │ [Approve]   │
│       │              │        │              │                 │ [Reject]    │
│ PO-100│ Meghna Tex.  │ 20 Nov │    512,300.00│ ● Approved      │ [Receive]   │
│ PO-099│ Meghna Tex.  │ 20 Nov │     98,000.00│ ● Sent          │ [Receive]   │
│ PO-098│ Dhaka Chem.  │ 19 Nov │    421,700.00│ ● Rejected      │ [Edit]      │
│       │              │        │              │  reason caption │             │
└───────┴──────────────┴────────┴──────────────┴─────────────────┴─────────────┘
```

Maker–checker rendering: if PO-102 was submitted by the signed-in user and they are also a checker, the Actions cell shows `[Approve] [Reject]` with `aria-disabled="true"` plus a visible caption line "You submitted this PO — another approver must review it." Never tooltip-only, never hidden buttons.

**Mobile (<768)** — card per PO:

```text
┌────────────────────────────────┐
│ PO-102 · Dhaka Chemicals       │
│ 21 Nov · BDT 86,500.00  mono   │
│ ● Pending approval             │
│ ┌───────────┐  ┌───────────┐   │
│ │  Approve  │  │  Reject   │   │  .btn--sm→44px touch rows, side by side
│ └───────────┘  └───────────┘   │
└────────────────────────────────┘
```

**State placement**
- Loading: toolbar renders; table body = 8 × `.skeleton--row`; column headers render immediately.
- Empty: "No purchase orders yet — create your first PO" + `[+ New PO]` CTA inside the table region (keeps toolbar).
- Filtered-no-match: "No POs match the current filters. [Clear filters]" — distinct from empty (03 §5).
- Error: `.banner--error` above the table region — "Purchase orders couldn't be loaded. [Retry]" — toolbar stays interactive.
- Action errors (submit/approve/reject/receive fail): toast error (manual close) + row returns to prior status; nothing is lost because every mutation is server-authoritative.

**Keyboard**: search → each filter → Reset → New PO → rows: status cell is text (focusable summary if it carries the rejection reason) → action buttons in DOM order Approve → Reject → Receive. Row as a whole is not a click target; only real links/buttons are tabbable.

---

### 3.6 Journals — list with reverse confirm (`/[locale]/finance/journals`)

**Desktop**

```text
Finance ▸ Journals                                  [ + New journal ] .btn--primary
┌──────────────────────────────────────────────────────────────────────────────┐
│ [ Search… ] [Period ▾ 2026-11 ● OPEN] [Status ▾]                             │
├────────┬─────────┬────────┬──────────────────────┬──────────────┬────────────┤
│ JRNL#⇅ │ Period  │ Date ⇅ │ Description          │ Amount (BDT) │ Actions    │
│ J-0412 │ 2026-11 │ 21 Nov │ Rent accrual Nov     │   150,000.00 │ [Reverse]  │
│ J-0411 │ 2026-11 │ 21 Nov │ AP batch PO-101..104 │ 1,876,200.00 │ [Reverse]  │
│ J-0398 │ 2026-10 │ 05 Oct │ Depreciation Oct     │    92,410.00 │ Period     │
│        │         │        │ ● Posted             │              │ closed     │
└────────┴─────────┴────────┴──────────────────────┴──────────────┴────────────┘
Status column: ● Posted (badge--success) · ● Reversed (badge--danger) · ● Draft (badge--draft)
```

Locked-period rows show the reason as **text** ("Period closed") — a bare "—" plus gray styling would be color-alone status (principle 4).

**Reverse-confirm modal** (opened by `[Reverse]` on a Posted row in an open period):

```text
┌──────────────────────────────────────────────────────┐
│ Reverse journal J-0412?                          ✕   │
│ This will post a reversing entry and cannot be       │
│ undone. J-0412 (BDT 150,000.00, posted 21 Nov)       │
│ stays in the audit trail, linked as reversed-by.     │
│                                                      │
│ Reason *  [ __________________________________ ]     │
│ required — appears in the audit trail                │
│                                                      │
│ Period 2026-11 is open.                              │
│                                                      │
│ [ Cancel ]                 [ Post reversal ]         │
│  .btn--secondary           .btn--danger              │
└──────────────────────────────────────────────────────┘
```

If the period is closed the dialog is not reachable; the row itself explains why. If posting fails, the modal stays open with an inline `.banner--error` ("Reversal failed — try again. Nothing was posted."), draft state untouched.

**Mobile**: cards; Reverse opens the same modal full-width (`min-inline-size: min(480px, 92vw)` covers it).

**State placement**
- Loading / empty / error: same placements as §3.5, with module copy ("No journals yet — create your first journal").
- Success: modal closes, toast "Reversal J-0413 posted", row re-renders as ● Reversed with the reversal link, focus returns to the Reverse trigger (now gone — focus moves to the row's status cell).

---

## 4. Component CSS reference (`ui-kit.css`, ~150 lines)

Applies to the `components/uikit.tsx` class names. Plain CSS only. Every color resolves to a 03_UI_Spec hex; hovers shift luminance via `filter` (rule §2.3.5) — no new hex, no alpha mixes.

```css
/* ui-kit.css — TeamZlab ERP pilot. Layers: 1 primitive -> 2 semantic -> 3 component.
   Components reference layers 2-3 only. All colors from docs/03_UI_Spec.md §2. */

/* ── Layer 1 · primitives (raw spec values; never referenced by components) ── */
:root {
  --p-white:#FFFFFF;   --p-gray-50:#F6F7F9;  --p-gray-200:#D9DDE3;
  --p-gray-800:#1A1D23; --p-gray-600:#5A6070;
  --p-blue-600:#1F5EFF; --p-green-600:#0E8345; --p-amber-700:#B26A00;
  --p-red-600:#C21F2E;  --p-teal-700:#0B6E8A;
  --p-night-900:#0F1115; --p-night-800:#171A21; --p-night-700:#2A2F3A;
  --p-gray-100:#E8EAEF;  --p-gray-400:#9AA0AC;
  --p-blue-400:#5B8CFF;  --p-green-400:#3DBB6E; --p-amber-400:#E0A24A;
  --p-red-300:#F07178;   --p-teal-400:#58B7D4;
  --font-stack: system-ui, "Segoe UI", Roboto, "Noto Sans", "Noto Sans Bengali", sans-serif;
  --font-mono-stack: ui-monospace, "Cascadia Mono", "Segoe UI Mono", Menlo, monospace;
  --space-1:4px; --space-2:8px; --space-3:12px; --space-4:16px;
  --space-6:24px; --space-8:32px; --space-12:48px;          /* 03 §4 grid */
  --radius-1:2px; --radius-2:4px;                           /* grid-derived, non-color */
  --row:40px; --row-comfortable:48px; --target:24px; --target-touch:44px;
}

/* ── Layer 2 · semantic (names = 03_UI_Spec §2; dark via prefers-color-scheme) ── */
:root {
  --bg:var(--p-white); --surface:var(--p-gray-50); --border:var(--p-gray-200);
  --text:var(--p-gray-800); --text-muted:var(--p-gray-600);
  --accent:var(--p-blue-600); --success:var(--p-green-600); --warning:var(--p-amber-700);
  --danger:var(--p-red-600); --info:var(--p-teal-700);
}
@media (prefers-color-scheme: dark) { :root {
  --bg:var(--p-night-900); --surface:var(--p-night-800); --border:var(--p-night-700);
  --text:var(--p-gray-100); --text-muted:var(--p-gray-400);
  --accent:var(--p-blue-400); --success:var(--p-green-400); --warning:var(--p-amber-400);
  --danger:var(--p-red-300); --info:var(--p-teal-400);
} }

/* ── Layer 3 · component tokens (each choice justified in §2.2/§2.3) ── */
:root {
  --font-body:400 14px/20px var(--font-stack);
  --font-body-strong:600 14px/20px var(--font-stack);
  --font-mono:400 13px/20px var(--font-mono-stack);
  --font-caption:400 12px/16px var(--font-stack);
  --font-title:600 18px/26px var(--font-stack);
  --font-display:600 24px/32px var(--font-stack);
  --focus-ring:2px solid var(--accent);
  --btn-filled-fg:var(--p-white);      /* 5.12 on accent / 5.95 on danger, light */
  --control-border:var(--text-muted);  /* --border fails 1.4.11 as a control edge */
}
@media (prefers-color-scheme: dark) { :root {
  --btn-filled-fg:var(--p-night-900);  /* 5.97 on accent / 6.60 on danger, dark */
} }

/* focus — global, keyboard-only, never removed (03 §2) */
:where(a,button,input,select,textarea,summary,[tabindex]):focus-visible {
  outline:var(--focus-ring); outline-offset:2px;
}

/* ── .btn ── */
.btn { position:relative; display:inline-flex; align-items:center; justify-content:center;
  gap:var(--space-2); min-block-size:40px; padding-inline:var(--space-4);
  border:1px solid transparent; border-radius:var(--radius-2);
  font:var(--font-body-strong); cursor:pointer;
  background:var(--btn-bg,transparent); color:var(--btn-fg,var(--text)); }
.btn--primary   { --btn-bg:var(--accent); --btn-fg:var(--btn-filled-fg); }
.btn--danger    { --btn-bg:var(--danger); --btn-fg:var(--btn-filled-fg); }
.btn--secondary { --btn-fg:var(--text); background:var(--surface); border-color:var(--control-border); }
.btn--sm { min-block-size:32px; padding-inline:var(--space-3); font:var(--font-caption); }
.btn--block { inline-size:100%; min-block-size:44px; }      /* touch rows/cards */
.btn:hover:not(:disabled,.is-loading) { filter:brightness(.94); }
.btn--secondary:hover:not(:disabled,.is-loading) { filter:none; background:var(--border); }
.btn:active:not(:disabled,.is-loading) { filter:brightness(.88); }
.btn:disabled,.btn[aria-disabled="true"] { opacity:.5; cursor:not-allowed; filter:none; }
.btn.is-loading { color:transparent; pointer-events:none; }  /* spinner replaces label */
.btn.is-loading::after { content:""; position:absolute;
  inset-block-start:50%; inset-inline-start:50%; translate:-50% -50%;
  inline-size:16px; block-size:16px; border-radius:50%;
  border:2px solid var(--btn-filled-fg); border-block-start-color:transparent;
  animation:kit-spin .7s linear infinite; }
.btn--secondary.is-loading::after { border-color:var(--text); }

/* ── .input (+ field furniture) ── */
.field-label { display:block; font:var(--font-body-strong); margin-block-end:var(--space-1); }
.input { inline-size:100%; min-block-size:40px; padding-inline:var(--space-3);
  background:var(--bg); color:var(--text);
  border:1px solid var(--control-border); border-radius:var(--radius-2);
  font:var(--font-body); }
.input:hover:not(:disabled,:readonly) { border-color:var(--text); }
.input:focus-visible { outline:var(--focus-ring); outline-offset:0; }  /* ring hugs field */
.input:disabled { opacity:.5; background:var(--surface); cursor:not-allowed; }
.input[aria-invalid="true"] { border-color:var(--danger); }
.input[aria-invalid="true"]:focus-visible { outline-color:var(--danger); }
.field-error { font:var(--font-caption); color:var(--danger); margin-block-start:var(--space-1); }
.field-hint  { font:var(--font-caption); color:var(--text-muted); margin-block-start:var(--space-1); }

/* ── .badge — outline pattern: label in --text, dot+border carry status color ── */
.badge { display:inline-flex; align-items:center; gap:var(--space-2);
  padding-block:2px; padding-inline:var(--space-2); white-space:nowrap;
  border:1px solid var(--badge-accent); border-radius:var(--radius-1);
  background:transparent; color:var(--text); font:var(--font-caption); }
.badge::before { content:""; inline-size:8px; block-size:8px;
  border-radius:50%; background:var(--badge-accent); }
.badge--draft   { --badge-accent:var(--text-muted); }
.badge--pending { --badge-accent:var(--warning); }   /* never as text — §2.3.1 */
.badge--success { --badge-accent:var(--success); }
.badge--danger  { --badge-accent:var(--danger); }

/* ── .table — sticky header + sticky first column, zebra, row hover ── */
.table-wrap { overflow:auto; background:var(--bg);
  border:1px solid var(--border); border-radius:var(--radius-2); }
.table { inline-size:100%; border-collapse:separate; border-spacing:0; font:var(--font-body); }
.table th,.table td { text-align:start; padding-inline:var(--space-3);
  block-size:var(--row); border-block-end:1px solid var(--border); white-space:nowrap; }
.table thead th { position:sticky; inset-block-start:0; z-index:2;
  background:var(--surface); font:var(--font-body-strong); }
.table tbody tr:nth-child(even) { background:var(--surface); }
.table tbody tr:hover { background:var(--surface);
  outline:1px solid var(--control-border); outline-offset:-1px; }
.table :is(td,th):first-child { position:sticky; inset-inline-start:0;
  z-index:1; background:inherit; }
.table thead th:first-child { z-index:3; }
.num { font:var(--font-mono); text-align:end; font-variant-numeric:tabular-nums; }
.table-wrap[data-density="comfortable"] :is(td,th) { block-size:var(--row-comfortable); }

/* ── .tile — stat tile, text tokens only ── */
.tile { display:grid; gap:var(--space-1); padding:var(--space-4);
  background:var(--bg); border:1px solid var(--border); border-radius:var(--radius-2); }
.tile__label { font:var(--font-caption); color:var(--text-muted); }
.tile__value { font:var(--font-display); font-variant-numeric:tabular-nums; }
.tile__delta { font:var(--font-caption); color:var(--text-muted); }
.tile__delta[data-dir="up"]   { color:var(--success); }  /* markup keeps ▲ + words */
.tile__delta[data-dir="down"] { color:var(--danger); }

/* ── .skeleton ── */
.skeleton { inline-size:100%; block-size:14px; border-radius:var(--radius-1);
  background:linear-gradient(90deg,var(--surface) 35%,var(--border) 50%,var(--surface) 65%);
  background-size:200% 100%; animation:kit-wave 1.4s ease-in-out infinite; }
.skeleton--tile { block-size:32px; inline-size:96px; }
.skeleton--row  { block-size:40px; margin-block:var(--space-2); border-radius:var(--radius-2); }

/* ── .banner — error/info states required by the 4-state rule (03 §5) ── */
.banner { display:flex; align-items:center; gap:var(--space-2);
  padding:var(--space-3) var(--space-4); color:var(--text);
  background:var(--surface); border:1px solid var(--banner-accent);
  border-inline-start-width:4px; border-radius:var(--radius-2);
  font:var(--font-body); }
.banner--error { --banner-accent:var(--danger); }
.banner--info  { --banner-accent:var(--info); }

/* ── .modal / .toast shells (behavior in §5) ── */
.modal-scrim { position:fixed; inset:0; background:var(--text); opacity:.5; }
.modal { position:fixed; inset-block-start:50%; inset-inline-start:50%; translate:-50% -50%;
  min-inline-size:min(480px,92vw); padding:var(--space-6);
  background:var(--bg); border:1px solid var(--control-border); border-radius:var(--radius-2); }
.toast-region { position:fixed; inset-block-end:var(--space-4); inset-inline-end:var(--space-4);
  display:grid; gap:var(--space-2); z-index:50; }

@keyframes kit-spin { to { rotate:360deg; } }
@keyframes kit-wave { from { background-position:200% 0; } to { background-position:-200% 0; } }

@media (prefers-reduced-motion: reduce) {
  .skeleton { animation:none; background:var(--border); }
  .btn.is-loading::after { animation:none; }
  .modal, .toast-region > * { transition:none; }
}
```

Implementation notes for `components/uikit.tsx`:
- Keep the class names as above; variant modifiers are single-purpose (`--primary`, `--secondary`, `--danger`, `--sm`, `--block`; badge `--draft/--pending/--success/--danger`).
- Density toggle = `data-density="comfortable"` on `.table-wrap`; default is dense 40px (03 §4).
- Below 768px the list markup switches from `.table` to stacked cards per 03 §4 — this is a markup concern, the CSS above stays desktop-oriented.
- Nothing here uses `margin-left`/`left`/`right`/`text-align: left`; grep for those in review as a lint step.

---

## 5. Micro-interactions

### 5.1 Focus
- `:focus-visible` only — mouse clicks never show rings. Global rule in §4 (2px `accent` + 2px offset), inputs override to 0 offset so the ring hugs the field.
- The ring is never removed anywhere; `outline: none` is a lint error. Dark theme ring = `accent` #5B8CFF, 5.97:1 against `bg`.
- Focus order matches reading order on every screen (per-screen orders in §3); skip link is tab stop 1 on authenticated pages.

### 5.2 Hover / active
- Filled buttons: `brightness(.94)` hover, `brightness(.88)` active — luminance-only, hue-stable, AA-safe (label contrast only improves). Secondary: fill shifts `surface` → `border`.
- Table rows: hover = `surface` fill + 1px inset ring, so hover reads on both zebra and non-zebra rows. Clickable rows also change cursor and expose a real link/button in the row.
- Tiles and static cards: no hover state — they are not clickable; only their inner links are.

### 5.3 Button loading
- On invoke: class `is-loading` + `aria-busy="true"`; label goes transparent (width preserved — zero layout shift) and the 16px spinner replaces it (03 §5 "spinner replaces label"). `pointer-events: none` + disabled state block double-submit.
- Keep the loading state visible ≥ 400ms even for instant responses, so the state change never flickers.
- Resolution: success → button restores, toast confirms; failure → button restores, toast error (manual close) + inline `.banner--error` where the blueprint says so.
- `prefers-reduced-motion`: spinner is a static ring; the state is still announced via `aria-busy` on the control and its live region.

### 5.4 Toasts
- Region: fixed bottom inline-end (mirrors in RTL); max 3 visible, oldest drops.
- Success/info: auto-dismiss 5s (03 §5), countdown **pauses** on hover/focus (WCAG 2.2.1 Timing Adjustable).
- Error: **manual close only** — never auto-dismisses. Close buttons are ≥ 24px (44px touch).
- Semantics: success = `role="status"`, error = `role="alert"` (4.1.3 Status Messages). Enter: 160ms translate+fade; exit: 120ms fade; reduced-motion: opacity only.

### 5.5 Modals / drawers
- Open: background becomes `inert`, `aria-modal="true"`, focus moves to the first interactive control (title carries `tabindex="-1"` and is named by `aria-labelledby`).
- Tab / Shift+Tab cycle inside the dialog only — no keyboard trap on close (2.1.2): Esc always exits unless a submission is in flight.
- Esc and scrim-click close; if the form is dirty, they route through the unsaved-changes confirm (5.6) instead.
- Close returns focus to the invoking element; if that element no longer exists (row re-rendered), focus moves to the nearest surviving row/cell.

### 5.6 Unsaved-changes guard
- Any form/drawer tracks dirty state from the first input change (including IME composition — never guards on keystrokes).
- Guarded exits: Esc, scrim click, nav away, browser back (02 §1 "Back never loses unsaved form data without confirm") — in-app via a history guard, plus `beforeunload` for the hard exit.
- Confirm copy: "You have unsaved changes — leaving now will discard them." `[Keep editing]` (primary, receives focus — safe default) / `[Discard and leave]` (secondary). Discard never fires the danger styling: leaving is already the destructive act; the safe action must win the visual hierarchy.

---

## 6. Bengali (bn) typography

- **Stack**: `system-ui → "Segoe UI" → Roboto → "Noto Sans" → "Noto Sans Bengali" → sans-serif` (already in §4 `--font-stack`). Bengali is its own script — Devanagari coverage does **not** render it, so Noto Sans Bengali must be in the catalog (D-07). Self-host weights 400/600 (CDN reachability inside Bangladesh is unreliable).
- **Line height**: Bengali matras and conjuncts (যুক্তাক্ষর) clip in the spec's 20px box at 14px. Under `:lang(bn)`, re-point the layer-3 font tokens — components never change:

```css
:lang(bn) {
  --font-body:400 14px/22px var(--font-stack);
  --font-body-strong:600 14px/22px var(--font-stack);
  --font-caption:400 12px/18px var(--font-stack);
  --font-title:600 18px/28px var(--font-stack);
  --font-display:600 24px/34px var(--font-stack);
}
```

- **Badges/captions**: min line-height 1.5 under `:lang(bn)`; never apply `letter-spacing` or `text-transform` to Bengali (no case axis; tracking breaks matras).
- **Digits/amounts**: always `Intl.NumberFormat`, never hand-built strings. Pilot decision: `bn-BD-u-nu-latn` — Latin digits — because `--font-mono` stacks carry no Bengali glyphs and dense tables need tabular figures. Pure Bengali numerals (১২,৩৪৫) via `nu-beng` are the locale-pure option deferred to D-07.
- **Dates**: `Intl.DateTimeFormat(locale)` only; the Bengali calendar (`-u-ca-beng`) is out of pilot scope — Gregorian everywhere.
- **Mixed content**: Latin codes (PO-102, BIN) inside Bengali text are wrapped in `<span lang="en">` and rendered in `mono`.
- **Input/IME**: validate on submit, never per keystroke — Bengali phonetic keyboards emit composition events that naive per-key validation corrupts.

---

## 7. Accessibility checklist per screen

| Screen | Labels & semantics | Contrast pairs (from §2.2) | Tab order | WCAG 2.2 specifics |
|---|---|---|---|---|
| Login | Radios are native `<input type="radio">` in a `fieldset` with legend; Continue is a real `<button>` | 1, 3, 5, 6 | locale → radios (↑↓) → Continue | 3.3.8 Accessible Authentication (picker, no cognitive puzzle); 2.5.8 targets ≥ 24px |
| Select-entity | Both selects have visible `<label>`s; disabled entity select announces why; period status = text + dot | 1, 3, 20, 25 | org → entity → Continue | 3.3.2 Labels or Instructions; 3.3.3 Error Suggestion (empty-entities state) |
| Dashboard | Each tile is one focusable element with a full-text alternative; nav is `<nav>` with `aria-current="page"`; mini-tables are real `<table>`s | 1, 3, 13, 14, 15, 16 | skip → top bar → nav → tiles → View all → rows | 2.4.11 Focus Not Obscured (sticky header never covers focus); 1.4.10 reflow at 320px |
| Suppliers list | Search has label; sort buttons in `th` with `aria-sort`; status = badge label + dot | 1, 2, 3, 4, 19, 20, 26 | search → filters → Reset → New → rows | 1.4.10 (table region scrolls internally, page doesn't); 4.1.3 (result-count live region on filter) |
| Supplier form | Every input has a visible label; errors via `aria-describedby` + `aria-invalid`; Code is readonly with hint | 1, 3, 14, 25 | field order → Cancel → Save | 3.3.7 Redundant Entry (no re-typing known values); 2.4.3 focus to first error on failed submit; Esc/back guarded (02 §1) |
| PO list | Status cell always has label text; disabled Approve shows reason in visible text (`aria-disabled`, not `disabled`, so it stays reachable) | 1, 3, 13–16, 19–21 | search → filters → New → rows → actions | 1.4.1 Use of Color (label + dot); 2.5.8 (sm buttons 32px desktop / 44px touch); 4.1.3 (status-change toasts) |
| Journals | Reverse modal: `aria-labelledby` title, reason required with label, locked-period reason is visible text | 1, 2, 3, 7, 9, 14, 26 | row Reverse → title → reason → Cancel → Post reversal (trapped) | 2.1.2 No Keyboard Trap (Esc exits); focus returns to trigger; consequences stated before submit (principle 7) |
| All | Skip link first; `lang` switches with locale; error copy never shows raw API codes (03 §6) | — | skip → top bar → nav → main | 2.4.7 / 2.4.11 focus; 1.4.12 text spacing survives; 3.2.6 Consistent Help (help entry stays top-bar-right) |

---

## 8. Conflicts and open questions (02_AppFlow vs 03_UI_Spec vs task brief)

1. **03_UI_Spec's own contrast claim is false for `warning`.** §2 says "all text pairs ≥ 4.5:1", but warning #B26A00 is 4.24:1 on white and 3.95:1 on surface. Resolved here by rule §2.3.1 (warning never used as text) — token values unchanged.
2. **`border` fails WCAG 1.4.11 as a control edge** (1.36:1 on white) while the spec assigns it to "outlines". Resolved: `border` for dividers/zebra only; controls use `text-muted` (§2.3.3).
3. **02_AppFlow 3.1 includes a `Send` step (approved → sent) but the task's per-status action map has no Send button** — it specifies Receive on both Approved and Sent. Blueprints follow the task's map; if Send is still a distinct user action, Approved rows need a second `[Send]` button (same pattern, one more row in the §3.5 table).
4. **Badge naming differs**: 03_UI_Spec §5 "approved-posted / rejected-locked" vs the task's `success / danger` variants. Mapped 1:1 (approved-posted → `--success`, rejected-locked → `--danger`); uikit keeps the task's names.
5. **Login**: 02_AppFlow specifies IdP auth (D-13 TBD); the pilot requires a 3-user picker. Blueprint marks the picker as a pilot-mode shim, to be replaced by the IdP without layout changes.
6. **Fonts**: 03_UI_Spec's fallback note lists "CJK/Arabic/Devanagari" but not Bengali script, while the pilot's primary locale includes `bn`. Noto Sans Bengali added (§6); D-07 must include it.
7. **`mono` amounts vs Bengali digits**: `mono` 13/20 is mandated for amounts, but mono stacks carry no Bengali glyphs. Resolved with `bn-BD-u-nu-latn` for the pilot (§6); D-07 decides the locale-pure mode.
8. **Toast 5s auto-dismiss vs WCAG 2.2.1**: satisfied by pausing on hover/focus and by the errors-are-manual rule — noted so the 5s constant isn't "fixed" by removing it.
