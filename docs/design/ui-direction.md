# UI/UX Design Direction — TeamZlab ERP

> Status: **ADOPTED 2026-11-24 (v2 — exact tokens)** — owner reference: the TailwindAdmin
> React theme, with its extracted token system in
> `G:\Practices\clone site as json\website-analysis\design-tokens.json` (shadcn/ui-style,
> light + dark). Values below are EXTRACTED, not invented — do not change them without a
> new reference. This file is the synced instruction file for ALL web UI work. When code
> and this file disagree, fix the code or update this file in the same commit.
> Implementation: `apps/web/app/globals.css` (tokens + component classes),
> `components/uikit.tsx` (React primitives), `app/[locale]/layout.tsx` (app shell, DM Sans),
> Tailwind CSS v4 for utilities.

---

## 1. Design principles

1. **Dark is the default theme.** Mechanism = class toggle on `<html>`: `html.dark` /
   `html.light` (reference convention), persisted to `localStorage("theme")`, applied
   pre-paint by the layout's inline script. Components never branch on theme — only tokens flip.
2. **Flat surfaces.** Cards carry NO border and NO shadow — the background contrast
   separates them (reference: cards/buttons/td border 0, box-shadow none). Inputs are the
   exception: 1px `--input` border + `shadow-sm`. Popovers/modals sit on `--popover` with a
   soft drop shadow.
3. **Every screen renders four states: loading / empty / error / success.** Fixed placement,
   never improvised spinners.
4. **WCAG 2.2 AA is the floor.** Text ≥ 4.5:1, boundaries ≥ 3:1, focus ring never removed
   (2px `--ring` offset 2), targets ≥ 24px, reflow at 320px, no keyboard traps.
5. **Status = pill + label, never color alone.** Solid pills, white text on tone.
6. **RTL-safe by construction.** Only logical properties. (bn renders `dir="ltr"` — flipping
   is a one-line layout decision awaiting the owner.)
7. **Tokens only.** shadcn-style semantic layer (`--background --foreground --card --popover
   --primary --secondary --muted --muted-foreground --accent --destructive --border --input
   --ring --sidebar --success --warning --info`) + legacy aliases; Tailwind utilities
   bridged via `@theme inline`. No raw hex below the token layer.
8. **Irreversible actions state consequences before submit.**
9. **Maker–checker is enforced in the UI**, not just the API.
10. **Numbers are data.** IDs/amounts in mono, `Intl`-formatted, end-aligned.

## 2. Palette (extracted from the reference — exact)

| Token | Light | Dark |
|---|---|---|
| `--background` / `--foreground` | `#FFFFFF` / `#1C2536` | `#1C2536` / `#FFFFFF` |
| `--card` (same as background) | `#FFFFFF` | `#1C2536` |
| `--popover` | `#FFFFFF` | `#333F55` |
| `--primary` (+ `--primary-foreground` #FFF) | `#5D87FF` | `#5D87FF` |
| `--secondary` | `#49BEFF` | `#49BEFF` |
| `--muted` / `--muted-foreground` | `#F5F5F5` / `#5A6A85` | `#2A3851` / `#7C8FAC` |
| `--border` / `--input` | `#DFE5EF` / `#E5E7EB` | `#333F55` / `#333F55` |
| `--ring` | `#5D87FF` | `#5D87FF` |
| `--sidebar` / `--sidebar-foreground` | `#FFFFFF` / `#2A3547` | `#1C2536` / `#7C8FAC` |
| `--success` | `#13DEB9` | `#13DEB9` |
| `--warning` | `#F6B51E` | `#F6B51E` |
| `--destructive` | `#EF4444` | `#EF4444` |
| `--info` | `#8754EC` | `#8754EC` |
| charts 1–5 | primary, secondary, success, warning, destructive | same |

Type: **DM Sans** variable (next/font, latin subset; Bengali falls back to Noto Sans
Bengali via the stack). Body 14px/20. Page titles 24px/700, card titles 16px/600,
captions 12px muted. Radius **10px** everywhere (`--radius`); pills 999px. Cards/buttons
have **no shadow**; inputs carry `shadow-sm`; popovers/modals a soft shadow.

## 3. App shell

```
┌────────────┬──────────────────────────────────────────┐
│ sidebar    │ topbar: app title · locale · theme ◐     │
│ 248px      ├──────────────────────────────────────────┤
│ brand      │ content (max 1200px)                     │
│ OVERVIEW   │   page header (title + caption + CTAs)   │
│ Procurement│   KPI tile row (1/2/4 cols by breakpoint)│
│ Inventory  │   cards / tables / forms                 │
│ Finance    │                                          │
│ System     │                                          │
└────────────┴──────────────────────────────────────────┘
```

- Sidebar ≥1024px only; below it hides (topbar carries a compact nav). Groups: Overview,
  Procurement, Inventory, Finance, System. Active link = accent tint + inset accent bar,
  `aria-current="page"`.
- Topbar is sticky; actions: locale switch, theme toggle.
- Content column: `max-inline-size: 1200px`, 24/20px padding.

## 4. Component classes (globals.css)

| Class | Spec |
|---|---|
| `.card` + `.card__title/.card__subtitle` | surface fill, 1px border, 14px radius, 20px padding |
| `.kpi` + `__label/__value/__delta` | tile: label 12px muted, value 26px/700, delta colored + ▲/▼ |
| `.btn` (primary) / `--secondary` / `--danger` / `--sm` | accent fill, 10px radius, 600 weight; secondary = transparent + border |
| `.badge` (solid pill) | white text on tone: `--success/--warning/--danger/--info/--muted` modifiers; label text always present |
| `.table-scroll` > `.table` | bordered surface card; th 12px muted on `--surface-2`; rows hover `--surface-2`; mobile collapse via `data-label` |
| `.field` + `.input/.textarea` | label 600, hint/error 12px, focus = accent outline + border, 10px radius |
| `.modal` + `.modal-scrim` | scrim #050a12/60, dialog = surface card, focus trap in `uikit.tsx` |
| `.toast` (region bottom inline-end) | surface card, 4px accent inline-start edge, `role=alert` for errors |
| `.tabs__list [role=tab]` | accent underline on selected, roving tabindex |
| `.pagination__current` | accent-bordered chip |
| `.auth` / `.auth__card` / `.auth__row` | centered 460px login card; role rows = ledger list (role 600 + mono email + aligned action) |
| `.state` / `.state--error` | centered empty/error states; error = danger-tinted card + retry |
| `.skeleton__row` | shimmer at `--surface-2/--border`; disabled under reduced motion |

## 5. Screen checklist (every screen)

1. Page header: title + one-line caption (+ primary action, right).
2. Four states designed: loading (`.skeleton`), empty (`.state` + CTA), error (`.state--error`
   + retry), success (the content).
3. Tables: `.table-scroll > .table`, `data-label` on every td, row actions in `.row-actions`.
4. Forms: `.form-grid` + `.field`; validation errors inline via `.field__error`.
5. i18n: no hardcoded copy — every string through `lib/i18n/{en,bn}.ts` with key parity.
6. Create/view through `services/` — no raw fetch in components.
